using System.Text.Json;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

// 依赖 ToolCatalog/ToolCacheService 全局状态,与 ToolCatalogTests/FavoritesServiceTests 串行执行
[Collection("GlobalConfigTests")]
public class ToolCacheServiceTests
{
    private static string CreateTempToolsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tubacache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void ComputeFingerprint_SameStructure_ReturnsSameHash()
    {
        var root = CreateTempToolsRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "处理器工具", "CPU-Z"));
            Directory.CreateDirectory(Path.Combine(root, "显卡工具", "GPU-Z"));
            File.WriteAllText(Path.Combine(root, "Version"), "2026.01");

            var a = ToolCacheService.ComputeFingerprint(root);
            var b = ToolCacheService.ComputeFingerprint(root);

            Assert.False(string.IsNullOrEmpty(a));
            Assert.Equal(a, b);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ComputeFingerprint_ToolAdded_ChangesHash()
    {
        var root = CreateTempToolsRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "处理器工具", "CPU-Z"));
            File.WriteAllText(Path.Combine(root, "Version"), "2026.01");
            var before = ToolCacheService.ComputeFingerprint(root);

            Directory.CreateDirectory(Path.Combine(root, "处理器工具", "新工具"));
            var after = ToolCacheService.ComputeFingerprint(root);

            Assert.NotEqual(before, after);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ComputeFingerprint_VersionFileChanged_ChangesHash()
    {
        var root = CreateTempToolsRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "处理器工具", "CPU-Z"));
            File.WriteAllText(Path.Combine(root, "Version"), "2026.01");
            var before = ToolCacheService.ComputeFingerprint(root);

            File.WriteAllText(Path.Combine(root, "Version"), "2026.02");
            var after = ToolCacheService.ComputeFingerprint(root);

            Assert.NotEqual(before, after);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveCacheTo_RoundTrips_Entries()
    {
        var outFile = Path.Combine(Path.GetTempPath(), "tubacache_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var entries = new List<ToolCacheEntry>
            {
                new()
                {
                    Name = "CPU-Z",
                    Category = "处理器工具",
                    Path = @"D:\tools\CPU-Z\cpuz_x64.exe",
                    RelativePath = @"CPU-Z\cpuz_x64.exe",
                    Extension = "EXE",
                    Description = "处理器信息检测",
                    Publisher = "CPUID",
                    Version = "2.09",
                    PrimaryArch = "x64",
                    Tags = ["检测", "CPU"]
                }
            };

            ToolCacheService.SaveCacheTo(entries, outFile);
            Assert.True(File.Exists(outFile));

            var json = File.ReadAllText(outFile);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("Version", out var version));
            Assert.True(doc.RootElement.TryGetProperty("Fingerprint", out var fingerprint));
            Assert.False(string.IsNullOrEmpty(fingerprint.GetString()));
            Assert.True(doc.RootElement.TryGetProperty("Entries", out var entriesJson));

            var loaded = JsonSerializer.Deserialize<List<ToolCacheEntry>>(entriesJson.GetRawText());
            Assert.NotNull(loaded);
            var item = Assert.Single(loaded!);
            Assert.Equal("CPU-Z", item.Name);
            Assert.Equal("处理器工具", item.Category);
            Assert.Equal("cpuz_x64.exe", Path.GetFileName(item.Path));
            Assert.Equal(["检测", "CPU"], item.Tags);
        }
        finally
        {
            try { File.Delete(outFile); } catch { }
        }
    }

    [Fact]
    public async Task BundledCache_Loads_AndFillsCategoryCache()
    {
        // 需要真实 Tools 目录（开发/CI 环境存在；否则跳过）
        if (!Directory.Exists(ToolCatalog.ToolsRoot))
            return;

        // 1. 生成随包缓存（与 --build-tool-cache 构建模式同款逻辑：各分类原始列表）
        var bundledPath = ToolCacheService.BundledCachePath;
        Assert.False(string.IsNullOrEmpty(bundledPath));
        Directory.CreateDirectory(Path.GetDirectoryName(bundledPath)!);
        var tools = ToolCatalog.GetAllToolsCached();
        Assert.True(tools.Count > 0);
        ToolCacheService.SaveCacheTo(ToolCatalog.BuildCacheEntries(), bundledPath);

        try
        {
            // 2. 模拟冷启动：清空内存缓存 + AppData 缓存
            ToolCacheService.Invalidate();
            ToolCatalog.InvalidateTagsCache();
            Assert.False(ToolCatalog.IsCacheReady);

            // 3. 应从随包缓存秒读（不扫描），数量一致
            var loaded = await ToolCatalog.GetAllToolsAsync();
            Assert.Equal(tools.Count, loaded.Count);

            // 4. 分类缓存已填充 → 分类视图直接命中
            var category = loaded[0].Category;
            var catTools = ToolCatalog.GetTools(category);
            Assert.NotEmpty(catTools);
            Assert.All(catTools, t => Assert.Equal(category, t.Category, ignoreCase: true));
        }
        finally
        {
            ToolCatalog.InvalidateTagsCache();
            try { File.Delete(bundledPath); } catch { }
        }
    }

    [Fact]
    public async Task BundledCache_AllToolsList_MatchesLiveScan()
    {
        // 需要真实 Tools 目录（开发/CI 环境存在；否则跳过）
        if (!Directory.Exists(ToolCatalog.ToolsRoot))
            return;

        // 从磁盘真实扫描得到「全部工具」一览
        var live = ToolCatalog.GetAllToolsCached().ToList();

        // 从缓存加载（模拟冷启动），应得到与实时扫描完全一致的列表
        var bundledPath = ToolCacheService.BundledCachePath;
        Assert.False(string.IsNullOrEmpty(bundledPath));
        Directory.CreateDirectory(Path.GetDirectoryName(bundledPath)!);
        ToolCacheService.SaveCacheTo(ToolCatalog.BuildCacheEntries(), bundledPath);
        try
        {
            ToolCacheService.Invalidate();
            ToolCatalog.InvalidateTagsCache();
            var loaded = (await ToolCatalog.GetAllToolsAsync()).ToList();

            // 逐项对比名称 + 主分类（去重 key 为 PrimaryCategory|Name），找出差异工具
            var liveKeys = live.Select(t => (Key: (t.PrimaryCategory ?? t.Category) + "|" + t.Name, Name: t.Name, Cat: t.Category)).ToList();
            var loadedKeys = loaded.Select(t => (Key: (t.PrimaryCategory ?? t.Category) + "|" + t.Name, Name: t.Name, Cat: t.Category)).ToList();
            var onlyLive = liveKeys.Where(l => !loadedKeys.Any(x => x.Key.Equals(l.Key, StringComparison.OrdinalIgnoreCase))).ToList();
            var onlyLoaded = loadedKeys.Where(l => !liveKeys.Any(x => x.Key.Equals(l.Key, StringComparison.OrdinalIgnoreCase))).ToList();

            Assert.True(liveKeys.Count == loadedKeys.Count,
                $"全部工具一览不一致: live={liveKeys.Count} cache={loadedKeys.Count} | 仅实时扫描有: {string.Join(", ", onlyLive.Select(x => $"{x.Name}@{x.Cat}"))} | 仅缓存有: {string.Join(", ", onlyLoaded.Select(x => $"{x.Name}@{x.Cat}"))}");

            foreach (var l in liveKeys)
                Assert.Contains(l.Key, loadedKeys.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            ToolCatalog.InvalidateTagsCache();
            try { File.Delete(bundledPath); } catch { }
        }
    }

    [Fact]
    public void ToCacheEntries_MapsAllFields()
    {
        var tool = new TubaWinUi3.Models.ToolItem
        {
            Name = "鲁大师",
            Category = "综合检测",
            PrimaryCategory = "工具集",
            IsLinked = true,
            Categories = ["综合检测", "工具集"],
            Path = @"D:\tools\综合检测\鲁大师\master.exe",
            RelativePath = @"鲁大师\master.exe",
            Extension = "EXE",
            Description = "综合性能检测",
            Publisher = "LuDaShi",
            Version = "6.1",
            WingetId = "LuDaShi.LuDaShi",
            PrimaryArch = "x64",
            Tags = ["检测"],
            IsFavorite = true,
            IsBuiltinLink = false
        };

        var entries = ToolCatalog.ToCacheEntries([tool]);
        var entry = Assert.Single(entries);

        Assert.Equal(tool.Name, entry.Name);
        Assert.Equal(tool.Category, entry.Category);
        Assert.Equal(tool.PrimaryCategory, entry.PrimaryCategory);
        Assert.Equal(["综合检测", "工具集"], entry.Categories);
        Assert.True(entry.IsLinked);
        Assert.Equal(tool.Path, entry.Path);
        Assert.Equal(tool.Description, entry.Description);
        Assert.Equal(tool.WingetId, entry.WingetId);
        Assert.True(entry.IsFavorite);
        Assert.Equal("检测", Assert.Single(entry.Tags));
    }

    [Fact]
    public async Task CacheLoad_Preserves_LinkedMultiCategoryTools()
    {
        // 回归：缓存构建不能把 link.json 关联工具的「目标分类」副本搞没，
        // 也不能丢失多分类信息（此前 v4 缓存存的是去重后的全部工具一览）。
        var root = CreateTempToolsRoot();
        ToolCatalog.SetToolsRootForBuild(root);
        try
        {
            // 真实工具在「综合检测」，处理器工具通过 link.json 关联它 → 多分类
            Directory.CreateDirectory(Path.Combine(root, "综合检测", "CPUZ"));
            File.WriteAllText(Path.Combine(root, "综合检测", "CPUZ", "cpuz.exe"), "x");
            Directory.CreateDirectory(Path.Combine(root, "处理器工具", "CPUZ"));
            File.WriteAllText(Path.Combine(root, "处理器工具", "CPUZ", "link.json"),
                """{"target":"综合检测/CPUZ"}""");

            ToolCatalog.InvalidateTagsCache();
            var all = ToolCatalog.GetAllToolsCached();
            var scanned = Assert.Single(all.Where(t => t.Name.Equals("cpuz", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("综合检测", scanned.Categories);

            // 模拟 --build-tool-cache：按各分类原始列表写盘
            var bundledPath = ToolCacheService.BundledCachePath;
            Assert.False(string.IsNullOrEmpty(bundledPath));
            Directory.CreateDirectory(Path.GetDirectoryName(bundledPath)!);
            ToolCacheService.SaveCacheTo(ToolCatalog.BuildCacheEntries(), bundledPath);

            // 模拟冷启动：清空内存 + AppData 缓存 → 从随包缓存加载
            ToolCacheService.Invalidate();
            ToolCatalog.InvalidateTagsCache();
            var loaded = await ToolCatalog.GetAllToolsAsync();

            // ① 全部工具一览只保留一份，多分类信息不丢失
            var loadedCuz = Assert.Single(loaded.Where(t => t.Name.Equals("cpuz", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("综合检测", loadedCuz.Categories);
            Assert.Contains("处理器工具", loadedCuz.Categories);

            // ② 目标分类「综合检测」下仍有该工具（此前从缓存加载后消失）
            Assert.Contains(ToolCatalog.GetTools("综合检测"),
                t => t.Name.Equals("cpuz", StringComparison.OrdinalIgnoreCase));

            // ③ 链接分类「处理器工具」下仍有关联副本
            var linked = Assert.Single(ToolCatalog.GetTools("处理器工具"));
            Assert.Equal("处理器工具", linked.Category);
            Assert.True(linked.IsLinked);
            Assert.Equal("综合检测", linked.PrimaryCategory);
            Assert.Contains("综合检测", linked.Categories);
        }
        finally
        {
            ToolCatalog.SetToolsRootForBuild(null);
            try { File.Delete(ToolCacheService.BundledCachePath); } catch { }
            try { ToolCatalog.InvalidateTagsCache(); } catch { }
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryLoadBundledCache_RejectsPathsOutsideToolsRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "tubacache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "处理器工具", "CPU-Z"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "处理器工具", "GPU-Z"));
        ToolCatalog.SetToolsRootForBuild(tempRoot);
        try
        {
            var bundledPath = ToolCacheService.BundledCachePath;
            Assert.False(string.IsNullOrEmpty(bundledPath));

            var entries = new List<ToolCacheEntry>
            {
                // 合法：占位符形式，展开后位于当前 ToolsRoot 之下
                new()
                {
                    Name = "CPU-Z",
                    Category = "处理器工具",
                    Path = "{ToolsRoot}\\处理器工具\\CPU-Z\\cpuz_x64.exe",
                    RelativePath = @"处理器工具\CPU-Z\cpuz_x64.exe",
                    Extension = "EXE"
                },
                // 不合法：旧安装位置（非打包路径）的绝对路径 → 应被丢弃
                new()
                {
                    Name = "GPU-Z",
                    Category = "处理器工具",
                    Path = @"C:\Program Files\TubaWinUi3\Tools\处理器工具\GPU-Z\gpu-z.exe",
                    RelativePath = @"处理器工具\GPU-Z\gpu-z.exe",
                    Extension = "EXE"
                }
            };
            ToolCacheService.SaveCacheTo(entries, bundledPath);

            Assert.True(ToolCacheService.TryLoadBundledCache(out var loaded));
            var item = Assert.Single(loaded);
            Assert.Equal("CPU-Z", item.Name);
            Assert.StartsWith(tempRoot, item.Path);
        }
        finally
        {
            try { File.Delete(ToolCacheService.BundledCachePath); } catch { }
            ToolCatalog.SetToolsRootForBuild(null);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void TryLoadBundledCache_MostlyOutsideToolsRoot_InvalidatesWholeCache()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "tubacache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "处理器工具", "CPU-Z"));
        ToolCatalog.SetToolsRootForBuild(tempRoot);
        try
        {
            var bundledPath = ToolCacheService.BundledCachePath;

            var entries = new List<ToolCacheEntry>
            {
                new()
                {
                    Name = "合法",
                    Category = "处理器工具",
                    Path = "{ToolsRoot}\\处理器工具\\CPU-Z\\cpuz_x64.exe",
                    RelativePath = @"处理器工具\CPU-Z\cpuz_x64.exe",
                    Extension = "EXE"
                },
                new()
                {
                    Name = "旧路径1",
                    Category = "处理器工具",
                    Path = @"C:\Old\Tools\a.exe",
                    RelativePath = @"a.exe",
                    Extension = "EXE"
                },
                new()
                {
                    Name = "旧路径2",
                    Category = "处理器工具",
                    Path = @"C:\Old\Tools\b.exe",
                    RelativePath = @"b.exe",
                    Extension = "EXE"
                }
            };
            ToolCacheService.SaveCacheTo(entries, bundledPath);

            // 过半条目指向 ToolsRoot 之外 → 缓存整体作废，回退全量扫描
            Assert.False(ToolCacheService.TryLoadBundledCache(out _));
        }
        finally
        {
            try { File.Delete(ToolCacheService.BundledCachePath); } catch { }
            ToolCatalog.SetToolsRootForBuild(null);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void MakeRelative_RespectsDirectoryBoundary()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "tubacache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "工具"));
        ToolCatalog.SetToolsRootForBuild(tempRoot);
        try
        {
            // ToolsExtra 与 ToolsRoot 同名前缀但不在其下 → 不得匹配 {ToolsRoot}
            var outside = Path.Combine(tempRoot + "Extra", "a.exe");
            Assert.Equal(outside, PathResolver.MakeRelative(outside));

            // 正常子路径 → {ToolsRoot}\...
            var inside = Path.Combine(tempRoot, "工具", "a.exe");
            var rel = PathResolver.MakeRelative(inside);
            Assert.StartsWith("{ToolsRoot}", rel);
            Assert.Contains("工具", rel);
        }
        finally
        {
            ToolCatalog.SetToolsRootForBuild(null);
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
