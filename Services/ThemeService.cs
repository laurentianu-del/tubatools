using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Services;

public static class ThemeService
{
    private const string Key = "AppTheme";
    private static AppTheme _currentTheme = AppTheme.Default;

    public static AppTheme CurrentTheme => _currentTheme;

    public static ElementTheme CurrentElementTheme => _currentTheme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    public static void SetTheme(AppTheme theme)
    {
        _currentTheme = theme;
        AppSettings.Set(Key, theme.ToString());
        ApplyTheme(theme);
    }

    public static void ApplySavedTheme()
    {
        var saved = AppSettings.Get(Key);
        if (saved is not null && Enum.TryParse<AppTheme>(saved, out var theme))
            _currentTheme = theme;

        ApplyTheme(_currentTheme);
    }

    private static void ApplyTheme(AppTheme theme)
    {
        var window = App.MainWindow;
        if (window?.Content is not FrameworkElement root)
            return;

        var elementTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        root.RequestedTheme = elementTheme;

        // Propagate theme to nested elements that might not inherit
        PropagateTheme(root, elementTheme);

        if (window is MainWindow mw)
            mw.ApplyTitleBarTheme(elementTheme);
    }

    private static void PropagateTheme(DependencyObject parent, ElementTheme theme)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe)
                fe.RequestedTheme = theme;
            PropagateTheme(child, theme);
        }
    }
}

public enum AppTheme
{
    Default,
    Light,
    Dark
}
