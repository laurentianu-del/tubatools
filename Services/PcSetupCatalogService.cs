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

    public static async Task CheckInstalledStatusAsync(List<CatalogCategory> categories)
    {
        var allPkgs = new List<CatalogPackage>();
        foreach (var cat in categories)
        {
            allPkgs.AddRange(cat.Packages);
            foreach (var sub in cat.SubCategories)
                allPkgs.AddRange(sub.Packages);
        }
        var tasks = allPkgs.Select(async pkg =>
        {
            pkg.State = WingetInstallState.Checking;
            var installed = await WingetService.IsInstalledAsync(pkg.Id);
            pkg.State = installed ? WingetInstallState.Installed : WingetInstallState.NotInstalled;
        });
        await Task.WhenAll(tasks);
    }

    public static string GeneratePowerShellScript(List<PcSetupAction> actions)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#Requires -RunAsAdministrator");
        sb.AppendLine($"# 新机开荒 - 自动配置脚本");
        sb.AppendLine($"# 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# 由图吧工具箱 (TubaWinUi3) 生成");
        sb.AppendLine();
        sb.AppendLine("Write-Host \"========== 新机开荒 ==========\" -ForegroundColor Cyan");
        sb.AppendLine();

        var selected = actions.Where(a => a.IsSelected).ToList();
        var wingetActions = selected.OfType<WingetInstallAction>().ToList();
        var otherActions = selected.Where(a => a is not WingetInstallAction).ToList();

        if (wingetActions.Count > 0)
        {
            sb.AppendLine("# ---- 软件安装 ----");
            sb.AppendLine($"Write-Host \"`n--- 软件安装 ({wingetActions.Count} 个) ---\" -ForegroundColor Cyan");
            for (var i = 0; i < wingetActions.Count; i++)
            {
                sb.AppendLine($"Write-Host \"`n[{i + 1}/{wingetActions.Count}] {wingetActions[i].Name}\" -ForegroundColor Yellow");
                sb.AppendLine(wingetActions[i].ToPowerShell());
            }
        }

        if (otherActions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("# ---- 系统优化 ----");
            sb.AppendLine($"Write-Host \"`n--- 系统优化 ({otherActions.Count} 项) ---\" -ForegroundColor Cyan");
            foreach (var action in otherActions)
            {
                sb.AppendLine();
                sb.AppendLine(action.ToPowerShell());
            }
        }

        sb.AppendLine();
        sb.AppendLine("Write-Host \"`n========== 全部完成 ==========\" -ForegroundColor Green");
        return sb.ToString();
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
