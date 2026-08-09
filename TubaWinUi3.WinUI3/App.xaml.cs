using System.Diagnostics;
using System.Security.Principal;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
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

        LiveCharts.Configure(config => config
            .AddSkiaSharp()
            .AddDefaultMappers()
            .AddDefaultTheme());
        
        AppSettings.Load();
        
        BuiltinToolRegistry.RegisterDefaults();

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
        // EnergyStar silent auto-start (scheduled-task launched this instance
        // in the background — silently enable EcoQoS without showing the main UI).
        var silentEnergyStar = Environment.GetCommandLineArgs()
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

        _ = RunStartupSequenceAsync();
    }

    private static async Task RunStartupSequenceAsync()
    {
        if (MainWindow?.DispatcherQueue is not null)
        {
            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                ThemeService.ApplySavedTheme();
                FontService.ApplySavedFonts();
            });
        }

        _ = Task.Run(() => ToolIconService.CleanExpiredCache());
        _ = Task.Run(() => HardwareInfoService.PreloadAsync());
        _ = Task.Run(() => ConfigManager.AutoMigratePathsIfNeeded());

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
            _ = CheckForToolUpdatesSilentAsync();
            _ = CheckForUpdateSilentAsync();
        }
        else
        {
            _ = CheckForToolUpdatesSilentAsync();
        }
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
