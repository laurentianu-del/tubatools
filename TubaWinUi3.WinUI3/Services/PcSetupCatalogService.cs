using System.Text.Json;
using System.Text.Json.Serialization;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class PcSetupCatalogService
{
    private static List<CatalogCategory>? _cache;

    public static List<CatalogCategory> GetCatalog()
    {
        if (_cache != null) return _cache;
        var path = FindCatalogFile();
        if (!File.Exists(path))
        {
            _cache = [];
            return _cache;
        }
        try
        {
            var json = File.ReadAllText(path);
            var db = JsonSerializer.Deserialize<CatalogDatabase>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            _cache = db?.Categories ?? [];
            return _cache;
        }
        catch
        {
            _cache = [];
            return _cache;
        }
    }

    public static void InvalidateCache() => _cache = null;

    public static List<WingetInstallAction> ToInstallActions(List<CatalogCategory> categories)
    {
        var actions = new List<WingetInstallAction>();
        foreach (var cat in categories)
        {
            foreach (var pkg in cat.Packages)
            {
                if (!pkg.IsSelected) continue;
                actions.Add(new WingetInstallAction
                {
                    Id = $"winget-{pkg.Id}",
                    Name = pkg.Name,
                    Description = pkg.Desc ?? "",
                    Glyph = cat.Glyph,
                    Group = cat.Name,
                    IsSelected = true,
                    IsDangerous = false,
                    RequiresAdmin = false,
                    PackageId = pkg.Id
                });
            }
            foreach (var sub in cat.SubCategories)
            {
                foreach (var pkg in sub.Packages)
                {
                    if (!pkg.IsSelected) continue;
                    actions.Add(new WingetInstallAction
                    {
                        Id = $"winget-{pkg.Id}",
                        Name = pkg.Name,
                        Description = pkg.Desc ?? "",
                        Glyph = cat.Glyph,
                        Group = $"{cat.Name}/{sub.Name}",
                        IsSelected = true,
                        IsDangerous = false,
                        RequiresAdmin = false,
                        PackageId = pkg.Id
                    });
                }
            }
        }
        return actions;
    }

    public static async Task CheckInstalledStatusAsync(List<CatalogCategory> categories, IProgress<(int Done, int Total, string Name)>? progress = null)
    {
        var allPkgs = new List<CatalogPackage>();
        foreach (var cat in categories)
        {
            allPkgs.AddRange(cat.Packages);
            foreach (var sub in cat.SubCategories)
                allPkgs.AddRange(sub.Packages);
        }

        var completed = 0;
        var total = allPkgs.Count;

        const int batchSize = 4;
        for (var i = 0; i < allPkgs.Count; i += batchSize)
        {
            var batch = allPkgs.Skip(i).Take(batchSize).ToList();
            var tasks = batch.Select(async pkg =>
            {
                pkg.State = WingetInstallState.Checking;
                var installed = await WingetService.IsInstalledAsync(pkg.Id);
                pkg.State = installed ? WingetInstallState.Installed : WingetInstallState.NotInstalled;
            });
            await Task.WhenAll(tasks);

            completed += batch.Count;
            progress?.Report((completed, total, batch[^1].Name));
        }
    }

    private static string FindCatalogFile()
    {
        var dir = FindRoot("Metadata");
        return Path.Combine(dir, "pcsetup_catalog.json");
    }

    private static string FindRoot(string folderName)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, folderName);
            if (Directory.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null) break;
            dir = parent;
        }
        return Path.Combine(AppContext.BaseDirectory, folderName);
    }

    private sealed class CatalogDatabase
    {
        [JsonPropertyName("categories")]
        public List<CatalogCategory>? Categories { get; set; }
    }
}
