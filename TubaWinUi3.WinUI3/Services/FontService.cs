using System.Drawing.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Services;

public static class FontService
{
    private const string ChineseFontKey = "InterfaceChineseFont";
    private const string WesternFontKey = "InterfaceWesternFont";

    public static string DefaultChineseFont => "HarmonyOS Sans SC";
    public static string DefaultWesternFont => "JetBrains Mono";

    public static event Action? FontChanged;

    public static string GetCurrentChineseFont()
    {
        try
        {
            var saved = AppSettings.Get(ChineseFontKey);
            return string.IsNullOrEmpty(saved) ? DefaultChineseFont : saved;
        }
        catch
        {
            return DefaultChineseFont;
        }
    }

    public static string GetCurrentWesternFont()
    {
        try
        {
            var saved = AppSettings.Get(WesternFontKey);
            return string.IsNullOrEmpty(saved) ? DefaultWesternFont : saved;
        }
        catch
        {
            return DefaultWesternFont;
        }
    }

    public static void SetChineseFont(string fontFamilyName)
    {
        AppSettings.Set(ChineseFontKey, fontFamilyName);
        FontChanged?.Invoke();
    }

    public static void SetWesternFont(string fontFamilyName)
    {
        AppSettings.Set(WesternFontKey, fontFamilyName);
        FontChanged?.Invoke();
    }

    public static void ApplySavedFonts()
    {
        try
        {
            var chinese = GetCurrentChineseFont();
            var western = GetCurrentWesternFont();

            FontFamily compositeFont;
            if (chinese == DefaultChineseFont && western == DefaultWesternFont)
            {
                compositeFont = new FontFamily("ms-appx:///Fonts/JetBrainsMono.ttf#JetBrains Mono, ms-appx:///Fonts/HarmonyOS_Sans_SC_Bold.otf#HarmonyOS Sans SC");
            }
            else
            {
                compositeFont = new FontFamily($"{western}, {chinese}");
            }

            if (App.Current?.Resources != null)
            {
                if (App.Current.Resources.TryGetValue("AppFontFamily", out var existing) && existing is FontFamily)
                {
                    App.Current.Resources["AppFontFamily"] = compositeFont;
                }
                else
                {
                    App.Current.Resources.Add("AppFontFamily", compositeFont);
                }
            }
        }
        catch
        {
        }
    }

    public static FontFamily GetCompositeFontFamily()
    {
        var chinese = GetCurrentChineseFont();
        var western = GetCurrentWesternFont();
        return new FontFamily($"{chinese}, {western}");
    }

    public static FontFamily GetChineseFontFamily()
    {
        return new FontFamily(GetCurrentChineseFont());
    }

    public static FontFamily GetWesternFontFamily()
    {
        return new FontFamily(GetCurrentWesternFont());
    }

    public static List<string> GetInstalledChineseFonts()
    {
        var fonts = new List<string>();
        fonts.Add("HarmonyOS Sans SC（默认）");

        var preferred = new[] { "微软雅黑", "宋体", "黑体", "楷体", "仿宋", "思源黑体", "思源宋体", "华文黑体", "华文宋体", "PingFang SC", "苹方-简" };

        using var fc = new InstalledFontCollection();
        var installed = fc.Families.Select(f => f.Name).ToHashSet();

        foreach (var p in preferred)
        {
            if (installed.Contains(p) && !fonts.Contains(p))
                fonts.Add(p);
        }

        foreach (var f in installed.OrderBy(f => f))
        {
            if (!fonts.Contains(f))
                fonts.Add(f);
        }

        return fonts;
    }

    public static List<string> GetInstalledWesternFonts()
    {
        var fonts = new List<string>();
        fonts.Add("JetBrains Mono（默认）");

        var preferred = new[] { "Segoe UI", "Arial", "Calibri", "Tahoma", "Verdana", "Helvetica", "Roboto", "SF Pro Display", "San Francisco" };

        using var fc = new InstalledFontCollection();
        var installed = fc.Families.Select(f => f.Name).ToHashSet();

        foreach (var p in preferred)
        {
            if (installed.Contains(p) && !fonts.Contains(p))
                fonts.Add(p);
        }

        foreach (var f in installed.OrderBy(f => f))
        {
            if (!fonts.Contains(f))
                fonts.Add(f);
        }

        return fonts;
    }

    public static bool IsFontInstalled(string fontFamilyName)
    {
        using var fc = new InstalledFontCollection();
        return fc.Families.Any(f => f.Name == fontFamilyName);
    }
}