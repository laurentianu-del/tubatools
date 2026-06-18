using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class DefenderTool : IBuiltinTool
{
    public string Id => "defender-control";
    public string Name => "Defender 控制";
    public string Description => "使用 dControl 一键关闭/开启 Windows Defender 实时保护。（需先下载工具）";
    public string Glyph => "\uE72E";
    public string Category => "安全工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    private const string GitCodeRawBase = "https://raw.gitcode.com/gcw_uDDNaqJw/tubatool/raw/master";
    private const string GitHubRawBase = "https://raw.githubusercontent.com/luolangaga/tubatool/master";
    private const string ToolFileName = "dControl.exe";

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var exePath = FindDControl();
        if (exePath is null)
        {
            await OfferDownloadAsync(context);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Exception ex)
        {
            context.OnProgress?.Invoke($"启动失败：{ex.Message}");
        }
    }

    private async Task OfferDownloadAsync(BuiltinToolContext context)
    {
        if (context.XamlRoot is null) return;

        var dialog = context.CreateDialog("需要下载 dControl", "取消");
        dialog.Content = "dControl.exe 未找到。此工具可能被杀毒软件报毒，需要单独下载。\n\n是否从镜像站下载？";
        dialog.PrimaryButtonText = "下载";

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await DownloadAndRunAsync(context);
        }
    }

    private async Task DownloadAndRunAsync(BuiltinToolContext context)
    {
        if (context.XamlRoot is null) return;

        var destDir = GetToolDirectory();
        Directory.CreateDirectory(destDir);

        var downloadUrls = new[]
        {
            $"{GitCodeRawBase}/remotedefender/dControl.exe",
            $"{GitHubRawBase}/remotedefender/dControl.exe"
        };

        string? downloadedPath = null;
        Exception? lastError = null;

        foreach (var url in downloadUrls)
        {
            var isProxy = url.StartsWith(GitCodeRawBase, StringComparison.OrdinalIgnoreCase);
            try
            {
                var dialog = new ToolDownloadDialog(
                    "dControl",
                    "Windows Defender 控制工具",
                    url,
                    null,
                    destDir);

                dialog.XamlRoot = context.XamlRoot;
                await dialog.ShowAsync();

                if (dialog.DownloadSucceeded && dialog.DownloadedFilePath is not null)
                {
                    downloadedPath = dialog.DownloadedFilePath;
                    break;
                }

                if (isProxy)
                {
                    lastError = new Exception("镜像站下载失败，正在回退到直连...");
                    continue;
                }

                lastError ??= new Exception("下载失败");
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (isProxy) continue;
            }
        }

        if (downloadedPath is null)
        {
            if (lastError is not null && context.XamlRoot is not null)
            {
                var errDialog = context.CreateDialog($"下载失败：{lastError.Message}", "确定");
                await errDialog.ShowAsync();
            }
            return;
        }

        var exePath = downloadedPath;
        if (!exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var files = Directory.GetFiles(destDir, "*.exe", SearchOption.TopDirectoryOnly);
            if (files.Length > 0) exePath = files[0];
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Exception ex)
        {
            context.OnProgress?.Invoke($"下载成功但启动失败：{ex.Message}");
        }
    }

    private static string GetToolDirectory()
    {
        var appDir = ToolCatalog.AppDirectory;
        return Path.Combine(appDir, "remotedefender");
    }

    private static string? FindDControl()
    {
        var destDir = GetToolDirectory();
        var exePath = Path.Combine(destDir, ToolFileName);
        if (File.Exists(exePath)) return exePath;

        var appDir = ToolCatalog.AppDirectory;
        var candidates = new[]
        {
            Path.Combine(appDir, "remotedefender", ToolFileName),
            Path.Combine(appDir, "..", "remotedefender", ToolFileName),
        };

        foreach (var p in candidates)
        {
            var full = Path.GetFullPath(p);
            if (File.Exists(full)) return full;
        }

        var dir = new DirectoryInfo(appDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "remotedefender", ToolFileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
