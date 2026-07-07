namespace TubaWinUi3.Services;

public sealed class JunkCategory
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Glyph { get; init; } = "";
    public string ColorHex { get; init; } = "#60A5FA";
    public bool Selected { get; set; } = true;
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public List<string> Paths { get; set; } = [];
}

public sealed class CleanProgress
{
    public required int Current { get; init; }
    public required int Total { get; init; }
    public required string CurrentPath { get; init; }
    public required long CleanedBytes { get; init; }
}

public static class JunkCleanerService
{
    public static async Task<List<JunkCategory>> ScanAsync(CancellationToken ct = default)
    {
        return await Task.Run(() => Scan(ct), ct);
    }

    private static List<JunkCategory> Scan(CancellationToken ct)
    {
        var categories = CreateCategories();

        foreach (var cat in categories)
        {
            if (ct.IsCancellationRequested) break;
            ScanCategory(cat, ct);
        }

        return categories;
    }

    private static List<JunkCategory> CreateCategories()
    {
        return
        [
            new JunkCategory { Name = "系统临时文件", Description = "Windows Temp 目录中的临时文件", Glyph = "\uE72E", ColorHex = "#60A5FA" },
            new JunkCategory { Name = "用户临时文件", Description = "用户 %TEMP% 目录中的临时文件", Glyph = "\uE8C8", ColorHex = "#A78BFA" },
            new JunkCategory { Name = "回收站", Description = "已删除但未永久删除的文件", Glyph = "\uE74D", ColorHex = "#F472B6" },
            new JunkCategory { Name = "浏览器缓存", Description = "Chrome/Edge/Firefox 浏览器缓存", Glyph = "\uE774", ColorHex = "#34D399" },
            new JunkCategory { Name = "缩略图缓存", Description = "Windows 资源管理器缩略图数据库", Glyph = "\uEB54", ColorHex = "#FBBF24" },
            new JunkCategory { Name = "Windows 更新缓存", Description = "Windows Update 下载缓存", Glyph = "\uE895", ColorHex = "#FB923C" },
            new JunkCategory { Name = "日志文件", Description = "系统与应用日志文件", Glyph = "\uE9D9", ColorHex = "#F87171" },
            new JunkCategory { Name = "预读取文件", Description = "Windows Prefetch 预读取数据", Glyph = "\uE72C", ColorHex = "#818CF8" },
        ];
    }

    private static void ScanCategory(JunkCategory cat, CancellationToken ct)
    {
        cat.Paths.Clear();
        cat.SizeBytes = 0;
        cat.FileCount = 0;

        var targets = GetCategoryPaths(cat.Name);

        foreach (var target in targets)
        {
            if (ct.IsCancellationRequested) break;

            if (!Directory.Exists(target)) continue;

            try
            {
                var files = Directory.EnumerateFiles(target, "*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false,
                    RecurseSubdirectories = true
                });

                foreach (var file in files)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Exists)
                        {
                            cat.SizeBytes += fi.Length;
                            cat.FileCount++;
                            cat.Paths.Add(file);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    public static long Clean(List<JunkCategory> categories, IProgress<CleanProgress>? progress = null, CancellationToken ct = default)
    {
        long totalCleaned = 0;
        var selectedCats = categories.Where(c => c.Selected && c.FileCount > 0).ToList();
        var totalItems = selectedCats.Sum(c => c.FileCount);
        var current = 0;

        foreach (var cat in selectedCats)
        {
            if (ct.IsCancellationRequested) break;

            foreach (var path in cat.Paths.ToList())
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists)
                    {
                        totalCleaned += fi.Length;
                        fi.Delete();
                    }
                }
                catch { }

                current++;
                progress?.Report(new CleanProgress
                {
                    Current = current,
                    Total = totalItems,
                    CurrentPath = path,
                    CleanedBytes = totalCleaned
                });
            }

            foreach (var dir in GetCategoryPaths(cat.Name).Where(d => !IsProtectedDir(d)))
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        foreach (var sub in Directory.EnumerateDirectories(dir, "*", new EnumerationOptions { IgnoreInaccessible = true }))
                        {
                            try { Directory.Delete(sub, true); } catch { }
                        }
                    }
                }
                catch { }
            }

            cat.SizeBytes = 0;
            cat.FileCount = 0;
            cat.Paths.Clear();
        }

        return totalCleaned;
    }

    private static bool IsProtectedDir(string dir)
    {
        var d = dir.TrimEnd('\\').ToLowerInvariant();
        var protectedDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\').ToLowerInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.System).TrimEnd('\\').ToLowerInvariant(),
        };
        return protectedDirs.Any(p => d == p);
    }

    private static List<string> GetCategoryPaths(string name)
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return name switch
        {
            "系统临时文件" => [Path.Combine(win, "Temp")],
            "用户临时文件" => [Path.Combine(user, "AppData", "Local", "Temp")],
            "回收站" => GetRecycleBinPaths(),
            "浏览器缓存" => GetBrowserCachePaths(localAppData, user),
            "缩略图缓存" => [Path.Combine(localAppData, "Microsoft", "Windows", "Explorer")],
            "Windows 更新缓存" => [Path.Combine(win, "SoftwareDistribution", "Download")],
            "日志文件" => GetLogPaths(win, user, localAppData),
            "预读取文件" => [Path.Combine(win, "Prefetch")],
            _ => []
        };
    }

    private static List<string> GetRecycleBinPaths()
    {
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName);
        return drives.Select(d => Path.Combine(d, "$Recycle.Bin")).ToList();
    }

    private static List<string> GetBrowserCachePaths(string localAppData, string user)
    {
        var paths = new List<string>();

        var chrome = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache");
        var chromeCodeCache = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache");
        if (Directory.Exists(chrome)) paths.Add(chrome);
        if (Directory.Exists(chromeCodeCache)) paths.Add(chromeCodeCache);

        var edge = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache");
        var edgeCodeCache = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache");
        if (Directory.Exists(edge)) paths.Add(edge);
        if (Directory.Exists(edgeCodeCache)) paths.Add(edgeCodeCache);

        var firefox = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefox))
        {
            foreach (var profile in Directory.EnumerateDirectories(firefox))
            {
                var cache = Path.Combine(profile, "cache2");
                if (Directory.Exists(cache)) paths.Add(cache);
            }
        }

        return paths;
    }

    private static List<string> GetLogPaths(string win, string user, string localAppData)
    {
        var paths = new List<string>();
        var logDir = Path.Combine(win, "Logs");
        if (Directory.Exists(logDir)) paths.Add(logDir);
        var cbsLog = Path.Combine(win, "Logs", "CBS");
        if (Directory.Exists(cbsLog)) paths.Add(cbsLog);
        var dismLog = Path.Combine(win, "Logs", "DISM");
        if (Directory.Exists(dismLog)) paths.Add(dismLog);
        var appLog = Path.Combine(localAppData, "CrashDumps");
        if (Directory.Exists(appLog)) paths.Add(appLog);
        return paths;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int idx = 0;
        while (size >= 1024 && idx < units.Length - 1) { size /= 1024; idx++; }
        return $"{size:0.##} {units[idx]}";
    }
}