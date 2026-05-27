using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Text.Json;

namespace TubaWinUi3.Services;

public static class BackgroundService
{
    private const string PathKey = "BackgroundImagePath";
    private const string OpacityKey = "BackgroundOpacity";

    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubaWinUi3", "settings.json");

    private static Dictionary<string, string> _cache;
    private static bool _dirty;

    private static Dictionary<string, string> LoadSettings()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            }
            else
            {
                _cache = [];
            }
        }
        catch
        {
            _cache = [];
        }
        return _cache;
    }

    private static void SaveSettings()
    {
        if (!_dirty || _cache is null) return;
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_cache);
            File.WriteAllText(_settingsPath, json);
            _dirty = false;
        }
        catch { }
    }

    public static string? GetBackgroundPath()
    {
        var s = LoadSettings();
        return s.TryGetValue(PathKey, out var v) ? v : null;
    }

    public static void SetBackgroundPath(string? path)
    {
        var s = LoadSettings();
        if (path is not null)
            s[PathKey] = path;
        else
            s.Remove(PathKey);
        _dirty = true;
        SaveSettings();
    }

    public static double GetBackgroundOpacity()
    {
        var s = LoadSettings();
        if (s.TryGetValue(OpacityKey, out var v) && double.TryParse(v, out var opacity))
            return opacity;
        return 0.15;
    }

    public static void SetBackgroundOpacity(double opacity)
    {
        var s = LoadSettings();
        s[OpacityKey] = opacity.ToString("F2");
        _dirty = true;
        SaveSettings();
    }

    public static BitmapImage? LoadBackgroundImage()
    {
        var path = GetBackgroundPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return new BitmapImage(new Uri(path));
        }
        catch { return null; }
    }
}
