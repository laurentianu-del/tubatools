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
            // MSIX 下随包缓存位于只读包目录 WindowsApps\<pkg>\Metadata\，
            // 而 ToolsRoot 指向 LocalState 工具包目录（parent 计算会指向不存在的副本）。
            // 包内缓存存在时才优先读取（Store 构建 ExcludeToolsFromPublish 通常不生成，行为不变）
            if (RuntimeHelper.IsMsixPackaged)
            {
                var bundledInPackage = Path.Combine(ToolCatalog.AppDirectory, "Metadata", "tool_cache.json");
                if (File.Exists(bundledInPackage))
                    return bundledInPackage;
            }

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

            var toolsRoot = ToolCatalog.ToolsRoot;
            var originalCount = data.Entries.Count;
            var dropped = 0;

            foreach (var e in data.Entries)
            {
                var expandedPath = PathResolver.MakeAbsolute(e.Path);
                // 路径基准校验：工具必须位于当前 ToolsRoot 之下（含分隔符边界）。
                // 拒绝旧安装位置/路径基准变化后残留的绝对路径条目，避免打开非打包路径的程序
                if (!IsPathUnderToolsRoot(expandedPath, toolsRoot))
                {
                    dropped++;
                    continue;
                }

                if (!string.IsNullOrEmpty(e.Path) && e.Path.Contains('{'))
                {
                    var expanded = new ToolCacheEntry
                    {
                        Name = e.Name,
                        Category = e.Category,
                        PrimaryCategory = e.PrimaryCategory,
                        Categories = e.Categories,
                        IsLinked = e.IsLinked,
                        Path = expandedPath,
                        RelativePath = e.RelativePath,
                        Extension = e.Extension,
                        Description = e.Description,
                        Publisher = e.Publisher,
                        Version = e.Version,
                        DownloadUrl = e.DownloadUrl,
                        WingetId = e.WingetId,
                        IconGlyph = e.IconGlyph,
                        PrimaryArch = e.PrimaryArch,
                        AlternateVersions = e.AlternateVersions.Select(a => new ArchVariantEntry
                        {
                            Name = a.Name,
                            Path = PathResolver.MakeAbsolute(a.Path),
                            Arch = a.Arch
                        }).ToList(),
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

            // 大量条目指向 ToolsRoot 之外（旧安装位置等）→ 缓存整体作废，回退全量扫描，
            // 避免只加载残缺列表（保留数不足原数量一半时作废）
            if (dropped > 0 && entries.Count * 2 < originalCount)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 校验路径是否位于当前 ToolsRoot 之下（含路径分隔符边界，杜绝 ToolsExtra 误匹配）。
    /// </summary>
    private static bool IsPathUnderToolsRoot(string? path, string? toolsRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(toolsRoot))
            return false;
        if (!Path.IsPathRooted(path) || !Path.IsPathRooted(toolsRoot))
            return false;
        if (!path.StartsWith(toolsRoot, StringComparison.OrdinalIgnoreCase))
            return false;
        if (path.Length == toolsRoot.Length)
            return true;
        var next = path[toolsRoot.Length];
        return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
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
                PrimaryCategory = e.PrimaryCategory,
                Categories = e.Categories,
                IsLinked = e.IsLinked,
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
                AlternateVersions = e.AlternateVersions.Select(a => new ArchVariantEntry
                {
                    Name = a.Name,
                    Path = PathResolver.MakeRelative(a.Path),
                    Arch = a.Arch
                }).ToList(),
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
    public string? PrimaryCategory { get; init; }
    public List<string> Categories { get; init; } = [];
    public bool IsLinked { get; init; }
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
    public List<ArchVariantEntry> AlternateVersions { get; init; } = [];
    public List<string> Tags { get; init; } = [];
    public bool IsFavorite { get; init; }
    public bool IsBuiltinLink { get; init; }
    public string? BuiltinToolId { get; init; }
    public string? BuiltinKindText { get; init; }
    public string? TutorialUrl { get; init; }
}

/// <summary><see cref="ArchVariant"/> 的可序列化缓存表示。</summary>
public sealed record ArchVariantEntry
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Arch { get; init; } = "";
}
