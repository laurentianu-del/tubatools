using System.Diagnostics;
using Microsoft.Win32;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public sealed class ContextMenuMgrTool : IBuiltinTool
{
    public string Id => "context-menu-mgr";
    public string Name => "右键菜单管理";
    public string Description => "管理 Windows 右键菜单项，支持添加/删除/编辑，来自 ContextMenuMgr 开源项目。";
    public string Glyph => "\uE74C";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    private const string Repo = "PLFJY/ContextMenuMgr";
    private const string ProjectUrl = $"https://github.com/{Repo}";

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        if (IsInstalled())
        {
            var exe = FindInstalledExe();
            if (exe is not null)
            {
                try { Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true }); return; }
                catch { }
            }
        }

        var arch = GitHubReleaseService.GetCurrentArch();
        var destDir = Path.Combine(Path.GetTempPath(), "TubaWinUi3_ContextMenuMgr");

        DownloadQueueService.EnqueueWithResolver(
            displayName: "右键菜单管理",
            urlResolver: async ct =>
            {
                var release = await GitHubReleaseService.FetchLatestReleaseAsync(Repo, ct);
                if (release is null)
                    throw new InvalidOperationException("无法从 GitHub 获取版本信息，请检查网络连接后重试。");

                var asset = GitHubReleaseService.FindBestAsset(release.Assets, arch, AssetMatchStrategy.ContextMenuMgr);
                if (asset is null)
                    throw new InvalidOperationException($"当前架构 {arch} 没有匹配的下载文件。版本：{release.TagName}");

                var proxyResults = await GitHubReleaseService.TestProxiesAsync(asset.OriginalUrl, 8, ct);
                var bestUrl = GitHubReleaseService.GetBestUrl(proxyResults, asset.OriginalUrl);

                return new ResolvedDownloadUrl(bestUrl, asset.Name, asset.Size);
            },
            destinationPath: destDir,
            postProcessor: new InstallerLaunchProcessor(),
            description: "此软件运行后会在后台占用约 30MB 内存（托盘驻留进程）",
            glyph: Glyph);
    }

    private static bool IsInstalled() => FindInstalledExe() is not null;

    private static string? FindInstalledExe()
    {
        try
        {
            var keys = new[]
            {
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
            };
            foreach (var key in keys)
            {
                if (key is null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(sub);
                    var name = subKey?.GetValue("DisplayName") as string;
                    if (name is not null && IsContextMenuMgr(name))
                    {
                        var loc = subKey?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(loc))
                        {
                            loc = loc.TrimEnd('\\');
                            if (Directory.Exists(loc))
                            {
                                var exe = FindMainExe(loc);
                                if (exe is not null) return exe;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        try
        {
            var programDirs = new[] { @"C:\Program Files", @"C:\Program Files (x86)" };
            foreach (var d in programDirs)
            {
                if (!Directory.Exists(d)) continue;
                foreach (var sub in Directory.GetDirectories(d))
                {
                    if (IsContextMenuMgr(Path.GetFileName(sub)))
                    {
                        var exe = FindMainExe(sub);
                        if (exe is not null) return exe;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static bool IsContextMenuMgr(string name) =>
        name.Contains("ContextMenuMgr", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Context Menu Manager", StringComparison.OrdinalIgnoreCase);

    private static string? FindMainExe(string dir)
    {
        var candidates = new[] { "ContextMenuManagerPlus.exe", "ContextMenuMgrPlus.exe" };
        foreach (var c in candidates)
        {
            var p = Path.Combine(dir, c);
            if (File.Exists(p)) return p;
        }
        foreach (var f in Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(f);
            if (name.Contains("ContextMenuManager", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ContextMenuMgr", StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return null;
    }
}
