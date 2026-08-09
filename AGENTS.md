# TubaWinUi3 — Agent Notes

## What this is

A WinUI 3 (Windows App SDK / .NET 10) Chinese-language PC hardware toolbox ("图吧工具箱"). Catalogs and launches third-party diagnostic executables from a local `Tools/` folder, shows WMI/LibreHardwareMonitor hardware info, ships ~30 built-in utility tools, and does real-time hardware monitoring with an FPS overlay. UI strings are hardcoded Chinese — there is no resource/localization system.

## Solution layout (3 projects in `TubaWinUi3.sln`)

- `TubaWinUi3.WinUI3/` — **the app**. The only project you normally build/run; `dotnet` commands target `TubaWinUi3.WinUI3/TubaWinUi3.csproj`.
- `TubaWinUi3.Compatible/` — a separate **.NET Framework 4.5 WinForms** edition (`图吧工具箱Winui3兼容版.exe`, MetroModernUI + Costura.Fody single-file). NOT WinUI 3 and NOT .NET 10 — different toolchain, different conventions. Built by CI and bundled into portable zips. Do not mix its patterns into the main app.
- `TubaWinUi3.Tests/` — **xUnit** tests (xUnit 2.9 + coverlet), referencing the main project via `InternalsVisibleTo`.

## Build, run, test

```bash
dotnet build                                                      # Debug; RuntimeIdentifier auto-detects current arch
dotnet run                                                        # Unpackaged profile (only profile in launchSettings.json)
dotnet test                                                       # all tests
dotnet test --filter "FullyQualifiedName~ToolCatalogTests"        # one class / one test
```

- Platforms x86 / x64 / ARM64; `RuntimeIdentifier` defaults to the current process architecture.
- `WindowsPackageType=None` + `EnableMsixTooling=false` → runs unpackaged; no MSIX registration for dev.
- **Requires admin**: `App.OnLaunched` auto-elevates via the `runas` verb and `Exit()`s if not admin (unpackaged mode only). A headless `EnergyStar` mode can also launch via `EnergyStarStartupService.SilentArg` to apply EcoQoS throttling with no window.
- `AllowUnsafeBlocks=true` (P/Invoke structs in `HardwareInfoService`).
- Publish is self-contained; `PublishTrimmed=false`, `PublishReadyToRun=false` — trimming is never used.
- **`.pri` gotcha**: after `dotnet publish`, copy `TubaWinUi3.pri` from `bin/<arch>/Release/.../<rid>/` into the publish output (CI does this; the app misbehaves without it).

## Stray root files — do not edit

`MainWindow.xaml`, `MainWindow.xaml.cs`, and `Pages/SettingsPage.xaml(.cs)` exist at the **repo root** but are NOT compiled by the main project. The live source is under `TubaWinUi3.WinUI3/`. Always edit there.

## Architecture essentials

- `App.xaml.cs` → `MainWindow` (custom TitleBar + `NavigationView` + `Frame`); nav categories come from `ToolCatalog.GetCategories()` and `BuiltinToolRegistry.GetCategories()`.
- **All services are static classes with no DI**, called directly from pages. The single exception is `LiteMonitorService`, a singleton (`Instance`).
- `ToolCatalog` scans `Tools/` for `.exe .bat .cmd .lnk .msc .ps1 .vbs`, merges x64/x86/ARM64 variants, and resolves the `Tools/` root by walking up from `AppContext.BaseDirectory` (`FindToolsRoot()`).
- `ToolMetadataService` merges `Metadata/tools.json` + `FileVersionInfo` + `readme.txt`. The `"match"` field is a **case-insensitive substring** against tool filenames/paths.
- `ToolItem.InitArchOptions()` auto-selects the best arch for the OS (ARM64 > x64 > x86 preference).
- Built-in tools: see `BuiltinToolRegistry.RegisterDefaults()` (~31 tools). `CommunityToolBuiltinTool` registers only when `!RuntimeHelper.IsMsixPackaged`.

### Adding a built-in tool
1. New class in `TubaWinUi3.WinUI3/Services/BuiltinTools/` implementing `IBuiltinTool`.
2. Pick `BuiltinToolKind`: `Dialog` / `BackgroundTask` / `ProgressTask` / `InstantAction`.
3. Register in `BuiltinToolRegistry.RegisterDefaults()` — **duplicate IDs throw**.
4. Create dialogs via `context.CreateDialog(title)` (or manually set `RequestedTheme = ThemeService.CurrentElementTheme`) so ContentDialogs respect the app theme.

