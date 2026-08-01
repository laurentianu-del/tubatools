using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Pages;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class PcSetupTool : IBuiltinTool
{
    public string Id => "pc-setup";
    public string Name => "新机开荒";
    public string Description => "引导式新机配置：安装软件、优化系统、烤机测试一步到位。";
    public string Glyph => "\uE977";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var available = await WingetService.IsWingetAvailableAsync();
        if (!available)
        {
            var errDialog = context.CreateDialog("winget 不可用");
            errDialog.Content = "未检测到 winget，部分功能（软件安装）将不可用。你可以继续使用系统优化和烤机测试功能。";
            errDialog.CloseButtonText = "继续";
            await errDialog.ShowAsync();
        }

        var window = new Window();
        var page = new PcSetupPage(window);
        page.RequestedTheme = ThemeService.CurrentElementTheme;

        window.Content = page;
        BackdropService.ApplyBackdrop(window);
        window.AppWindow.Title = "新机开荒";

        try
        {
            var displayArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea is not null)
            {
                var workArea = displayArea.WorkArea;
                var w = (int)(workArea.Width * 0.82);
                var h = (int)(workArea.Height * 0.85);
                window.AppWindow.Resize(new SizeInt32(w, h));
                window.AppWindow.Move(new PointInt32(
                    workArea.X + (int)((workArea.Width - w) / 2),
                    workArea.Y + (int)((workArea.Height - h) / 2)));
            }
        }
        catch
        {
            window.AppWindow.Resize(new SizeInt32(1100, 750));
            try
            {
                var mainPos = App.MainWindow?.AppWindow.Position;
                if (mainPos is not null)
                    window.AppWindow.Move(new PointInt32(mainPos.Value.X + 50, mainPos.Value.Y + 50));
            }
            catch { }
        }

        window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        ApplyTitleBarTheme(window);
        window.Activate();
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
    }
}
