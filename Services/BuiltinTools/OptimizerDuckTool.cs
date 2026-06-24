using System.Diagnostics;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

public sealed class OptimizerDuckTool : IBuiltinTool
{
    public string Id => "optimizer-duck";
    public string Name => "OptimizerDuck 优化鸭";
    public string Description => "开源的 Windows 系统优化工具，支持系统清理、性能优化、隐私保护等功能。";
    public string Glyph => "\uE945";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    private const string Repo = "itsfatduck/optimizerDuck";
    private const string ProjectUrl = "https://github.com/itsfatduck/optimizerDuck";

    private static string PortableDir => Path.Combine(ToolCatalog.ToolsRoot, "系统工具", "优化鸭");

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var exe = FindInstalledExe();
        if (exe is not null)
        {
            try { Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true }); return; }
            catch { }
        }

        await GitHubReleaseService.ShowDownloadFlowAsync(
            context,
            toolName: "OptimizerDuck",
            description: "一款开源的 Windows 系统优化工具，提供系统清理、性能优化、隐私保护、启动项管理等功能，" +
                         "界面简洁易用，适合日常系统维护。下载后可直接从工具箱启动。",
            projectUrl: ProjectUrl,
            repo: Repo,
            tag: null,
            strategy: AssetMatchStrategy.OptimizerDuck,
            warningText: "当前仅提供 x64 版本，ARM64 设备可能需要通过兼容层运行",
            sizeHint: null,
            portableDir: PortableDir);
    }

    private static string? FindInstalledExe()
    {
        try
        {
            if (Directory.Exists(PortableDir))
            {
                var exe = FindMainExe(PortableDir);
                if (exe is not null) return exe;
            }
        }
        catch { }

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
                    if (name is not null && IsOptimizerDuck(name))
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
                    if (IsOptimizerDuck(Path.GetFileName(sub)))
                    {
                        var exe = FindMainExe(sub);
                        if (exe is not null) return exe;
                    }
                }
            }
        }
        catch { }

        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localDirs = new[] { Path.Combine(localAppData, "Programs") };
            foreach (var d in localDirs)
            {
                if (!Directory.Exists(d)) continue;
                foreach (var sub in Directory.GetDirectories(d))
                {
                    if (IsOptimizerDuck(Path.GetFileName(sub)))
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

    private static bool IsOptimizerDuck(string name) =>
        name.Contains("optimizerDuck", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("OptimizerDuck", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("优化鸭", StringComparison.OrdinalIgnoreCase);

    private static string? FindMainExe(string dir)
    {
        var candidates = new[] { "optimizerDuck.exe", "OptimizerDuck.exe" };
        foreach (var c in candidates)
        {
            var p = Path.Combine(dir, c);
            if (File.Exists(p)) return p;
        }
        foreach (var f in Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(f);
            if (name.Contains("optimizerDuck", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("OptimizerDuck", StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return null;
    }
}