## Gotchas

- `Tools/` has Chinese category directory names (处理器工具, 显卡工具, …) — path handling must be Unicode-safe.
- `HardwareInfoService` runs WMI on `Task.Run`; results are consumed on the UI thread. `ApplyCpuzOverride()` deep-copies WMI sections and overwrites them with CPU-Z data (`IsVerified=true`).
- `LiteMonitorService` deploys the WinRing0 kernel driver — needs admin; `EnsureDriverAsync` handles consent UI.
- `FpsService` uses an ETW `DxgKrnl` trace session (`Microsoft.Diagnostics.Tracing.TraceEvent`) — needs admin for kernel tracing.
- `ConfigManager` supports two data locations — AppData (`%LocalAppData%/TubaWinUi3/`) or AppRoot (`<appdir>/Data/`) — selected by a `.config_location` marker file.
- `Package.appxmanifest` declares `runFullTrust` and `systemAIModels`.
- **Bundled icon cache**: `ToolIconService` prefers `<appdir>/IconCache/` (ships inside the package, read-only) over the writable `DataDir/IconCache`; missing/stale icons are copied from the bundled cache or extracted at runtime. `build-icon-cache.ps1` generates it (same SHA256 `{ToolsRoot}\<relative>` key scheme); a `GenerateBundledIconCache` MSBuild target runs it automatically before every `dotnet publish` (skipped when `ExcludeToolsFromPublish=true`). The `IconCache/` folder is gitignored — never commit it.

## File-transfer subsystem (separate from the .NET app)

The "文件传输" feature spans three pieces with their own toolchains — none are part of `dotnet build`:
- `TubaWinUi3.WinUI3/Services/BuiltinTools/LanFileShareTool.cs` + the `SIPSorcery` package (WebRTC) — the in-app side.
- `file-transfer-web/` — Vue 3 + Vuetify 4 web UI (`npm run dev`; build is `vue-tsc -b && vite build`).
- `cloudflare-worker/` — WebRTC signaling server (Cloudflare Durable Object `GroupRoom`); `wrangler dev` / `wrangler deploy`.

## CI (`.github/workflows/` — all manual `workflow_dispatch`)

- `build-release.yml` — bumps `<Version>` in **both** `.csproj`s and `#define MyAppVersion` in all `installer*.iss`, publishes x64/x86/ARM64 portable + Inno installer + x64-lite (`ExcludeToolsFromPublish=true` + `.lite_build` marker), builds the Compatible edition, restores `.pri`, generates the changelog via **DeepSeek** (`DEEPSEEK_API_KEY`), creates the GitHub release, and optionally mirrors to **GitCode/AtomGit** (`GITCODE_ACCESS_TOKEN`). Portable zips are staged as a `src/` folder plus the native `Launcher\bin\图吧工具箱WinUI3_<arch>.exe` (renamed `图吧工具箱WinUI3.exe`).
- `publish-winget.yml` — submits a released version to winget (`WINGET_GITHUB_TOKEN`; package id `luolangaga.tubatools`).
- `auto-approve-benchmark.yml` — rebuilds the benchmark leaderboard files.

`Launcher/` is a native C launcher (`launcher.c` + `launcher.rc`, built via `Launcher/build.ps1`) that finds and starts the .NET app. `build-setup.ps1` / `build-store*.ps1` build the Inno installer / MSIX locally.

## Docs site (separate)

Root `package.json` + `src/docs/` are a **VitePress** site only (`npm run dev` / `npm run build`). `node_modules/` is not referenced by any `.csproj`.

## New official website (separate)

`website-winui3/` is the new official website, built on **WinUIonWeb** (Vue 3 + Web WinUI controls, `npm run dev` / `npm run build`). Docs markdown lives in `website-winui3/src/docs/` (migrated from `src/docs/`), tutorial images in `website-winui3/public/tutorials/images/`. Icon glyphs must exist in `src/assets/Fonts/SEGOEICONS.TTF` (check with `node check-font` against the cmap table).

## Conventions

- Namespaces: `TubaWinUi3` / `.Pages` / `.Services` / `.Models`. PascalCase; XAML + code-behind pairs.
- Commit format: `feat:` / `fix:` / `docs:` / `refactor:`.
- Never commit: `bin/`, `obj/`, `.pfx`, `.cer`.
