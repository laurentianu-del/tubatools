# TubaWinUi3 — Agent Notes

## What this is

A WinUI 3 (Windows App SDK 1.8 / .NET 10) desktop app — a Chinese-language PC hardware toolbox ("图吧工具箱") that catalogs and launches third-party diagnostic/stress-test executables from a local `Tools/` folder, displays WMI/LibreHardwareMonitor hardware info, provides 19 built-in utility tools, and offers real-time hardware monitoring with FPS overlay.

## Build & run

```bash
dotnet build                          # Debug, x64 (RuntimeIdentifier auto-detects arch)
dotnet run                            # Unpackaged profile (the only profile in launchSettings.json)
```

- **No `.sln` file** — all commands go through `TubaWinUi3.csproj`
- `EnableMsixTooling=false` + `WindowsPackageType=None` — app runs unpackaged; no MSIX registration needed for dev
- Platforms: x86, x64, ARM64 — RuntimeIdentifier defaults to current process architecture
- `AllowUnsafeBlocks=true` — unsafe code is used (e.g. P/Invoke structs in HardwareInfoService)
- No test framework configured — verify with `dotnet build` only
- **Requires admin elevation** — `App.OnLaunched()` auto-elevates via `runas` if not running as admin (unpackaged mode)

## Architecture Overview

```
App.xaml.cs
  ├─ Auto-elevate to admin (unpackaged)
  ├─ BuiltinToolRegistry.RegisterDefaults() → 19 tools
  ├─ Unhandled exception → ErrorWindow
  └─ Startup: SetupWizard → ToolsBundle (MSIX) → Update check (unpackaged)
       │
       ▼
MainWindow.xaml.cs
  ├─ TitleBar (custom, ExtendsContentIntoTitleBar=true)
  ├─ NavigationView (left nav) with Frame
  ├─ Nav categories from ToolCatalog.GetCategories()
  ├─ Global AutoSuggestBox search (UnifiedSearchService)
  ├─ BackdropService (Mica/MicaAlt/Acrylic)
  └─ WindowSizeService (persist window pos/size)
       │
       ▼ navigates to Pages
  ┌────────────────────────────────────────────┐
  │ HomePage        Tool grid, search, launch   │
  │ FavoritesPage   Persisted favorites          │
  │ HardwarePage    WMI+CPU-Z info, screenshots  │
  │ BuiltinToolsPage  19 built-in tools grid     │
  │ LiteMonitorPage Real-time monitor+FPS overlay│
  │ SettingsPage    Theme/backdrop/update/config │
  │ PcSetupPage     New PC setup (winget bulk)   │
  └────────────────────────────────────────────┘
  Dialogs: ToolDetailDialog, ToolDownloadDialog,
    ToolsBundleDownloadDialog, UpdateDialog,
    SetupWizardDialog, ConfigManagerDialog,
    CustomToolManagerWindow, ErrorWindow,
    HardwareSpooferWindow, HostsEditorWindow,
    PortViewerWindow, WingetInstallerPage,
    HardwareDetailPage, FpsDetailPage
```

## Service Layer — Detailed Module Map

All services are **static classes** with no DI — called directly from pages.

### Core Tool Management

| Service | Responsibility |
|---------|---------------|
| `ToolCatalog` | Scans `Tools/` for launchable files (.exe .bat .cmd .lnk .msc .ps1 .vbs), merges arch variants (x64/x86/ARM64 dirs), caches tags/search, resolves `Tools/` root via `FindToolsRoot()` (walks up from `AppContext.BaseDirectory`) |
| `ToolMetadataService` | Merges `Metadata/tools.json` + `FileVersionInfo` + `readme.txt`; `"match"` = case-insensitive substring; supports `archVariants` with `file`/`dir` + `arch`; can write/remove entries |
| `ToolIconService` | Extracts .exe/.lnk icons to `%LocalAppData%/TubaWinUi3/IconCache/` (SHA256 cache key); `GetIconGlyph()` fallback |
| `ToolDownloaderService` | Downloads tools from GitHub/Hub releases; handles archives/installers; progress tracking |
| `ToolsBundleService` | Full Tools.zip bundle for MSIX packaged mode; downloads from Hub/GitHub/GitCode mirrors |
| `FavoritesService` | Persisted favorite tool paths |
| `LaunchHistoryService` | Tracks recently launched tools for search suggestions |
| `CustomToolPackageService` | Imports custom tools from ZIP; scans for executables, creates tool directory, updates tools.json |
| `CompactModeService` | Toggles card grid vs compact list view |
| `FastModeService` | Disables all animations |

