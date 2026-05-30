using Microsoft.UI.Xaml;
using Windows.UI;

namespace TubaWinUi3.Services;

internal static class ThemeColors
{
    private static bool IsDark
    {
        get
        {
            // 优先使用应用手动设置的主题
            var appTheme = ThemeService.CurrentTheme;
            if (appTheme == AppTheme.Dark) return true;
            if (appTheme == AppTheme.Light) return false;
            // Default 跟随系统
            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
    }

    public static Color CardBg => IsDark ? Color.FromArgb(255, 45, 45, 45) : Color.FromArgb(255, 249, 249, 249);
    public static Color BorderColor => IsDark ? Color.FromArgb(255, 60, 60, 60) : Color.FromArgb(255, 229, 229, 229);
    public static Color DimText => IsDark ? Color.FromArgb(255, 140, 140, 140) : Color.FromArgb(255, 110, 110, 110);
    public static Color PrimaryText => IsDark ? Color.FromArgb(255, 210, 210, 210) : Color.FromArgb(255, 30, 30, 30);
    public static Color HeaderBg => IsDark ? Color.FromArgb(255, 38, 38, 38) : Color.FromArgb(255, 245, 245, 245);
    public static Color RowHover => IsDark ? Color.FromArgb(255, 50, 50, 50) : Color.FromArgb(255, 240, 240, 240);
    public static Color DisabledBg => IsDark ? Color.FromArgb(255, 55, 55, 55) : Color.FromArgb(255, 240, 240, 240);
    public static Color KeyDefault => IsDark ? Color.FromArgb(255, 55, 55, 55) : Color.FromArgb(255, 230, 230, 230);
    public static Color KeyBorder => IsDark ? Color.FromArgb(255, 75, 75, 75) : Color.FromArgb(255, 200, 200, 200);
    public static Color KeyText => IsDark ? Color.FromArgb(255, 210, 210, 210) : Color.FromArgb(255, 30, 30, 30);
    public static Color KeyboardBg => IsDark ? Color.FromArgb(255, 35, 35, 35) : Color.FromArgb(255, 240, 240, 240);
    public static Color Separator => IsDark ? Color.FromArgb(255, 50, 50, 50) : Color.FromArgb(255, 220, 220, 220);

    public static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    public static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    public static readonly Color AccentOrange = Color.FromArgb(255, 251, 191, 36);
    public static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);
    public static readonly Color AccentPurple = Color.FromArgb(255, 167, 139, 250);
    public static readonly Color DodgerBlue = Color.FromArgb(255, 30, 144, 255);
}
