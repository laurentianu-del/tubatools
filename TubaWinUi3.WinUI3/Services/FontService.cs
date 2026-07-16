using System.Drawing.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Services;

public static class FontService
{
    private const string FontKey = "InterfaceFont";

    public static string DefaultFont => "MiSans";

    public static event Action? FontChanged;

    public static string GetCurrentFont()
    {
        try
        {
            var saved = AppSettings.Get(FontKey);
            return string.IsNullOrEmpty(saved) ? DefaultFont : saved;
        }
        catch
        {
            return DefaultFont;
        }
    }

    public static void SetFont(string fontFamilyName)
    {
        AppSettings.Set(FontKey, fontFamilyName);
        FontChanged?.Invoke();
    }

    public static void ApplySavedFonts()
    {
        try
        {
            var font = GetCurrentFont();

            FontFamily appFont;
            if (font == DefaultFont)
            {
                appFont = new FontFamily("ms-appx:///Fonts/MiSans-Medium.otf#MiSans");
            }
            else
            {
                appFont = new FontFamily(font);
            }

            if (App.Current?.Resources != null)
            {
                if (App.Current.Resources.TryGetValue("AppFontFamily", out var existing) && existing is FontFamily)
                {
                    App.Current.Resources["AppFontFamily"] = appFont;
                }
                else
                {
                    App.Current.Resources.Add("AppFontFamily", appFont);
                }
            }
        }
        catch
        {
        }
    }

    public static FontFamily GetFontFamily()
    {
        var font = GetCurrentFont();
        if (font == DefaultFont)
        {
            return new FontFamily("ms-appx:///Fonts/MiSans-Medium.otf#MiSans");
        }
        return new FontFamily(font);
    }

    public static List<string> GetInstalledFonts()
    {
        var fonts = new List<string>();
        fonts.Add("MiSans（默认）");

        var preferred = new[] { "微软雅黑", "宋体", "黑体", "楷体", "仿宋", "思源黑体", "思源宋体", "华文黑体", "华文宋体", "PingFang SC", "苹方-简", "Segoe UI", "Arial", "Calibri", "Tahoma", "Verdana" };

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