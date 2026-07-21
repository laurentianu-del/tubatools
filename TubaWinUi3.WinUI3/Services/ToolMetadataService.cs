using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services;

public sealed record ToolMetadata(
    string? Description,
    string? Publisher,
    string? Version,
    string? DatabaseSource,
    string? DownloadUrl,
    string? DownloadFilter,
    string? WingetId,
    string? LaunchTarget,
    string? TutorialUrl,
    IReadOnlyList<string>? Tags,
    int? ToolVersion);

public sealed record JsonArchVariantResult(string? File, string? Dir, string? Arch);

public sealed record RemoteToolVersion(string Match, int Version, string? DownloadUrl);

public static class ToolMetadataService
{
    private static IReadOnlyList<JsonToolMetadata>? _metadata;

    public static void InvalidateCache()
    {
        _metadata = null;
    }

    public static async Task RemoveMetadataAsync(string toolPath)
    {
        var dirName = Path.GetFileName(Path.GetDirectoryName(toolPath));
        if (string.IsNullOrWhiteSpace(dirName)) return;

        var metadataRoot = GetWritableMetadataDir();
        var metadataPath = Path.Combine(metadataRoot, "tools.json");
        if (!File.Exists(metadataPath)) return;

        JsonObject root;
        JsonArray tools;

        await using (var readStream = File.OpenRead(metadataPath))
        {
            root = await JsonNode.ParseAsync(readStream) as JsonObject ?? new JsonObject();
        }

        tools = root["tools"] as JsonArray ?? [];
        var existing = tools
            .OfType<JsonObject>()
            .FirstOrDefault(item =>
                string.Equals(item["match"]?.GetValue<string>(), dirName, StringComparison.CurrentCultureIgnoreCase));

        if (existing is null) return;

        tools.Remove(existing);
        root["tools"] = tools;

        await using var writeStream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(writeStream, root, new JsonSerializerOptions { WriteIndented = true });
        _metadata = null;
    }

