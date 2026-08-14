using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class ToolCacheService
{
    private static string CachePath => Path.Combine(
        ConfigManager.GetDataDir(), "tool_cache.json");

    /// <summary>
    /// 构建时预生成的随包缓存（<c>Metadata/tool_cache.json</c>），与 ToolsRoot 同级。
    /// 由 GenerateBundledToolCache MSBuild target 在 publish 时扫描 Tools 生成，
    /// 随包分发后运行时直接读取，免去首启全量扫描。
    /// </summary>
    public static string BundledCachePath
    {
        get
        {
            var toolsRoot = ToolCatalog.ToolsRoot;
            var parent = Path.GetDirectoryName(toolsRoot);
            return string.IsNullOrEmpty(parent) ? "" : Path.Combine(parent, "Metadata", "tool_cache.json");
        }
    }

    /// <summary>
    /// 计算 Tools 目录的内容指纹：Version 文件内容 + 分类目录名 + 各分类下的工具目录名。
    /// 用户增删工具会改变目录结构 → 指纹变化 → 缓存失效回退扫描。
    /// 只枚举目录名（毫秒级），不读文件内容。
    /// </summary>
    public static string ComputeFingerprint(string toolsRoot)
    {
        try
        {
            if (!Directory.Exists(toolsRoot))
                return "";

            var sb = new StringBuilder();
            var versionFile = Path.Combine(toolsRoot, "Version");
            if (File.Exists(versionFile))
            {
                sb.Append('V').Append(File.ReadAllText(versionFile)).Append('|');
            }

            foreach (var category in Directory.GetDirectories(toolsRoot)
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append('[').Append(Path.GetFileName(category)).Append(']');
                foreach (var toolDir in Directory.GetDirectories(category)
                             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    sb.Append(Path.GetFileName(toolDir)).Append(';');
                }
            }

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>AppData 运行时缓存（由扫描/后台刷新写入）。</summary>
    public static bool TryLoadCache(out List<ToolCacheEntry> entries)
    {
        return TryLoadCacheFrom(CachePath, out entries);
    }

    /// <summary>构建时预生成的随包缓存（校验 Tools 指纹）。</summary>
    public static bool TryLoadBundledCache(out List<ToolCacheEntry> entries)
    {
        return TryLoadCacheFrom(BundledCachePath, out entries);
    }

    private static bool TryLoadCacheFrom(string path, out List<ToolCacheEntry> entries)
    {
        entries = [];
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<ToolCacheData>(json);
            if (data?.Entries is null || data.Entries.Count == 0 || data.Version != ToolCatalog.CacheVersion)
                return false;

            // 指纹不匹配（Tools 目录内容已变化）→ 缓存失效
            var expected = ComputeFingerprint(ToolCatalog.ToolsRoot);
            if (expected.Length == 0 || !string.Equals(data.Fingerprint, expected, StringComparison.Ordinal))
                return false;

            foreach (var e in data.Entries)
            {
                if (!string.IsNullOrEmpty(e.Path) && e.Path.Contains('{'))
                {
                    var expanded = new ToolCacheEntry
                    {
                        Name = e.Name,
                        Category = e.Category,
                        Path = PathResolver.MakeAbsolute(e.Path),
                        RelativePath = e.RelativePath,
                        Extension = e.Extension,
                        Description = e.Description,
                        Publisher = e.Publisher,
                        Version = e.Version,
                        DownloadUrl = e.DownloadUrl,
                        WingetId = e.WingetId,
                        IconGlyph = e.IconGlyph,
                        PrimaryArch = e.PrimaryArch,
                        Tags = e.Tags,
                        IsFavorite = e.IsFavorite,
                        IsBuiltinLink = e.IsBuiltinLink,
                        BuiltinToolId = e.BuiltinToolId,
                        BuiltinKindText = e.BuiltinKindText,
                        TutorialUrl = e.TutorialUrl
                    };
                    entries.Add(expanded);
                }
                else
                {
                    entries.Add(e);
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveCache(List<ToolCacheEntry> entries)
    {
        SaveCacheTo(entries, CachePath);
    }

    /// <summary>把扫描结果序列化写到指定路径（构建工具缓存模式与后台刷新共用）。</summary>
    public static void SaveCacheTo(List<ToolCacheEntry> entries, string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var toSave = entries.Select(e => new ToolCacheEntry
            {
                Name = e.Name,
                Category = e.Category,
                Path = PathResolver.MakeRelative(e.Path),
                RelativePath = e.RelativePath,
                Extension = e.Extension,
                Description = e.Description,
                Publisher = e.Publisher,
                Version = e.Version,
                DownloadUrl = e.DownloadUrl,
                WingetId = e.WingetId,
                IconGlyph = e.IconGlyph,
                PrimaryArch = e.PrimaryArch,
                Tags = e.Tags,
                IsFavorite = e.IsFavorite,
                IsBuiltinLink = e.IsBuiltinLink,
                BuiltinToolId = e.BuiltinToolId,
                BuiltinKindText = e.BuiltinKindText,
                TutorialUrl = e.TutorialUrl
            }).ToList();

            var data = new ToolCacheData
            {
                Version = ToolCatalog.CacheVersion,
                Fingerprint = ComputeFingerprint(ToolCatalog.ToolsRoot),
                Entries = toSave,
                SavedAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(path, json);
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
        public string? Fingerprint { get; set; }
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
