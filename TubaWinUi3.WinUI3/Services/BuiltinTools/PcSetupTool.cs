using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;

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
        window.AppWindow.Title = "新机开荒";

        var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(window.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        if (displayArea is not null)
        {
            var screenWidth = displayArea.WorkArea.Width;
            var screenHeight = displayArea.WorkArea.Height;
            var w = (int)(screenWidth * 0.8);
            var h = (int)(screenHeight * 0.8);
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
            window.AppWindow.Move(new Windows.Graphics.PointInt32(
                (screenWidth - w) / 2 + displayArea.WorkArea.X,
                (screenHeight - h) / 2 + displayArea.WorkArea.Y));
        }
        else
        {
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 750));
        }

        ApplyTitleBarTheme(window);
        window.Activate();
    }

    private static void ApplyTitleBarTheme(Window window)
    {
        var isDark = ThemeService.CurrentElementTheme == ElementTheme.Dark ||
                     (ThemeService.CurrentElementTheme == ElementTheme.Default &&
                      Application.Current.RequestedTheme == ApplicationTheme.Dark);
        var titleBar = window.AppWindow.TitleBar;
        titleBar.BackgroundColor = isDark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        titleBar.ForegroundColor = isDark ? Windows.UI.Color.FromArgb(255, 210, 210, 210) : Windows.UI.Color.FromArgb(255, 30, 30, 30);
        titleBar.InactiveBackgroundColor = titleBar.BackgroundColor;
        titleBar.InactiveForegroundColor = isDark ? Windows.UI.Color.FromArgb(255, 100, 100, 100) : Windows.UI.Color.FromArgb(255, 160, 160, 160);
        titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonForegroundColor = titleBar.ForegroundColor;
        titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveForegroundColor = isDark ? Windows.UI.Color.FromArgb(255, 80, 80, 80) : Windows.UI.Color.FromArgb(255, 180, 180, 180);
    }
}
