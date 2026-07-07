# Download Queue Pattern

A reusable download queue with progress tracking, pause/resume, post-processing, and user feedback
via TeachingTip anchored to the triggering button.

---

## Architecture Overview

```
DownloadQueueService (static)
  ├─ Enqueue() / EnqueueWithResolver() → adds to ObservableCollection<DownloadItem>
  ├─ Max 2 concurrent downloads (SemaphoreSlim)
  ├─ Pause / Resume / Cancel / Retry / Remove
  ├─ QueueChanged event → MainWindow updates InfoBadge count
  ├─ Persisted to JSON via ConfigManager
  └─ Post-processing pipeline (IDownloadPostProcessor)

DownloadQueueFlyout (UserControl in Flyout)
  ├─ Shown from MainWindow title bar download button
  ├─ ItemsRepeater bound to DownloadItemViewModel[]
  └─ Per-item: ProgressBar + Pause/Resume/Cancel/Retry/OpenFolder/Remove

MainWindow TitleBar
  ├─ DownloadQueueButton (FontIcon &#xE896;)
  └─ DownloadQueueBadge (InfoBadge with pending count)

TeachingTip Feedback
  └─ Shown from EnqueueDownload() → anchored to clicked button
```

---

## 1. DownloadQueueService — Core Service

```csharp
// Services/DownloadQueueService.cs
public static class DownloadQueueService
{
    private const int MaxConcurrentDownloads = 2;
    private static readonly SemaphoreSlim _semaphore = new(MaxConcurrentDownloads);
    private static readonly ObservableCollection<DownloadItem> _queue = [];

    public static ObservableCollection<DownloadItem> Queue => _queue;
    public static event Action? QueueChanged;
    public static int PendingCount => _pendingCount;

    public static void Initialize(DispatcherQueue dq) { ... }

    // Direct URL download
    public static DownloadItem Enqueue(
        string displayName,
        string downloadUrl,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null,
        string? glyph = null,
        object? tag = null) { ... }

    // Deferred URL resolution (e.g., winget, API-based)
    public static DownloadItem EnqueueWithResolver(
        string displayName,
        Func<CancellationToken, Task<ResolvedDownloadUrl>> urlResolver,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null,
        string? glyph = null,
        object? tag = null) { ... }

    public static void Pause(string itemId) { ... }
    public static void Resume(string itemId) { ... }
    public static void Cancel(string itemId) { ... }
    public static void Retry(string itemId) { ... }
    public static void Remove(string itemId) { ... }
    public static void ClearCompleted() { ... }
}
```

---

## 2. Enqueue + TeachingTip Feedback (from a secondary Window)

When a download is enqueued from a page or secondary window, show a `TeachingTip`
anchored to the clicked button so the user gets **immediate, in-context feedback**.

```xaml
<!-- Add TeachingTip to the page/window root Grid (same row as content) -->
<TeachingTip
    x:Name="QueueTeachingTip"
    IsLightDismissEnabled="True"
    PreferredPlacement="Bottom"
    CloseButtonContent="知道了" />
```

```csharp
// The EnqueueDownload method accepts the button as target
private void EnqueueDownload(WindowsImageEntry entry, FrameworkElement? target = null)
{
    var destDir = WindowsImageService.GetDownloadDir();
    DownloadQueueService.Enqueue(
        entry.DisplayName,
        entry.DownloadUrl,
        destDir,
        postProcessor: null,
        description: $"{entry.Language} | {entry.Arch} | {entry.SizeDisplay}",
        glyph: "\uE896");

    // InfoBar for persistent status (optional)
    StatusInfoBar.Title = "已加入下载队列";
    StatusInfoBar.Message = $"{entry.DisplayName} 正在下载至 {destDir}";
    StatusInfoBar.Severity = InfoBarSeverity.Success;
    StatusInfoBar.IsOpen = true;

    // TeachingTip for immediate visual feedback — tells user where to check progress
    ShowQueueTip(entry.DisplayName, target);
}

private void ShowQueueTip(string name, FrameworkElement? target)
{
    QueueTeachingTip.Title = "已加入下载队列";
    QueueTeachingTip.Subtitle = $"{name}\n点击主页搜索框旁的下载按钮可查看进度";
    QueueTeachingTip.IconSource = new SymbolIconSource { Symbol = Symbol.Download };
    QueueTeachingTip.Target = target;
    QueueTeachingTip.IsOpen = true;
}
```

Wire up from click handlers:

```csharp
private void DownloadBtn_Click(object sender, RoutedEventArgs e)
{
    if (sender is not Button { Tag: WindowsImageEntry entry } btn) return;
    EnqueueDownload(entry, btn);
}

private void MsDownloadBtn_Click(object sender, RoutedEventArgs e)
{
    if (_msResolvedEntry is null) return;
    EnqueueDownload(_msResolvedEntry, sender as FrameworkElement);
}
```

---

## 3. MainWindow — Title Bar Download Button + Badge

