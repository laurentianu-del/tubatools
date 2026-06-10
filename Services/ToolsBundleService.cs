using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace TubaWinUi3.Services;

public sealed record ToolsBundleUpdateInfo(
    bool HasUpdate,
    string Version,
    string? GitCodeUrl = null,
    string? HubUrl = null,
    string? GitHubUrl = null,
    long Size = 0);

public sealed record ToolsBundleProgress(
    long BytesReceived,
    long TotalBytes,
    double Percentage,
    double SpeedMbps,
    TimeSpan? EstimatedRemaining);

public static class ToolsBundleService
{
    private const string Owner = "luolangaga";
    private const string Repo = "tubatool";
    private const string HubBase = "https://hub.tubawinui3.cn";
    private const string HubReleaseApi = $"{HubBase}/api/repos/{Owner}/{Repo}/releases/latest";
    private const string GitHubReleaseApi = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private const string GitCodeOwner = "gcw_uDDNaqJw";
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

    public static string? GetCurrentVersion()
    {
        return AppSettings.Get("ToolsBundleVersion");
    }

    public static Version? CurrentAppVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is not null ? new Version(v.Major, v.Minor, v.Build) : new Version(1, 0, 0);
        }
    }

    public static async Task<ToolsBundleUpdateInfo?> CheckForToolsUpdateAsync(CancellationToken ct = default)
    {
        var currentVersion = GetCurrentVersion();

        string? gitCodeUrl = null;
        string? hubUrl = null;
        string? githubUrl = null;
        long size = 0;
        string? versionStr = null;

        var gitCodeTask = FetchGitCodeLatestAsync(ct);
        var hubTask = FetchHubLatestAsync(ct);
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
            var hub = await hubTask;
            if (hub is not null)
            {
                hubUrl = hub.Value.Url;
                size = size > 0 ? size : hub.Value.Size;
                versionStr ??= hub.Value.Version;
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
            return new ToolsBundleUpdateInfo(false, versionStr, gitCodeUrl, hubUrl, githubUrl, size);

        return new ToolsBundleUpdateInfo(true, versionStr, gitCodeUrl, hubUrl, githubUrl, size);
    }

    public static async Task<bool> DownloadAndExtractAsync(
        ToolsBundleUpdateInfo info,
        IProgress<ToolsBundleProgress>? progress = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"TubaWinUi3_Tools_{Guid.NewGuid():N}");
        var tempZipPath = Path.Combine(tempDir, ToolsAssetName);

        try
        {
            Directory.CreateDirectory(tempDir);

            var downloadUrl = PickBestUrl(info);
            if (string.IsNullOrEmpty(downloadUrl))
                throw new InvalidOperationException("没有可用的下载链接");

            await DownloadFileAsync(downloadUrl, tempZipPath, info.Size, progress, ct);

            if (!File.Exists(tempZipPath))
                throw new InvalidOperationException("下载文件不存在");

            progress?.Report(new ToolsBundleProgress(0, 0, 0, 0, null));

            await ExtractAndReplaceAsync(tempZipPath, ct);

            AppSettings.Set("ToolsBundleVersion", info.Version);
            ToolCatalog.InvalidateTagsCache();

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    private static string? PickBestUrl(ToolsBundleUpdateInfo info)
    {
        if (!string.IsNullOrEmpty(info.GitCodeUrl)) return info.GitCodeUrl;
        if (!string.IsNullOrEmpty(info.HubUrl)) return info.HubUrl;
        if (!string.IsNullOrEmpty(info.GitHubUrl)) return info.GitHubUrl;
        return null;
    }

    private static async Task DownloadFileAsync(
        string url, string destPath, long knownSize,
        IProgress<ToolsBundleProgress>? progress, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
        var sw = Stopwatch.StartNew();

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? knownSize;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fs = File.Create(destPath);

        var buffer = new byte[81920];
        long bytesRead = 0;
        var lastReport = sw.Elapsed;
        long lastBytes = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;

            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesRead += read;

            var now = sw.Elapsed;
            if (now - lastReport > TimeSpan.FromMilliseconds(300))
            {
                var chunkBytes = bytesRead - lastBytes;
                var chunkTime = (now - lastReport).TotalSeconds;
                var speedMbps = chunkBytes / Math.Max(chunkTime, 0.001) * 8 / 1_000_000;
                var percentage = totalBytes > 0 ? (double)bytesRead / totalBytes * 100 : 0;
                var remaining = totalBytes > 0 && speedMbps > 0
                    ? TimeSpan.FromSeconds((totalBytes - bytesRead) / Math.Max(speedMbps * 1_000_000 / 8, 1))
                    : (TimeSpan?)null;

                progress?.Report(new ToolsBundleProgress(bytesRead, totalBytes, percentage, speedMbps, remaining));

                lastReport = now;
                lastBytes = bytesRead;
            }
        }

        progress?.Report(new ToolsBundleProgress(bytesRead, totalBytes, 100, 0, TimeSpan.Zero));
    }

    private static async Task ExtractAndReplaceAsync(string zipPath, CancellationToken ct)
    {
        var extractDir = Path.Combine(Path.GetTempPath(), $"TubaWinUi3_Extract_{Guid.NewGuid():N}");

        try
        {
            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(zipPath, extractDir, true);
            }, ct);

            var toolsDir = ToolsBundleDir;

            if (Directory.Exists(toolsDir))
            {
                var backupDir = toolsDir + "_bak";
                if (Directory.Exists(backupDir))
                {
                    try { Directory.Delete(backupDir, true); } catch { }
                }
                try { Directory.Move(toolsDir, backupDir); } catch { }
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(toolsDir)!);
                Directory.Move(extractDir, toolsDir);
            }
            catch
            {
                var backupDir = toolsDir + "_bak";
                if (Directory.Exists(backupDir))
                {
                    try { Directory.Move(backupDir, toolsDir); } catch { }
                }
                throw;
            }

            var oldBackup = toolsDir + "_bak";
            if (Directory.Exists(oldBackup))
            {
                try { Directory.Delete(oldBackup, true); } catch { }
            }
        }
        catch
        {
            if (Directory.Exists(extractDir))
            {
                try { Directory.Delete(extractDir, true); } catch { }
            }
            throw;
        }
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

    private static async Task<(string Url, long Size, string Version)?> FetchHubLatestAsync(CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolsBundle");

            var response = await client.GetAsync(HubReleaseApi, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseGitHubJson(json, true);
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
            return ParseGitHubJson(json, false);
        }
        catch { return null; }
    }

    private static (string Url, long Size, string Version)? ParseGitHubJson(string json, bool isHub)
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

                var originalUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                var url = isHub
                    ? originalUrl.Replace("https://github.com", HubBase, StringComparison.OrdinalIgnoreCase)
                    : originalUrl;
                var assetSize = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;

                if (string.IsNullOrEmpty(url)) continue;
                return (url, assetSize, versionStr);
            }

            return null;
        }
        catch { return null; }
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
}
