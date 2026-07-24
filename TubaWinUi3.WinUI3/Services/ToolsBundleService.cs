using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public sealed record ToolsBundleUpdateInfo(
    bool HasUpdate,
    string Version,
    string? GitCodeUrl = null,
    string? GitHubUrl = null,
    long Size = 0);

public static class ToolsBundleService
{
    private const string Owner = "luolangaga";
    private const string Repo = "tubatool";
    private const string GitHubReleaseApi = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private const string GitCodeOwner = "luolangaga";
    private const string GitCodeRepo = "tubatool";
    private const string GitCodeReleaseApiBase = $"https://api.gitcode.com/api/v5/repos/{GitCodeOwner}/{GitCodeRepo}/releases";
    private const string ToolsAssetName = "Tools.zip";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static string ToolsBundleDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubaWinUi3", "Tools");

    static ToolsBundleService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolsBundle");
    }

    public static bool IsToolsBundleReady()
    {
        try
        {
            if (!Directory.Exists(ToolsBundleDir)) return false;
            return Directory.EnumerateFileSystemEntries(ToolsBundleDir).Any();
        }
        catch { return false; }
    }

    public static string GetToolsBundleDir() => ToolsBundleDir;

    public static string? GetCurrentVersion()
    {
        return AppSettings.Get("ToolsBundleVersion");
    }

    public static Version? CurrentAppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is not null ? new Version(v.Major, v.Minor, v.Build) : new Version(1, 0, 0);
        }
    }

    public static async Task<ToolsBundleUpdateInfo?> CheckForToolsUpdateAsync(CancellationToken ct = default)
    {
        var currentVersion = GetCurrentVersion();

        string? gitCodeUrl = null;
        string? githubUrl = null;
        long size = 0;
        string? versionStr = null;

        var gitCodeTask = FetchGitCodeLatestAsync(ct);
        var githubTask = FetchGitHubLatestAsync(ct);

        try
        {
            var gc = await gitCodeTask;
            if (gc is not null)
            {
                gitCodeUrl = gc.Value.Url;
                size = gc.Value.Size;
                versionStr ??= gc.Value.Version;
            }
        }
        catch { }

        try
        {
            var gh = await githubTask;
            if (gh is not null)
            {
                githubUrl = gh.Value.Url;
                size = size > 0 ? size : gh.Value.Size;
                versionStr ??= gh.Value.Version;
            }
        }
        catch { }

        if (versionStr is null) return null;

        if (currentVersion is not null && versionStr == currentVersion)
            return new ToolsBundleUpdateInfo(false, versionStr, gitCodeUrl, githubUrl, size);

        return new ToolsBundleUpdateInfo(true, versionStr, gitCodeUrl, githubUrl, size);
    }

    public static string? PickBestUrl(ToolsBundleUpdateInfo info)
    {
        if (!string.IsNullOrEmpty(info.GitCodeUrl)) return info.GitCodeUrl;
        if (!string.IsNullOrEmpty(info.GitHubUrl)) return info.GitHubUrl;
        return null;
    }

    public static Func<CancellationToken, Task<ResolvedDownloadUrl>> CreateUrlResolver(
        ToolsBundleUpdateInfo info, bool preferGitCode = true)
    {
        return async ct =>
        {
            var url = preferGitCode
                ? (info.GitCodeUrl ?? info.GitHubUrl)
                : (info.GitHubUrl ?? info.GitCodeUrl);

            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException("没有可用的下载链接");

            var fileName = ToolsAssetName;
            return new ResolvedDownloadUrl(url, fileName, info.Size);
        };
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{(double)bytes / (1L << 30):F2} GB";
        if (bytes >= 1L << 20) return $"{(double)bytes / (1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{(double)bytes / (1L << 10):F1} KB";
        return $"{bytes} B";
    }

    public static string FormatSpeed(double mbps)
    {
        if (mbps >= 1000) return $"{mbps / 1000:F2} Gbps";
        if (mbps >= 1) return $"{mbps:F2} Mbps";
        return $"{mbps * 1000:F0} Kbps";
    }

    public static string FormatTime(TimeSpan? time)
    {
        if (time is null || time.Value.TotalSeconds <= 0) return "--";
        var t = time.Value;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    private static async Task<(string Url, long Size, string Version)?> FetchGitCodeLatestAsync(CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolsBundle");

            var url = $"{GitCodeReleaseApiBase}/latest";
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseReleaseJson(json);
        }
        catch { return null; }
    }

    private static async Task<(string Url, long Size, string Version)?> FetchGitHubLatestAsync(CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolsBundle");

            var response = await client.GetAsync(GitHubReleaseApi, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseReleaseJson(json);
        }
        catch { return null; }
    }

    private static (string Url, long Size, string Version)? ParseReleaseJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var versionStr = tagName.TrimStart('v', 'V');

            if (!root.TryGetProperty("assets", out var assetsEl)) return null;

            foreach (var asset in assetsEl.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.Equals(ToolsAssetName, StringComparison.OrdinalIgnoreCase)) continue;

                var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                var assetSize = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;

                if (string.IsNullOrEmpty(downloadUrl)) continue;
                return (downloadUrl, assetSize, versionStr);
            }

            return null;
        }
        catch { return null; }
    }
}
