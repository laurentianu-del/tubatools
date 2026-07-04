# Download Queue Skill — 图吧工具箱统一下载队列

## Overview

TubaWinUi3 has a unified download queue system (`DownloadQueueService`) that provides a single entry point for all download operations. A button in the title bar with an InfoBadge shows the active download count; clicking it opens a Flyout listing all downloads with real-time progress.

All new download operations **must** use `DownloadQueueService.Enqueue()` or `EnqueueWithResolver()`. The legacy per-service download loops (in `ToolDownloaderService`, `ToolsBundleService`) are being gradually migrated. `UpdateService` has been migrated — it now uses `EnqueueUpdateDownload()` which delegates to `DownloadQueueService`.

## Architecture

```
DownloadQueueService (static)          ← single queue, SemaphoreSlim(2) concurrency
  ├─ Enqueue(directUrl)               ← known URL, immediate download
  ├─ EnqueueWithResolver(resolver)    ← async URL resolution (gh:, gc: prefixes)
  ├─ Cancel / Retry / Remove / ClearCompleted
  ├─ Queue: ObservableCollection<DownloadItem>
  └─ QueueChanged event               ← triggers badge update in MainWindow

DownloadItem (model)                   ← INotifyPropertyChanged for live UI binding
  ├─ State machine: Queued → Resolving → Downloading → Processing → Completed
  ├─                                  └→ Failed / Cancelled
  ├─ Progress: DownloadQueueProgress record
  └─ PostProcessor: IDownloadPostProcessor? (optional)

IDownloadPostProcessor                 ← extensibility point for post-download logic
   ├─ ArchiveExtractProcessor           ← unzip + delete archive
   ├─ InstallerLaunchProcessor          ← Process.Start .exe installer
   ├─ MoveToDestinationProcessor        ← move file to destination
   ├─ UpdateInstallProcessor            ← app update: launch .exe installer+exit, or open folder for .zip
   ├─ DelegatePostProcessor             ← wrap a lambda Func
   └─ ChainedPostProcessor              ← chain multiple processors sequentially
```

## Key Files

| File | Role |
|------|------|
| `Models/DownloadQueueModels.cs` | `DownloadQueueProgress`, `DownloadItemState`, `DownloadItem`, `ResolvedDownloadUrl`, `IDownloadPostProcessor` + built-in processors |
| `Services/DownloadQueueService.cs` | Core service: enqueue, download loop, concurrency, format helpers |
| `Services/UpdateService.cs` | `EnqueueUpdateDownload()` — update download via queue (legacy `DownloadUpdateAsync`/`DownloadFromGitCodeAsync` marked `[Obsolete]`) |
| `Pages/DownloadQueueFlyout.xaml` | Flyout UI with card-based queue display |
| `Pages/DownloadQueueFlyout.xaml.cs` | `DownloadItemViewModel` wrapper, button handlers, collection sync |
| `MainWindow.xaml` | Title bar download button (`&#xE896;`) + `InfoBadge` |
| `MainWindow.xaml.cs` | Flyout creation, `QueueChanged` subscription, badge update |

## API Quick Reference

### Enqueue a direct URL download

```csharp
DownloadQueueService.Enqueue(
    displayName: "CPU-Z",
    downloadUrl: "https://example.com/cpuz.zip",
    destinationPath: destDir,
    postProcessor: new ArchiveExtractProcessor(),
    description: "x64 版本",
    glyph: "\uEEA1"          // optional, auto-falls-back to state glyph
);
```

### Enqueue with async URL resolution (for `gh:` / `gc:` prefixes)

```csharp
DownloadQueueService.EnqueueWithResolver(
    displayName: "GPU-Z",
    urlResolver: async ct => {
        var info = await ToolDownloaderService.ResolveDownloadUrlAsync("gh:TechPowerUp/GPU-Z", ct);
        return new ResolvedDownloadUrl(info!.DownloadUrl, info.FileName, info.Size);
    },
    destinationPath: destDir,
    postProcessor: new ArchiveExtractProcessor(),
    glyph: "\uF211"
);
```

### Custom post-processing with DelegatePostProcessor

```csharp
DownloadQueueService.Enqueue(
    displayName: "工具包",
    downloadUrl: url,
    destinationPath: destDir,
    postProcessor: new DelegatePostProcessor("解压并刷新缓存", async (file, dest, progress, ct) => {
        progress?.Report("正在解压...");
        await ToolDownloaderService.ExtractArchiveAsync(file, dest, ct);
        progress?.Report("正在刷新工具目录...");
        ToolCatalog.InvalidateCache();
    })
);
```

### Chain multiple post-processors

```csharp
DownloadQueueService.Enqueue(
    displayName: "工具包",
    downloadUrl: url,
    destinationPath: destDir,
    postProcessor: new ChainedPostProcessor("解压并安装",
        new ArchiveExtractProcessor(),
        new DelegatePostProcessor("刷新缓存", (file, dest, progress, ct) => {
            ToolCatalog.InvalidateCache();
            return Task.CompletedTask;
        }))
);
```

### Control methods

