using System.Diagnostics;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class ToolCacheService
{
    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubaWinUi3", "tool_cache.json");

    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(24);

    public static bool TryLoadCache(out List<ToolCacheEntry> entries)
    {
        entries = [];
        try
        {
            if (!File.Exists(CachePath))
                return false;

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath);
            if (age >= CacheMaxAge)
                return false;

            var json = File.ReadAllText(CachePath);
            var data = JsonSerializer.Deserialize<ToolCacheData>(json);
            if (data?.Entries is null || data.Version != ToolCatalog.CacheVersion)
                return false;

            entries = data.Entries;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveCache(List<ToolCacheEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var data = new ToolCacheData
            {
                Version = ToolCatalog.CacheVersion,
                Entries = entries,
                SavedAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(CachePath, json);
        }
        catch { }
    }

    public static void Invalidate()
    {
        try
        {
            if (File.Exists(CachePath))
                File.Delete(CachePath);
        }
        catch { }
    }

    private sealed class ToolCacheData
    {
        public int Version { get; set; }
        public List<ToolCacheEntry> Entries { get; set; } = [];
        public DateTime SavedAt { get; set; }
    }
}

public sealed record ToolCacheEntry
{
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Path { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Extension { get; init; } = "";
    public string? Description { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public string? DownloadUrl { get; init; }
    public string? WingetId { get; init; }
    public string? IconGlyph { get; init; }
    public string? PrimaryArch { get; init; }
    public List<string> Tags { get; init; } = [];
    public bool IsFavorite { get; init; }
    public bool IsBuiltinLink { get; init; }
    public string? BuiltinToolId { get; init; }
    public string? BuiltinKindText { get; init; }
    public string? TutorialUrl { get; init; }
}