using Windows.Storage;

namespace TubaWinUi3.Services;

public static class FavoritesService
{
    private const string Key = "FavoriteTools";
    private static List<string>? _cache;

    private static ApplicationDataContainer? GetSettings()
    {
        try
        {
            return ApplicationData.Current.LocalSettings;
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<string> GetFavorites()
    {
        if (_cache is not null)
            return _cache;

        var settings = GetSettings();
        if (settings is null || settings.Values[Key] is not string json)
        {
            _cache = [];
            return _cache;
        }

        try
        {
            _cache = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            _cache = [];
        }

        return _cache;
    }

    public static bool IsFavorite(string toolPath)
    {
        return GetFavorites().Contains(toolPath, StringComparer.OrdinalIgnoreCase);
    }

    public static void AddFavorite(string toolPath)
    {
        var list = GetFavorites()
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (list.Contains(toolPath, StringComparer.OrdinalIgnoreCase))
            return;

        list.Add(toolPath);
        _cache = list;
        Save(list);
    }

    public static void RemoveFavorite(string toolPath)
    {
        var list = GetFavorites()
            .Where(p => !p.Equals(toolPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _cache = list;
        Save(list);
    }

    public static void RemoveAll()
    {
        _cache = [];
        Save([]);
    }

    public static void ToggleFavorite(string toolPath)
    {
        if (IsFavorite(toolPath))
            RemoveFavorite(toolPath);
        else
            AddFavorite(toolPath);
    }

    private static void Save(List<string> favorites)
    {
        var settings = GetSettings();
        if (settings is null)
            return;

        var json = System.Text.Json.JsonSerializer.Serialize(favorites);
        settings.Values[Key] = json;
    }
}