    public static bool HasDownloadUrl(string category, string toolDir)
    {
        var dirName = Path.GetFileName(toolDir);
        var metadata = LoadMetadata();

        return metadata.Any(item =>
            !string.IsNullOrWhiteSpace(item.Match) &&
            (!string.IsNullOrWhiteSpace(item.DownloadUrl) || !string.IsNullOrWhiteSpace(item.WingetId)) &&
            dirName.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase));
    }

    public static ToolMetadata GetMetadata(string category, string toolPath)
    {
        var jsonMetadata = FindJsonMetadata(toolPath);

        string? description = jsonMetadata?.Description;
        string? publisher = jsonMetadata?.Publisher;
        string? version = null;

        if (File.Exists(toolPath))
        {
            try
            {
                var ext = Path.GetExtension(toolPath);
                if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(toolPath);
                    description ??= FirstUseful(versionInfo.FileDescription, versionInfo.ProductName);
                    publisher ??= FirstUseful(versionInfo.CompanyName, versionInfo.LegalCopyright);
                    version = FirstUseful(versionInfo.ProductVersion, versionInfo.FileVersion);
                }
            }
            catch { }
        }

        if (description is null)
        {
            description = ReadFolderDescription(toolPath);
        }

        return new ToolMetadata(
            description,
            publisher,
            version,
            jsonMetadata is null ? null : "JSON",
            jsonMetadata?.DownloadUrl,
            jsonMetadata?.DownloadFilter,
            jsonMetadata?.WingetId,
            jsonMetadata?.LaunchTarget,
            jsonMetadata?.TutorialUrl,
            jsonMetadata?.Tags,
            jsonMetadata?.ToolVersion);
    }

    public static IReadOnlyList<JsonArchVariantResult> GetArchVariants(string toolPath, string? toolDir = null)
    {
        var jsonMetadata = FindJsonMetadata(toolPath);
        if (jsonMetadata is null && toolDir is not null)
            jsonMetadata = FindJsonMetadataByDir(toolDir);

        if (jsonMetadata?.ArchVariants is null || jsonMetadata.ArchVariants.Count == 0)
            return [];

        return jsonMetadata.ArchVariants
            .Select(v => new JsonArchVariantResult(v.File, v.Dir, v.Arch))
            .ToList();
    }

    private static JsonToolMetadata? FindJsonMetadata(string toolPath)
    {
        var metadata = LoadMetadata();
        var fileName = Path.GetFileNameWithoutExtension(toolPath);
        var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, toolPath);
        var dirName = Path.GetFileName(Path.GetDirectoryName(toolPath));

        return metadata
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Match) &&
                (fileName.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
                 relativePath.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
                 MatchesFlexible(dirName, item.Match)))
            .OrderByDescending(item => item.Match!.Length)
            .FirstOrDefault();
    }

    public static string? GetLaunchTarget(string toolDir)
    {
        var jsonMetadata = FindJsonMetadataByDir(toolDir);
        return jsonMetadata?.LaunchTarget;
    }

    public static int? GetToolVersion(string toolPath)
    {
        var jsonMetadata = FindJsonMetadata(toolPath);
        return jsonMetadata?.ToolVersion;
    }

    public static int? GetToolVersionByDir(string toolDir)
    {
        var jsonMetadata = FindJsonMetadataByDir(toolDir);
        return jsonMetadata?.ToolVersion;
    }

    public static string? GetDownloadUrlByDir(string toolDir)
    {
        var jsonMetadata = FindJsonMetadataByDir(toolDir);
        return jsonMetadata?.DownloadUrl;
    }

    public static async Task<IReadOnlyList<RemoteToolVersion>?> FetchRemoteToolsJsonAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await TryFetchGitCodeToolsJsonAsync(ct);
            if (result is not null) return result;
        }
        catch { }

        try
        {
            var result = await TryFetchGitHubToolsJsonAsync(ct);
            if (result is not null) return result;
        }
        catch { }

        return null;
    }

    private static async Task<IReadOnlyList<RemoteToolVersion>?> TryFetchGitCodeToolsJsonAsync(CancellationToken ct)
    {
        const string owner = "gcw_uDDNaqJw";
        const string repo = "tubatool";
        var url = $"https://api.gitcode.com/api/v5/repos/{owner}/{repo}/contents/Metadata/tools.json?ref=master";

        using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(15));
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolUpdate");

        var json = await client.GetStringAsync(url, ct);
        var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.TryGetProperty("content", out var contentEl)
            ? contentEl.GetString() ?? "" : "";
        var encoding = doc.RootElement.TryGetProperty("encoding", out var encEl)
            ? encEl.GetString() ?? "" : "";

        string toolsJsonText;
        if (encoding == "base64")
            toolsJsonText = Encoding.UTF8.GetString(Convert.FromBase64String(content));
        else
            toolsJsonText = content;

        return ParseRemoteToolsJson(toolsJsonText);
    }

    private static async Task<IReadOnlyList<RemoteToolVersion>?> TryFetchGitHubToolsJsonAsync(CancellationToken ct)
    {
        const string owner = "luolangaga";
        const string repo = "tubatool";
        var url = $"https://api.github.com/repos/{owner}/{repo}/contents/Metadata/tools.json";

        using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(15));
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolUpdate");

        var json = await client.GetStringAsync(url, ct);
        var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.TryGetProperty("content", out var contentEl)
            ? contentEl.GetString() ?? "" : "";
        var encoding = doc.RootElement.TryGetProperty("encoding", out var encEl)
            ? encEl.GetString() ?? "" : "";

        string toolsJsonText;
        if (encoding == "base64")
            toolsJsonText = Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "", StringComparison.Ordinal)));
        else
            toolsJsonText = content;

        return ParseRemoteToolsJson(toolsJsonText);
    }

    private static IReadOnlyList<RemoteToolVersion>? ParseRemoteToolsJson(string jsonText)
    {
        try
        {
            var database = JsonSerializer.Deserialize<JsonToolDatabase>(jsonText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (database?.Tools is null) return null;

            return database.Tools
                .Where(t => !string.IsNullOrWhiteSpace(t.Match) && t.ToolVersion.HasValue)
                .Select(t => new RemoteToolVersion(t.Match!, t.ToolVersion!.Value, t.DownloadUrl))
                .ToList();
        }
        catch { return null; }
    }

    private static JsonToolMetadata? FindJsonMetadataByDir(string toolDir)
    {
        var metadata = LoadMetadata();
        var dirName = Path.GetFileName(toolDir);
        var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, toolDir);

        return metadata
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Match) &&
                (relativePath.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
                 MatchesFlexible(dirName, item.Match)))
            .OrderByDescending(item => item.Match!.Length)
            .FirstOrDefault();
    }

    private static bool MatchesFlexible(string? source, string match)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (source.Contains(match, StringComparison.CurrentCultureIgnoreCase))
            return true;

        var normalizedSource = source.Replace(" ", "", StringComparison.Ordinal)
                                      .Replace("-", "", StringComparison.Ordinal)
                                      .Replace("_", "", StringComparison.Ordinal);
        var normalizedMatch = match.Replace(" ", "", StringComparison.Ordinal)
                                   .Replace("-", "", StringComparison.Ordinal)
                                   .Replace("_", "", StringComparison.Ordinal);

        return normalizedSource.Contains(normalizedMatch, StringComparison.CurrentCultureIgnoreCase);
    }

    private static IReadOnlyList<JsonToolMetadata> LoadMetadata()
    {
        if (_metadata is not null)
        {
            return _metadata;
        }

        var path = Path.Combine(GetWritableMetadataDir(), "tools.json");
        if (!File.Exists(path))
        {
            _metadata = [];
            return _metadata;
        }

        using var stream = File.OpenRead(path);
        var database = JsonSerializer.Deserialize<JsonToolDatabase>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        _metadata = database?.Tools ?? [];
        return _metadata;
    }

    private static string? ReadFolderDescription(string toolPath)
    {
        var directory = Path.GetDirectoryName(toolPath);
        if (directory is null)
        {
            return null;
        }

        var textFile = Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileName(path).Contains("readme", StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetFileName(path).Contains("说明", StringComparison.CurrentCultureIgnoreCase) ||
                                    Path.GetFileName(path).Contains("What's New", StringComparison.OrdinalIgnoreCase));
        if (textFile is null)
        {
            return null;
        }

        try
        {
            var text = File.ReadLines(textFile).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
            return text is { Length: > 160 } ? text[..160] : text;
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstUseful(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string FindRoot(string folderName)
    {
        var appDir = ToolCatalog.AppDirectory;
        var outputRoot = Path.Combine(appDir, folderName);
        if (Directory.Exists(outputRoot))
        {
            return outputRoot;
        }

        var directory = new DirectoryInfo(appDir);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, folderName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return outputRoot;
    }

    public static string GetWritableMetadataDir()
    {
        if (!RuntimeHelper.IsMsixPackaged)
            return FindRoot("Metadata");

        var writableDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubaWinUi3", "Metadata");

        if (!Directory.Exists(writableDir))
        {
            var installDir = FindRoot("Metadata");
            if (Directory.Exists(installDir))
            {
                Directory.CreateDirectory(writableDir);
                foreach (var file in Directory.EnumerateFiles(installDir))
                {
                    try
                    {
                        var dest = Path.Combine(writableDir, Path.GetFileName(file));
                        if (!File.Exists(dest))
                            File.Copy(file, dest, false);
                    }
                    catch { }
                }
            }
            else
            {
                Directory.CreateDirectory(writableDir);
            }
        }

        return writableDir;
    }

    private sealed class JsonToolDatabase
    {
        public List<JsonToolMetadata> Tools { get; set; } = [];
    }

    private sealed class JsonToolMetadata
    {
        public string? Match { get; set; }
        public string? Description { get; set; }
        public string? Publisher { get; set; }
        public string? DownloadUrl { get; set; }
        public string? DownloadFilter { get; set; }
        public string? WingetId { get; set; }
        public string? LaunchTarget { get; set; }
        public string? TutorialUrl { get; set; }
        public List<string>? Tags { get; set; }
        public List<JsonArchVariant>? ArchVariants { get; set; }
        public int? ToolVersion { get; set; }
    }

    private sealed class JsonArchVariant
    {
        public string? File { get; set; }

        public string? Dir { get; set; }

        public string? Arch { get; set; }
    }
}
