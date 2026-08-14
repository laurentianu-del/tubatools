using System.Text.Json;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

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

        // 1. 生成随包缓存（与 --build-tool-cache 构建模式同款逻辑）
        var bundledPath = ToolCacheService.BundledCachePath;
        Assert.False(string.IsNullOrEmpty(bundledPath));
        Directory.CreateDirectory(Path.GetDirectoryName(bundledPath)!);
        var tools = ToolCatalog.GetAllToolsCached();
        Assert.True(tools.Count > 0);
        ToolCacheService.SaveCacheTo(ToolCatalog.ToCacheEntries(tools), bundledPath);

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
    public void ToCacheEntries_MapsAllFields()
    {
        var tool = new TubaWinUi3.Models.ToolItem
        {
            Name = "鲁大师",
            Category = "综合检测",
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
        Assert.Equal(tool.Path, entry.Path);
        Assert.Equal(tool.Description, entry.Description);
        Assert.Equal(tool.WingetId, entry.WingetId);
        Assert.True(entry.IsFavorite);
        Assert.Equal("检测", Assert.Single(entry.Tags));
    }
}