### Hardware Information

| Service | Responsibility |
|---------|---------------|
| `HardwareInfoService` | WMI queries (Win32_ComputerSystem, Win32_Processor, Win32_VideoController, etc.) + P/Invoke (EnumDisplayDevices/EnumDisplaySettings); runs on `Task.Run`; 3 sections: 型号信息/系统信息/详细信息; CPU-Z override via `ApplyCpuzOverride()`; detail data via `LoadDetailAsync()` |
| `CpuzInfoService` | Parses CPU-Z `.txt` report files to supplement/verify WMI data |
| `LiteMonitorService` | Singleton wrapping LibreHardwareMonitor `Computer`; CPU/GPU/mem/net/storage/battery sensors; requires kernel driver |
| `FpsService` | ETW trace session (DxgKrnl provider) tracking DXGI Present events for FPS; auto-discovers foreground app, excludes system processes |
| `CpuBurnService` | CPU stress test: `Environment.ProcessorCount` workers doing math loops; reports temp/freq via LHM |
| `CpuRankingService` | CPU benchmark ranking from remote JSON; desktop/laptop; 1-hour cooldown |
| `GpuRankingService` | GPU benchmark ranking from remote JSON; desktop/laptop; 1-hour cooldown |
| `HardwareSpooferService` | Modifies registry to spoof hardware IDs; backup/restore via JSON; requires admin |
| `BatteryAnalyzerService` | Battery report via `powercfg /batteryreport` |

### Built-in Tools (19 in BuiltinToolRegistry)

All implement `IBuiltinTool` (Id, Name, Description, Glyph, Category, Kind, ExecuteAsync).
Kinds: `Dialog`, `BackgroundTask`, `ProgressTask`, `InstantAction`.

| Tool | Kind | What it does |
|------|------|-------------|
| CertBlockTool | Dialog | Block/unblock certificate trust |
| PortViewerTool | Dialog | View active TCP/UDP ports |
| HostsEditorTool | Dialog | Edit system hosts file |
| KeyboardTestTool | Dialog | Interactive keyboard test |
| JunkCleanerTool | ProgressTask | Clean temp/cache/junk files |
| BsodAnalysisTool | ProgressTask | Analyze BSOD minidumps |
| WingetInstallerTool | Dialog | Install software via winget |
| BatteryAnalyzerTool | ProgressTask | Battery health report |
| SpeedTestTool | ProgressTask | Network speed test |
| WifiPasswordTool | BackgroundTask | Retrieve saved WiFi passwords |
| DiskSpaceAnalyzerTool | ProgressTask | Disk space usage analysis |
| LiteMonitorTool | Dialog | Open real-time hardware monitor |
| WindowsActivationTool | Dialog | KMS activation with remote server list |
| DefenderTool | InstantAction | Open Windows Defender settings |
| CpuRankingTool | Dialog | CPU benchmark rankings |
| GpuRankingTool | Dialog | GPU benchmark rankings |
| ContextMenuMgrTool | Dialog | Manage context menu entries |
| HardwareSpooferTool | Dialog | Spoof hardware IDs in registry |
| PcSetupTool | Dialog | New PC setup wizard (winget bulk) |

### System & UI Services

