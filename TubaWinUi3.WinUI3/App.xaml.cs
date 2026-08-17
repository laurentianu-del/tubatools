using System.Diagnostics;
using System.Security.Principal;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using TubaWinUi3.Services.ActiveIntercept;
using TubaWinUi3.Services.Agent;
using TubaWinUi3.Models;
namespace TubaWinUi3;

public partial class App : Application
{
    private MainWindow? _window;
    public static MainWindow? MainWindow => ((App)Current)?._window;
    public static bool IsLiteMode { get; set; } = false;

    public App()
    {
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        InitializeComponent();

        // LiveCharts/SkiaSharp 不再于启动时初始化：首个图表页面首次访问时才配置（ChartInitializer）。
        // LiveCharts.Configure 在 App() 中已移除，启动不再加载 SkiaSharp 原生库。

        AppSettings.Load();
        
        BuiltinToolRegistry.RegisterDefaults();
        AgentToolRegistry.RegisterDefaults();
        AgentSkillRegistry.RegisterDefaults();

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        UnhandledException += OnWinUIUnhandledException;
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void ElevateAndRestart()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                Verb = "runas",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 流氓软件的克星「安全增强菜单 - 复制完整路径」配方通过 --copy-path <路径>
        // 唤醒本程序复制路径到剪贴板（后台模式，不显示主窗口）。
        var cmdLine = Environment.GetCommandLineArgs();
        var copyPathIndex = Array.FindIndex(cmdLine, a => string.Equals(a, "--copy-path", StringComparison.OrdinalIgnoreCase));
        if (copyPathIndex >= 0)
        {
            if (copyPathIndex + 1 < cmdLine.Length && !string.IsNullOrWhiteSpace(cmdLine[copyPathIndex + 1]))
            {
                try
                {
                    var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    data.SetText(cmdLine[copyPathIndex + 1]);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                    Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                }
                catch
                {
                }
            }
            Exit();
            return;
        }

        // 后端 --toast 模式：读取通知文件，弹出 Windows 原生 Toast 后立即退出（不显示主窗口）。
        // 双通道防重复：主程序已运行时 FileSystemWatcher 先消费文件，此处读不到即跳过。
        var toastIndex = Array.FindIndex(cmdLine, a => string.Equals(a, "--toast", StringComparison.OrdinalIgnoreCase));
        if (toastIndex >= 0 && toastIndex + 1 < cmdLine.Length)
        {
            var notifFile = cmdLine[toastIndex + 1];
            try
            {
                // 延迟等待 FileSystemWatcher 先处理（主程序已运行时）
                Thread.Sleep(500);
                if (File.Exists(notifFile))
                {
                    var json = File.ReadAllText(notifFile);
                    var req = System.Text.Json.JsonSerializer.Deserialize(
                        json, Services.ActiveIntercept.ActiveInterceptJsonContext.Default.NotificationRequest);
                    if (req is not null && !string.IsNullOrWhiteSpace(req.Title))
                    {
                        new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                            .AddText(req.Title)
                            .AddText(req.Body)
                            .AddArgument("action", "show-active-intercept")
                            .Show(toast =>
                            {
                                toast.ExpirationTime = DateTimeOffset.Now.AddMinutes(10);
                            });
                    }
                    try { File.Delete(notifFile); } catch { }
                }
                // 文件已被 FileSystemWatcher 消费 → 无需重复弹通知
            }
            catch { }
            Exit();
            return;
        }

        // 构建工具缓存模式（publish 时由 GenerateBundledToolCache MSBuild target 调用）：
        // 扫描 Tools 目录并把结果写入 Metadata/tool_cache.json 随包分发，
        // 运行时直接读取免去全量扫描。复用全部扫描逻辑，无窗口后台运行。
        var buildCacheIndex = Array.FindIndex(cmdLine, a => string.Equals(a, "--build-tool-cache", StringComparison.OrdinalIgnoreCase));
        if (buildCacheIndex >= 0)
        {
            var toolsRoot = buildCacheIndex + 1 < cmdLine.Length ? cmdLine[buildCacheIndex + 1] : "";
            var outJson = buildCacheIndex + 2 < cmdLine.Length ? cmdLine[buildCacheIndex + 2] : "";
            if (!string.IsNullOrWhiteSpace(toolsRoot) && !string.IsNullOrWhiteSpace(outJson))
            {
                try
                {
                    ToolCatalog.SetToolsRootForBuild(toolsRoot);
                    ToolCatalog.GetAllToolsCached(); // 预热扫描（构建环境）
                    ToolCacheService.SaveCacheTo(ToolCatalog.BuildCacheEntries(), outJson);
                }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tool_cache_build.log"),
                        ex.ToString());
                }
            }
            Exit();
            return;
        }

        // EnergyStar silent auto-start (scheduled-task launched this instance
        // in the background — silently enable EcoQoS without showing the main UI).
        var silentEnergyStar = cmdLine
            .Any(a => string.Equals(a, EnergyStarStartupService.SilentArg, StringComparison.OrdinalIgnoreCase));

        if (silentEnergyStar)
        {
            try { EnergyStarService.Initialize(); } catch { /* swallow so OS keeps the task happy */ }
            // No main window: keep this process throttling in the background.
            // Active throttling is driven by the static service; the process can
            // stay alive without a WinUI window (the dispatcher here is unused).
            return;
        }

        if (!RuntimeHelper.IsMsixPackaged && !IsRunningAsAdmin())
        {
            ElevateAndRestart();
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
        ToolItem.SetUIDispatcher(_window.DispatcherQueue);
        BrowserAutomationService.Initialize(_window.DispatcherQueue);

        // 主动拦截 Toast 通知被点击时，后端以 --show-active-intercept 启动主程序，
        // 直接跳转「流氓软件的克星 → 主动拦截」审核页。
        var showActiveIntercept = cmdLine
            .Any(a => string.Equals(a, "--show-active-intercept", StringComparison.OrdinalIgnoreCase));
        if (showActiveIntercept)
        {
            _window.NavigateToToolPage(typeof(Pages.RogueCleanerPage), "activeintercept");
        }

        _ = RunStartupSequenceAsync();
    }

    private static async Task RunStartupSequenceAsync()
    {
        // MSIX 下包身份解析失败会回滚到共享 %LocalAppData%（非打包路径）：
        // 工具根/数据目录将指向旧安装版位置，可能启动非打包路径的程序，输出诊断日志
        if (RuntimeHelper.IsMsixPackaged && RuntimeHelper.LocalAppDataRootUsedFallback)
        {
            System.Diagnostics.Debug.WriteLine("[Startup] 警告：MSIX 包身份路径解析失败，数据根已回滚到共享 %LocalAppData%，工具根可能指向非打包路径");
        }

        if (MainWindow?.DispatcherQueue is not null)
        {
            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                ThemeService.ApplySavedTheme();
            });
        }

        // 图标缓存清理与硬件盘点都不再抢启动窗口：图标清理延迟到空闲期执行，
        // 硬件 WMI 盘点（20+ 条查询）移到首次打开硬件信息页时预热。
        _ = DelayThenRunAsync(TimeSpan.FromSeconds(15), () => { ToolIconService.CleanExpiredCache(); return Task.CompletedTask; });
        _ = Task.Run(() => ConfigManager.AutoMigratePathsIfNeeded());

        // 主动拦截：若用户开启了主动拦截，自动拉起 NativeAOT 后端（独立常驻进程）。
        // MSIX 沙箱下不支持启动独立后端进程。
        if (!RuntimeHelper.IsMsixPackaged && AppSettings.GetBool("ActiveInterceptEnabled", false))
        {
            ActiveInterceptService.Start();
        }

        try
        {
            if (AppSettings.Get("SetupCompleted") == null)
            {
                if (MainWindow?.Content is FrameworkElement root)
                {
                    var wizard = new SetupWizardDialog
                    {
                        XamlRoot = root.XamlRoot,
                        RequestedTheme = ThemeService.CurrentElementTheme
                    };
                    await wizard.ShowAsync();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Setup] Wizard failed: {ex.Message}");
        }
        finally
        {
            if (AppSettings.Get("SetupCompleted") == null)
                AppSettings.Set("SetupCompleted", true);
        }

        if (RuntimeHelper.IsMsixPackaged || RuntimeHelper.IsLiteBuild)
        {
            if (!ToolsBundleService.IsToolsBundleReady())
            {
                await ShowToolsBundleDownloadDialogAsync();
            }
            _ = CheckForToolsUpdateSilentAsync();
        }

        if (!RuntimeHelper.IsMsixPackaged)
        {
            // 更新检查延后 10s 发起，避开启动窗口期的磁盘/网络竞争
            _ = DelayThenRunAsync(TimeSpan.FromSeconds(10), CheckForToolUpdatesSilentAsync);
            _ = DelayThenRunAsync(TimeSpan.FromSeconds(10), () => CheckForUpdateSilentAsync());
        }
        else
        {
            _ = DelayThenRunAsync(TimeSpan.FromSeconds(10), CheckForToolUpdatesSilentAsync);
        }
    }

    private static async Task DelayThenRunAsync(TimeSpan delay, Func<Task> action)
    {
        try
        {
            await Task.Delay(delay);
            await action();
        }
        catch { }
    }

    private static async Task ShowToolsBundleDownloadDialogAsync()
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await Task.Delay(i == 0 ? 300 : 1000);

                if (MainWindow?.Content is FrameworkElement root)
                {
                    var dialog = new ToolsBundleDownloadDialog
                    {
                        XamlRoot = root.XamlRoot,
                        RequestedTheme = ThemeService.CurrentElementTheme
                    };
                    await dialog.ShowDownloadAsync();
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolsBundle] Download dialog attempt {i + 1} failed: {ex.Message}");
            }
        }
    }

    private static async Task CheckForToolsUpdateSilentAsync()
    {
        try
        {
            if (!ToolsBundleService.IsToolsBundleReady()) return;

            var info = await ToolsBundleService.CheckForToolsUpdateAsync();
            if (info is null || !info.HasUpdate) return;

            if (MainWindow?.DispatcherQueue is null) return;

            MainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                if (MainWindow?.Content is not FrameworkElement root) return;
                var dialog = new ToolsBundleDownloadDialog
                {
                    XamlRoot = root.XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                dialog.SetDescription("发现工具包新版本，建议更新以获取最新工具。");
                await dialog.ShowDownloadAsync(info);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToolsBundle] Update check failed: {ex.Message}");
        }
    }

    private static async Task<bool> CheckForUpdateSilentAsync()
    {
        try
        {
            var update = await UpdateService.CheckForUpdateAsync();
            if (update is null) return false;

            var skipped = UpdateService.GetSkippedVersion();
            if (skipped == update.Version) return false;

            if (MainWindow?.DispatcherQueue is null) return false;

            if (UpdateService.IsUpdateAlreadyDownloaded(update))
            {
                MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    if (MainWindow is MainWindow mw)
                        mw.ShowUpdateAlreadyDownloaded(update);
                });
                return true;
            }

            var autoDownload = false;

            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (MainWindow is MainWindow mw)
                    mw.ShowUpdateBanner(update, autoDownload);
            });

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Update] Silent check failed: {ex.Message}");
            return false;
        }
    }

    private static async Task CheckForToolUpdatesSilentAsync()
    {
        try
        {
            var updates = await ToolUpdateService.CheckForToolUpdatesAsync();
            if (updates is null || updates.Count == 0) return;

            ToolUpdateService.EnqueueToolUpdates(updates);
        }
        catch { }
    }

    private static Exception? _pendingException;

    private void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        _pendingException = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "未知错误");
        NavigateToErrorPage();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _pendingException = e.Exception;
        NavigateToErrorPage();
        e.SetObserved();
    }

    private void OnWinUIUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "app_crash.log"),
            $"WinUI Unhandled Exception:\n{e.Exception}\n\nMessage: {e.Message}");
        _pendingException = e.Exception ?? new Exception(e.Message);
        NavigateToErrorPage();
        e.Handled = true;
    }

    public static Exception? ConsumePendingException()
    {
        var ex = _pendingException;
        _pendingException = null;
        return ex;
    }

    private void NavigateToErrorPage()
    {
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            var errorWindow = new Pages.ErrorWindow();
            errorWindow.Activate();
        });
    }
}
