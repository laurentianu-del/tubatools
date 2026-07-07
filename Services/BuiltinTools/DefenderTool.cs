using System.Diagnostics;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public sealed class DefenderTool : IBuiltinTool
{
    public string Id => "defender-control";
    public string Name => "Defender 控制";
    public string Description => "一键关闭/开启 Windows Defender 实时保护，来自 defender-control 开源项目。";
    public string Glyph => "\uE72E";
    public string Category => "安全工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    private const string Repo = "pgkt04/defender-control";
    private const string ToolExeName = "disable-defender.exe";

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var exePath = FindTool();
        if (exePath is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                return;
            }
            catch (Exception ex)
            {
                context.OnProgress?.Invoke($"启动失败：{ex.Message}");
                return;
            }
        }

        var destDir = GetToolDirectory();
        Directory.CreateDirectory(destDir);

        DownloadQueueService.EnqueueWithResolver(
            displayName: "Defender 控制",
            urlResolver: async ct =>
            {
                var release = await GitHubReleaseService.FetchLatestReleaseAsync(Repo, ct);
                if (release is null)
                    throw new InvalidOperationException("无法从 GitHub 获取版本信息，请检查网络连接后重试。");

                GitHubAssetInfo? bestAsset = null;
                foreach (var asset in release.Assets)
                {
                    if (asset.Name.Equals("disable-defender.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        bestAsset = asset;
                        break;
                    }
                }

                if (bestAsset is null)
                    throw new InvalidOperationException($"未找到可下载文件。版本：{release.TagName}");

                var proxyResults = await GitHubReleaseService.TestProxiesAsync(bestAsset.OriginalUrl, 8, ct);
                var bestUrl = GitHubReleaseService.GetBestUrl(proxyResults, bestAsset.OriginalUrl);

                return new ResolvedDownloadUrl(bestUrl, bestAsset.Name, bestAsset.Size);
            },
            destinationPath: destDir,
            postProcessor: new DirectExeProcessor(),
            description: "disable-defender.exe 可能被杀毒软件报毒，属正常现象",
            glyph: Glyph);

        context.OnProgress?.Invoke("已加入下载队列，请在下载中心查看进度。");
    }

    private static string GetToolDirectory()
    {
        var appDir = ToolCatalog.AppDirectory;
        return Path.Combine(appDir, "defender-control");
    }

    private static string? FindTool()
    {
        var destDir = GetToolDirectory();
        var exePath = Path.Combine(destDir, ToolExeName);
        if (File.Exists(exePath)) return exePath;

        var appDir = ToolCatalog.AppDirectory;
        var dir = new DirectoryInfo(appDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "defender-control", ToolExeName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private sealed class DirectExeProcessor : IDownloadPostProcessor
    {
        public string DisplayName => "启动工具";

        public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
            IProgress<string>? statusProgress, CancellationToken ct)
        {
            var exePath = downloadedFilePath;
            if (!exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var files = Directory.GetFiles(destinationPath, "*.exe", SearchOption.TopDirectoryOnly);
                if (files.Length > 0) exePath = files[0];
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch { }

            await Task.CompletedTask;
        }
    }
}