| Service | Responsibility |
|---------|---------------|
| `AppSettings` | JSON settings (`Dictionary<string,string>`) at configurable path; fires `SettingChanged` |
| `ConfigManager` | Data dir (AppData vs AppRoot); settings path, export/import config ZIP, popup settings |
| `ThemeService` | Light/Dark/System; `CurrentElementTheme` used by all ContentDialogs |
| `BackdropService` | Mica/MicaAlt/Acrylic system backdrop; fires `BackdropChanged` |
| `BackgroundService` | Custom background image for HomePage; path + opacity in AppSettings |
| `WindowSizeService` | Persist and restore window position/size |
| `UnifiedSearchService` | Global search: external tools, built-in tools, settings, custom tools, quick actions |
| `SearchHighlightService` | Animates a Border to highlight search results |
| `MarkdownTextService` | Converts markdown to RichTextBlock inlines |
| `BulkObservableCollection<T>` | Observable collection with bulk add |
| `UpdateService` | GitHub release version check; skip-version support |
| `RuntimeHelper` | Detects MSIX packaged mode |
| `SystemOptimizer` | Registry-based Windows visual/performance presets |
| `KmsActivationService` | KMS server list from remote JSON; scored by success rate/latency |
| `WingetService` | Wraps `winget` CLI for search/install/upgrade |
| `PcSetupCatalogService` | New-PC setup catalog JSON (categories → packages → winget IDs) |
| `JunkCleanerService` | Scans/cleans temp, browser cache, Windows update cache |
| `BsodAnalysisService` | Wraps BlueScreenView.exe or parses minidumps |
| `BatteryReportService` | HTML battery report via `powercfg` |
| `SpeedTestService` | Downloads test files, measures throughput |
| `WifiPasswordService` | Extracts saved WiFi passwords via `netsh` |
| `HostsEditorService` | Reads/writes system hosts file; requires admin |
| `PortViewerService` | Lists TCP/UDP connections via `netstat` |
| `CertBlockService` | Certificate blocking data from `CertBlock/` |
| `ThemeColors` | Color palette definitions for theming |

## Models

| Model | Purpose |
|-------|---------|
| `ToolItem` | External tool: Name, Category, Path, Extension, Description, Publisher, Tags, Favorites, Arch variants, Winget state; `INotifyPropertyChanged` |
| `ArchVariant` / `ArchOption` | Architecture variant (x64/x86/ARM64) |
| `HardwareInfoSection` / `HardwareInfoItem` | WMI hardware info display sections |
| `HardwareDetailData` | Detailed: CpuDetail, MotherboardDetail, MemoryDetail+Modules, GpuDetail, DiskDetail, DisplayDetail, SoundDetail, NetworkDetail |
| `MonitorSample` | Real-time sensor reading for LiteMonitor |
| `SearchResult` / `SearchItemKind` | Unified search (ExternalTool/BuiltinTool/Setting/CustomTool/QuickAction) |
| `UpdateInfo` | App update version info |
| `WingetPackage` | Winget package model |
| `CpuRankingEntry` / `GpuRankingEntry` | Benchmark ranking entries |
| `CertBlockInfo` | Certificate blocking data |
| `NewPcSetupModels` | New PC setup catalog categories/packages |

## Data Flow

```
App Launch
  ├─ Unpackaged mode: auto-elevate → admin restart
  ├─ BuiltinToolRegistry.RegisterDefaults() → 19 tools registered
  ├─ ThemeService.ApplySavedTheme()
  ├─ ToolIconService.CleanExpiredCache()
  ├─ HardwareInfoService.Preload() → background Task.Run
  └─ Startup sequence:
       First run? → SetupWizardDialog
       MSIX? → ToolsBundleService download check
       Unpackaged? → UpdateService check

Navigation
  MainWindow.NavView → Frame.Navigate(typeof(XxxPage), parameter)
  Categories from ToolCatalog.GetCategories() → dynamic NavigationViewItems
  Search → UnifiedSearchService.Search(query) → HandleSearchResult → navigate

Tool Launch
  HomePage → ToolItem.EffectivePath → Process.Start (with admin elevate option)
  Arch selection → ToolItem.SelectedArch → re-resolves EffectivePath
  Download-needed tools → ToolDownloadDialog → ToolDownloaderService

Hardware Info
  HardwarePage → HardwareInfoService.LoadAsync() → WMI + P/Invoke
               → CpuzInfoService (optional) → ApplyCpuzOverride()
               → HardwareDetailPage for per-component detail

Hardware Monitor
  LiteMonitorPage → LiteMonitorService.Instance → LibreHardwareMonitor sensors
  FpsService → ETW DxgKrnl Present events → FPS per process
  Popup overlay window → topmost, transparent, compact sensor display
```

