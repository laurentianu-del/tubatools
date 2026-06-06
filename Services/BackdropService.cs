using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

public enum BackdropType
{
    Mica,
    MicaAlt,
    Acrylic
}

public static class BackdropService
{
    public static event Action? BackdropChanged;

    public static BackdropType GetBackdropType()
    {
        var val = AppSettings.Get("BackdropType");
        return Enum.TryParse<BackdropType>(val, out var t) ? t : BackdropType.Mica;
    }

    public static void SetBackdropType(BackdropType type)
    {
        AppSettings.Set("BackdropType", type.ToString());
        BackdropChanged?.Invoke();
    }

    public static Color GetTintColor()
    {
        var hex = AppSettings.Get("BackdropTintColor");
        if (!string.IsNullOrEmpty(hex) && TryParseHexColor(hex, out var c))
            return c;
        return Color.FromArgb(0, 0, 0, 0);
    }

    public static void SetTintColor(Color color)
    {
        AppSettings.Set("BackdropTintColor", $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
        BackdropChanged?.Invoke();
    }

    public static void ApplyBackdrop(Window window)
    {
        var type = GetBackdropType();

        switch (type)
        {
            case BackdropType.Mica:
                window.SystemBackdrop = new MicaBackdrop();
                break;

            case BackdropType.MicaAlt:
                var mica = new MicaBackdrop();
                try
                {
                    var kindProp = typeof(MicaBackdrop).GetProperty("Kind");
                    if (kindProp is not null)
                    {
                        var kindType = kindProp.PropertyType;
                        var baseAlt = Enum.Parse(kindType, "BaseAlt");
                        kindProp.SetValue(mica, baseAlt);
                    }
                }
                catch { }
                window.SystemBackdrop = mica;
                break;

            case BackdropType.Acrylic:
                window.SystemBackdrop = new DesktopAcrylicBackdrop();
                break;
        }
    }

    private static bool TryParseHexColor(string hex, out Color color)
    {
        color = default;
        try
        {
            if (hex.StartsWith('#')) hex = hex[1..];
            if (hex.Length == 8)
            {
                var a = byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber);
                var r = byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber);
                var g = byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber);
                var b = byte.Parse(hex[6..8], System.Globalization.NumberStyles.HexNumber);
                color = Color.FromArgb(a, r, g, b);
                return true;
            }
            if (hex.Length == 6)
            {
                var r = byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber);
                var g = byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber);
                var b = byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber);
                color = Color.FromArgb(255, r, g, b);
                return true;
            }
        }
        catch { }
        return false;
    }
}