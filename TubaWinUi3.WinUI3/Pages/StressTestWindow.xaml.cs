using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class StressTestWindow : Window
{
    public StressTestWindow()
    {
        InitializeComponent();

        StressControl.OwnerWindow = this;

        AppWindow.Title = "一键双烤";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        try
        {
            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea is not null)
            {
                var workArea = displayArea.WorkArea;
                var w = (int)(workArea.Width * 0.82);
                var h = (int)(workArea.Height * 0.85);
                AppWindow.Resize(new SizeInt32(w, h));
                AppWindow.Move(new PointInt32(
                    workArea.X + (int)((workArea.Width - w) / 2),
                    workArea.Y + (int)((workArea.Height - h) / 2)));
            }
        }
        catch
        {
            AppWindow.Resize(new SizeInt32(960, 820));
        }

        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = true;
            p.IsMaximizable = true;
        }

        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ApplyTitleBarTheme();

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        BackdropService.ApplyBackdrop(this);

        Closed += StressTestWindow_Closed;
    }

    private void StressTestWindow_Closed(object sender, WindowEventArgs args) => StressControl.Cleanup();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplyTitleBarTheme()
    {
        var tb = AppWindow.TitleBar;
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