## Adding a built-in tool

1. Create a new class in `Services/BuiltinTools/` implementing `IBuiltinTool`
2. Choose `BuiltinToolKind`: `Dialog` (popup UI), `BackgroundTask` (run silently), `ProgressTask` (progress bar), `InstantAction` (immediate)
3. Register in `BuiltinToolRegistry.RegisterDefaults()` — duplicate IDs throw
4. Use `context.CreateDialog(title)` to create themed ContentDialogs

## Key conventions

- **Namespace**: `TubaWinUi3` / `TubaWinUi3.Pages` / `TubaWinUi3.Services` / `TubaWinUi3.Models`
- **Services are static classes** with no DI — called directly from pages
- **UI strings are in Chinese** (hardcoded in XAML/C#); no resource localization system
- **`Tools/` content is bundled** via `<Content Include="Tools\**\*">` with `CopyToOutputDirectory=PreserveNewest`; excluded from publish when `ExcludeToolsFromPublish=true`
- **`Metadata/tools.json`** `"match"` field: case-insensitive substring match against tool filenames/paths; supports `"downloadUrl"`, `"wingetId"`, `"remoteUrl"`, `"launchTarget"`, `"archVariants"` entries
- **File naming**: PascalCase for C#; XAML + code-behind pairs
- **Commit format**: `feat:` / `fix:` / `docs:` / `refactor:` (from README)
- **Never commit**: `bin/`, `obj/`, `.pfx`, `.cer`

## Gotchas

- `Tools/` folder has Chinese directory names (处理器工具, 显卡工具, etc.); path handling must be Unicode-safe
- `ToolCatalog.FindToolsRoot()` walks up from `AppContext.BaseDirectory` to find `Tools/` — works both packaged and unpackaged; MSIX mode uses `%LocalAppData%/TubaWinUi3/Tools/`
- `HardwareInfoService` runs WMI on `Task.Run` (background thread); results consumed on UI thread
- `LiteMonitorService` deploys WinRing0 driver (`WinRing0x64.sys`) — requires admin elevation; the `EnsureDriverAsync` flow handles consent UI
- `FpsService` uses ETW trace session with `Microsoft.Diagnostics.Tracing.TraceEvent` — requires admin for kernel-level tracing
- `Package.appxmanifest` declares `runFullTrust` and `systemAIModels` capabilities
- Publish config: `PublishTrimmed=false`, `PublishReadyToRun=false` in both Debug and Release — trimming is not used
- `.pri` file must be manually restored from `bin/` to publish output after `dotnet publish` (both CI and build scripts do this)
- `package.json` / `node_modules/` / `src/docs/` are for the VitePress docs site only — not part of the .NET app
- `build-setup.ps1` builds Inno Setup installer (x64 + ARM64); `build-store.ps1` builds MSIX for Store submission
- `Launcher/` is a native C launcher (launcher.c + launcher.rc) that finds and launches the .NET app; built separately via `Launcher/build.ps1`
- CI workflow (`.github/workflows/build-release.yml`) is manual dispatch only (`workflow_dispatch`), publishes x64/x86/ARM64 portable zip + Inno installer, generates changelog via DeepSeek API
- `ConfigManager` supports two data locations: AppData (`%LocalAppData%/TubaWinUi3/`) or AppRoot (`<appdir>/Data/`), controlled by `.config_location` marker file
- `LiteMonitorService` is a singleton (`Instance`); not a static class — the only non-static service
- `ToolItem.InitArchOptions()` auto-selects the best architecture variant based on the current OS (ARM64 > x64 > x86 preference)
- `HardwareInfoService.ApplyCpuzOverride()` deep-copies WMI sections and overwrites with CPU-Z data; `IsVerified = true` marks overridden fields
- All ContentDialogs must use `context.CreateDialog()` or manually set `RequestedTheme = ThemeService.CurrentElementTheme` to respect the app theme

## Website (docs)

```bash
npm run dev       # VitePress dev server at src/docs
npm run build     # Build static site
```

Separate from the .NET build — `node_modules/` and `src/` are not referenced by the `.csproj`.
