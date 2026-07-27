using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Pages;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class QuickDeviceCheckTool : IBuiltinTool
{
    public string Id => "quick-device-check";
    public string Name => "快速验机";
    public string Description => "新电脑验机向导：外观检查、硬件信息、硬盘通电、屏幕坏点、外设测试、摄像头、音频、双烤测试，一站式完成。";
    public string Glyph => "\uE962";
    public string Category => "硬件信息";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        var window = new Window();
        var page = new QuickDeviceCheckPage(window);
        page.RequestedTheme = ThemeService.CurrentElementTheme;

        window.Content = page;
        BackdropService.ApplyBackdrop(window);
        window.AppWindow.Title = "快速验机";

        try
        {
            window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        catch
        {
            try
            {
                var displayArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary);
                if (displayArea is not null)
                {
                    var workArea = displayArea.WorkArea;
                    window.AppWindow.Resize(new SizeInt32(workArea.Width, workArea.Height));
                    window.AppWindow.Move(new PointInt32(workArea.X, workArea.Y));
                }
            }
            catch { }
        }

        window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ApplyTitleBarTheme(window);

        window.Closed += (_, _) => { page.Cleanup(); };

        window.Activate();
        return Task.CompletedTask;
    }

    private static void ApplyTitleBarTheme(Window window)
    {
        var tb = window.AppWindow.TitleBar;
        var isDark = ThemeService.CurrentTheme == AppTheme.Dark ||
                     (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        if (isDark)
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
            tb.InactiveBackgroundColor = Color.FromArgb(255, 32, 32, 32);
        }
        else
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
            tb.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.InactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
        }
        tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
        tb.ButtonInactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
    }
}
