using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Services;

public static class ThemeService
{
    private const string Key = "AppTheme";
    private static AppTheme _currentTheme = AppTheme.Default;

    /// <summary>
    /// 主题应用后触发（参数为解析后的 ElementTheme）。
    /// 子窗口/对话框宿主订阅此事件以实现主题实时跟随。
    /// </summary>
    public static event Action<ElementTheme>? ThemeChanged;

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

        if (window is MainWindow mw)
            mw.ApplyTitleBarTheme(elementTheme);

        ThemeChanged?.Invoke(elementTheme);
    }
}

public enum AppTheme
{
    Default,
    Light,
    Dark
}
