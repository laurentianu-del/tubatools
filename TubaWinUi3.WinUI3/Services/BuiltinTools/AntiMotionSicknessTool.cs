using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Pages;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class AntiMotionSicknessTool : IBuiltinTool
{
    public string Id => "anti-motion-sickness";
    public string Name => "游戏防晕3D";
    public string Description => "屏幕中央准星+四边标记辅助，缓解3D眩晕。一键帮你节约13块钱！";
    public string Glyph => "\uE7FC";
    public string Category => "游戏工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        try
        {
            var window = new Window();
            var page = new AntiMotionSicknessWindow(window);
            page.RequestedTheme = ThemeService.CurrentElementTheme;

            window.Content = page;
            BackdropService.ApplyBackdrop(window);
            window.AppWindow.Title = "游戏防晕3D";

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

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AntiMotionSickness] ExecuteAsync FAILED: {ex.Message}\n{ex.StackTrace}");
            ShowErrorDialog(context, ex);
            return Task.CompletedTask;
        }
    }

    private static async void ShowErrorDialog(BuiltinToolContext context, Exception ex)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "游戏防晕3D - 启动失败",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = $"{ex.Message}\n\n{ex.StackTrace}",
                        IsTextSelectionEnabled = true,
                        TextWrapping = TextWrapping.Wrap
                    }
                },
                CloseButtonText = "确定",
                XamlRoot = context.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await dialog.ShowAsync();
        }
        catch { }
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