```xaml
<!-- Inside TitleBar.Content Grid -->
<Button
    x:Name="DownloadQueueButton"
    Grid.Column="2"
    Click="DownloadQueueButton_Click"
    VerticalAlignment="Center"
    Width="34" Height="30"
    Padding="0"
    BorderThickness="0"
    Background="Transparent"
    AutomationProperties.Name="下载队列">
    <Grid>
        <FontIcon FontSize="14" Glyph="&#xE896;" />
        <InfoBadge
            x:Name="DownloadQueueBadge"
            Value="0"
            Visibility="Collapsed"
            VerticalAlignment="Top"
            HorizontalAlignment="Right"
            Margin="0,-4,-4,0" />
    </Grid>
</Button>
```

```csharp
// MainWindow.xaml.cs
private Flyout? _downloadFlyout;

private void DownloadQueueButton_Click(object sender, RoutedEventArgs e)
{
    if (_downloadFlyout is null)
    {
        _downloadFlyout = new Flyout { Content = new DownloadQueueFlyout() };
    }
    _downloadFlyout.ShowAt(DownloadQueueButton);
}

private void OnDownloadQueueChanged()
{
    var count = DownloadQueueService.PendingCount;
    DownloadQueueBadge.Value = count;
    DownloadQueueBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
}
```

---

## 4. DownloadQueueFlyout — Queue UI

A `UserControl` shown inside a `Flyout`. Uses `ItemsRepeater` bound to
`ObservableCollection<DownloadItemViewModel>`.

```xaml
<UserControl x:Class="TubaWinUi3.Pages.DownloadQueueFlyout">
    <StackPanel Width="380" MaxHeight="480" Spacing="0">
        <!-- Header: title + clear all -->
        <Grid Padding="16,12,12,8">
            <TextBlock FontSize="14" FontWeight="SemiBold" Text="下载队列" />
            <HyperlinkButton x:Name="ClearAllButton" Click="ClearAllButton_Click" Content="全部清除" />
        </Grid>

        <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="420">
            <ItemsRepeater x:Name="QueueRepeater" ItemsSource="{x:Bind Items}">
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate>
                        <Border Padding="12,10" Margin="0,4" CornerRadius="8"
                                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                                BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                                BorderThickness="1">
                            <Grid RowSpacing="6">
                                <!-- Row 0: icon + name + state badge -->
                                <!-- Row 1: ProgressBar -->
                                <!-- Row 2: status line + action buttons -->
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>
        </ScrollViewer>

        <!-- Empty state -->
        <StackPanel x:Name="EmptyState" HorizontalAlignment="Center" Spacing="8">
            <FontIcon FontSize="32" Opacity="0.3" Glyph="&#xE896;" />
            <TextBlock FontSize="13" Opacity="0.4" Text="暂无下载任务" />
        </StackPanel>
    </StackPanel>
</UserControl>
```

---

## 5. Post-Processing Pipeline

```csharp
// Services/IDownloadPostProcessor.cs
public interface IDownloadPostProcessor
{
    string DisplayName { get; }
    Task ExecuteAsync(string downloadedFile, string destinationPath,
        IProgress<string>? progress, CancellationToken ct);
}

// Example: ESD → ISO conversion after download
var postProcessor = new DelegatePostProcessor("ESD 转 ISO", async (file, dest, progress, ct) =>
{
    progress?.Report("正在等待下载完成...");
    var esdFile = Path.Combine(dest, Path.GetFileName(file));
    var isoPath = Path.Combine(dest, Path.ChangeExtension(Path.GetFileName(file), ".iso"));
    await WindowsImageService.ConvertEsdToIsoAsync(esdFile, isoPath, progress, ct);
    if (File.Exists(esdFile) && File.Exists(isoPath))
    {
        try { File.Delete(esdFile); } catch { }
    }
});

DownloadQueueService.Enqueue(
    displayName + " (ESD→ISO)",
    downloadUrl,
    destDir,
    postProcessor,
    description: "...",
    glyph: "\uE898");
```

---

## 6. DownloadItem States

| State | Meaning | UI |
|-------|---------|----|
| `Queued` | Waiting for semaphore slot | Indeterminate progress, "排队中" |
| `Resolving` | Resolving deferred URL | Indeterminate progress, "解析中" |
| `Downloading` | Active HTTP download | Determinate progress bar, speed/time |
| `Paused` | User paused / cancel during download | Paused progress, resume button |
| `Processing` | Post-processor running | Indeterminate progress, processor status |
| `Completed` | Done | "已完成", open folder button |
| `Failed` | Error occurred | Red progress bar, retry button |
| `Cancelled` | User cancelled | "已取消", retry button |

---

## Key Notes

- **TeachingTip must be in the same visual tree** as the target element. For secondary windows,
  place the TeachingTip in that window's XAML root.
- **Always pass the clicked button as `FrameworkElement? target`** to `EnqueueDownload()` so the
  TeachingTip anchors next to the button the user just clicked.
- **TeachingTip subtitle should guide users** to where they can monitor progress (e.g., the title
  bar download button), since the download queue Flyout is not always visible.
- **InfoBadge on the title bar button** updates via `QueueChanged` event to show pending count.
- **Downloads persist** across app restarts via JSON serialization in `ConfigManager.GetDownloadQueuePath()`.
- **Partial files use `.tubadl` suffix** and support HTTP range resume when the server allows it.
- **PostProcessors** are registered in `PostProcessorRegistry` and looked up by key during queue
  restoration.