```csharp
DownloadQueueService.Cancel(itemId);          // cancel active/queued download
DownloadQueueService.Retry(itemId);           // retry failed/cancelled download
DownloadQueueService.Remove(itemId);          // remove from queue (cancels if active)
DownloadQueueService.ClearCompleted();        // remove all completed/failed/cancelled
```

### Format helpers (shared, no need to duplicate)

```csharp
DownloadQueueService.FormatSize(bytes);       // "12.5 MB"
DownloadQueueService.FormatSpeed(mbps);       // "45.23 Mbps"
DownloadQueueService.FormatTime(timespan);    // "2m 30s"
```

## DownloadItem State Machine

```
         Enqueue()
            │
            ▼
         Queued ──────Cancel()──→ Cancelled
            │
        (semaphore acquired)
            │
            ▼
        Resolving ────Cancel()──→ Cancelled
            │
        (URL resolved)
            │
            ▼
       Downloading ───Cancel()──→ Cancelled
            │              │
        (download       (error)
         complete)         │
            │              ▼
            ▼            Failed ──Retry()──→ Queued
        Processing
            │
     (post-processor
      complete)
            │
            ▼
        Completed
```

## UI Behavior

- **Title bar button**: `&#xE896;` (Download icon) with `InfoBadge` showing pending count
- **InfoBadge**: Visible when `PendingCount > 0`, hidden when 0; max displayed value is 99
- **Flyout**: `Flyout` control (not ContentDialog) — non-blocking, closes on outside click
- **Each card** shows: glyph + name + description, state badge, progress bar, status line, action buttons
- **State badges**: 排队中 / 解析中 / 下载中 / 处理中 / 已完成 / 失败 / 已取消
- **Progress bar**: Determinate during download, indeterminate during resolve/processing, red on error
- **Action buttons per state**:
  - Queued/Resolving/Downloading/Processing → Cancel
  - Failed/Cancelled → Retry
  - Completed → Open folder
  - Completed/Failed/Cancelled → Remove (✕)

## Naming Convention

- **`DownloadQueueProgress`** — the unified progress type (renamed from `DownloadProgress` to avoid collision with `Models.UpdateInfo.DownloadProgress` used by `UpdateService`)
- **`ToolDownloadProgress`** — legacy type from `ToolDownloaderService`, still used by existing callers
- **`ToolsBundleProgress`** — legacy type from `ToolsBundleService`
- When migrating a service, convert its progress type to `DownloadQueueProgress` and use `Enqueue()` instead of its own download loop

## Migration Guide (for existing services)

### Pattern: Replace direct download loop

**Before** (e.g. in `ToolDownloadDialog`):
```csharp
var filePath = await ToolDownloaderService.DownloadToFileAsync(url, dir, name, progress, ct);
await ToolDownloaderService.ExtractArchiveAsync(filePath, dir, ct);
```

**After**:
```csharp
DownloadQueueService.Enqueue(displayName, url, dir, new ArchiveExtractProcessor(), glyph: glyph);
// Download runs in background; user sees progress in the queue flyout
```

### Pattern: Replace URL resolution + download

**Before** (e.g. in `HomePage.ShowDownloadDialogAsync`):
```csharp
var info = await ToolDownloaderService.ResolveDownloadUrlAsync(tool.DownloadUrl, ct);
var dialog = new ToolDownloadDialog(tool, info.DownloadUrl, destDir);
await dialog.ShowAsync();
```

**After**:
```csharp
DownloadQueueService.EnqueueWithResolver(tool.Name, async ct => {
    var info = await ToolDownloaderService.ResolveDownloadUrlAsync(tool.DownloadUrl, ct);
    return new ResolvedDownloadUrl(info!.DownloadUrl, info.FileName, info.Size);
}, destDir, new ArchiveExtractProcessor(), glyph: tool.Glyph);
```

### Pattern: Special post-processing (e.g. ToolsBundle backup-restore)

```csharp
DownloadQueueService.Enqueue("工具包", bundleUrl, toolsRoot,
    new DelegatePostProcessor("替换工具目录", async (file, dest, progress, ct) => {
        progress?.Report("正在备份旧目录...");
        // backup/restore logic here
        progress?.Report("正在解压...");
        await ToolDownloaderService.ExtractArchiveAsync(file, dest, ct);
        ToolCatalog.InvalidateCache();
    }));
```

## Concurrency

- Max 2 simultaneous downloads (SemaphoreSlim)
- Additional items wait in `Queued` state
- FIFO ordering — first enqueued, first served
- Each download has its own CancellationTokenSource

## Error Handling

- Network errors → `Failed` state with error message displayed in card
- Cancellation → `Cancelled` state
- Post-processor exceptions → `Failed` state with inner exception message
- User can retry failed/cancelled items

## Do NOT

- Create new `HttpClient` instances for downloads — `DownloadQueueService` has a shared one
- Implement new download progress types — use `DownloadQueueProgress`
- Show modal dialogs during download — the Flyout is non-blocking by design
- Block the UI thread — all download/processing runs on `Task.Run`
