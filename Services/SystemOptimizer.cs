using Microsoft.Win32;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class SystemOptimizer
{
    public static List<VisualPreset> GetVisualPresets()
    {
        return
        [
            new VisualPreset
            {
                Name = "极致性能",
                Description = "关闭所有动画和透明，最大化系统性能",
                Glyph = "\uE945",
                OptimizeItemStates = new Dictionary<string, bool>
                {
                    ["visual-animation"] = false,
                    ["visual-transparency"] = false,
                    ["visual-shadow"] = false,
                    ["visual-smooth-scroll"] = false,
                    ["visual-desktop-shadow"] = false,
                    ["visual-taskbar-center"] = false,
                    ["visual-taskbar-small"] = false,
                }
            },
            new VisualPreset
            {
                Name = "平衡",
                Description = "保留核心视觉效果，关闭多余动画",
                Glyph = "\uE946",
                OptimizeItemStates = new Dictionary<string, bool>
                {
                    ["visual-animation"] = true,
                    ["visual-transparency"] = true,
                    ["visual-shadow"] = false,
                    ["visual-smooth-scroll"] = true,
                    ["visual-desktop-shadow"] = true,
                    ["visual-taskbar-center"] = true,
                    ["visual-taskbar-small"] = true,
                }
            },
            new VisualPreset
            {
                Name = "极致美观",
                Description = "全部视觉效果全开，最佳视觉体验",
                Glyph = "\uE771",
                OptimizeItemStates = new Dictionary<string, bool>
                {
                    ["visual-animation"] = true,
                    ["visual-transparency"] = true,
                    ["visual-shadow"] = true,
                    ["visual-smooth-scroll"] = true,
                    ["visual-desktop-shadow"] = true,
                    ["visual-taskbar-center"] = true,
                    ["visual-taskbar-small"] = true,
                }
            }
        ];
    }

    public static List<PcSetupAction> GetVisualOptimizeActions()
    {
        return
        [
            new RegistryAction
            {
                Id = "visual-animation", Name = "窗口动画效果", Description = "启用/禁用窗口打开/关闭/最小化动画",
                Glyph = "\uE7B3", Group = "视觉效果", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                ValueName = "VisualFXSetting", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "visual-transparency", Name = "透明效果(Mica/Acrylic)", Description = "启用/禁用系统透明效果",
                Glyph = "\uE7B3", Group = "视觉效果", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                ValueName = "EnableTransparency", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "visual-shadow", Name = "窗口阴影效果", Description = "启用/禁用窗口投影阴影",
                Glyph = "\uE7B3", Group = "视觉效果", IsSelected = false, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "ListviewShadow", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "visual-smooth-scroll", Name = "平滑滚动效果", Description = "启用/禁用列表平滑滚动",
                Glyph = "\uE7B3", Group = "视觉效果", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Control Panel\Desktop",
                ValueName = "SmoothScroll", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "visual-desktop-shadow", Name = "桌面图标阴影", Description = "启用/禁用桌面图标文字阴影",
                Glyph = "\uE7B3", Group = "视觉效果", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "ListviewShadow", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "visual-taskbar-center", Name = "任务栏居中对齐", Description = "将任务栏图标设为居中对齐",
                Glyph = "\uE77B", Group = "视觉效果", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "TaskbarAl", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "visual-taskbar-small", Name = "任务栏小图标", Description = "使用小尺寸任务栏图标",
                Glyph = "\uE77B", Group = "视觉效果", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "TaskbarSi", Value = 0, ValueKind = RegistryValueKind.DWord
            },
        ];
    }

    public static List<PcSetupAction> GetFileExplorerActions()
    {
        return
        [
            new RegistryAction
            {
                Id = "file-show-ext", Name = "显示文件扩展名", Description = "在资源管理器中显示文件扩展名",
                Glyph = "\uE8B7", Group = "文件管理", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "HideFileExt", Value = 0, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "file-show-hidden", Name = "显示隐藏文件", Description = "在资源管理器中显示隐藏的文件和文件夹",
                Glyph = "\uE8B7", Group = "文件管理", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "Hidden", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "file-nav-pane-expand", Name = "导航窗格展开到文件夹", Description = "在资源管理器导航窗格中自动展开当前文件夹",
                Glyph = "\uE8B7", Group = "文件管理", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "NavPaneExpandToCurrentFolder", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "file-show-fullpath", Name = "标题栏显示完整路径", Description = "在资源管理器标题栏显示完整文件路径",
                Glyph = "\uE8B7", Group = "文件管理", IsSelected = false, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState",
                ValueName = "FullPath", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "file-disable-ads", Name = "关闭资源管理器广告", Description = "关闭资源管理器中的同步提供程序通知和广告",
                Glyph = "\uE8B7", Group = "文件管理", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "ShowSyncProviderNotifications", Value = 0, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "file-quick-access", Name = "打开资源管理器到此电脑", Description = "将资源管理器默认打开位置改为此电脑而非快速访问",
                Glyph = "\uE8B7", Group = "文件管理", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "LaunchTo", Value = 1, ValueKind = RegistryValueKind.DWord
            },
        ];
    }

    public static List<PcSetupAction> GetSystemTuneActions()
    {
        return
        [
            new PowerShellAction
            {
                Id = "tune-highperf", Name = "电源计划→高性能", Description = "切换电源计划为高性能模式",
                Glyph = "\uE945", Group = "系统调优", IsSelected = false, IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"
            },
            new PowerShellAction
            {
                Id = "tune-no-hibernate", Name = "关闭系统休眠", Description = "禁用休眠功能并释放hiberfil.sys占用的磁盘空间",
                Glyph = "\uE945", Group = "系统调优", IsSelected = false, IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "powercfg -h off"
            },
            new RegistryAction
            {
                Id = "tune-no-restore", Name = "关闭系统还原", Description = "禁用系统还原功能",
                Glyph = "\uE945", Group = "系统调优", IsSelected = false, IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore",
                ValueName = "DisableSR", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new PowerShellAction
            {
                Id = "tune-disable-startup", Name = "禁用多余启动项", Description = "禁用非系统必要的启动项(仅禁用第三方启动项)",
                Glyph = "\uE945", Group = "系统调优", IsSelected = false, IsDangerous = true, RequiresAdmin = true,
                UseRunAs = true,
                Script = "$startupItems = Get-ItemProperty 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' -ErrorAction SilentlyContinue\n$systemKeywords = @('SecurityHealth','WindowsDefender','OneDrive','Shell','TaskManager')\nforeach ($prop in $startupItems.PSObject.Properties) {\n    if ($prop.Name -match '^(PSPath|PSParentPath|PSChildName|PSDrive|PSProvider)') continue\n    $isSystem = $false\n    foreach ($kw in $systemKeywords) { if ($prop.Value -match $kw) { $isSystem = $true; break } }\n    if (-not $isSystem) {\n        Remove-ItemProperty 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' -Name $prop.Name -ErrorAction SilentlyContinue\n        Write-Host \"  禁用启动项: $($prop.Name)\"\n    }\n}"
            },
            new RegistryAction
            {
                Id = "tune-disable-cortana", Name = "禁用Cortana搜索", Description = "禁用任务栏Cortana搜索框",
                Glyph = "\uE945", Group = "系统调优", IsSelected = false, IsDangerous = false, RequiresAdmin = true,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Search",
                ValueName = "SearchboxTaskbarMode", Value = 0, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "tune-disable-news", Name = "关闭小组件/新闻", Description = "禁用任务栏小组件和新闻资讯",
                Glyph = "\uE945", Group = "系统调优", IsSelected = false, IsDangerous = false, RequiresAdmin = true,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "TaskbarDa", Value = 0, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "tune-disable-chat", Name = "关闭任务栏聊天", Description = "禁用任务栏Teams聊天图标",
                Glyph = "\uE945", Group = "系统调优", IsSelected = false, IsDangerous = false, RequiresAdmin = true,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "TaskbarMn", Value = 0, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "tune-disable-clipboard", Name = "关闭剪贴板历史", Description = "禁用Windows剪贴板历史记录功能",
                Glyph = "\uE945", Group = "系统调优", IsSelected = false, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Clipboard",
                ValueName = "EnableClipboardHistory", Value = 0, ValueKind = RegistryValueKind.DWord
            },
            new PowerShellAction
            {
                Id = "tune-cleanup-temp", Name = "清理临时文件", Description = "清理Windows临时文件夹和用户临时文件夹",
                Glyph = "\uE945", Group = "系统调优", IsSelected = false, IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "Remove-Item \"$env:TEMP\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nRemove-Item \"$env:LOCALAPPDATA\\Temp\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nRemove-Item \"C:\\Windows\\Temp\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nWrite-Host \"  临时文件清理完成\""
            },
            new RegistryAction
            {
                Id = "tune-superfetch", Name = "禁用SysMain(Superfetch)", Description = "禁用SysMain服务以减少磁盘占用(仅HDD建议，SSD不建议)",
                Glyph = "\uE945", Group = "系统调优", IsSelected = false, IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SYSTEM\CurrentControlSet\Services\SysMain",
                ValueName = "Start", Value = 4, ValueKind = RegistryValueKind.DWord
            },
        ];
    }

    public static List<PcSetupAction> GetSecurityActions()
    {
        return
        [
            new PowerShellAction
            {
                Id = "sec-no-defender", Name = "关闭 Windows Defender 实时保护", Description = "⚠ 高危：关闭后系统将失去实时病毒防护",
                Glyph = "\uEA18", Group = "安全设置", IsSelected = false, IsDangerous = true, RequiresAdmin = true,
                UseRunAs = true,
                Script = "Set-MpPreference -DisableRealtimeMonitoring $true"
            },
            new RegistryAction
            {
                Id = "sec-no-autoupdate", Name = "禁用 Windows 自动更新", Description = "⚠ 高危：关闭后系统将不再自动安装安全补丁",
                Glyph = "\uEA18", Group = "安全设置", IsSelected = false, IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
                ValueName = "NoAutoUpdate", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "sec-no-uac", Name = "关闭 UAC 弹窗提示", Description = "⚠ 高危：降低UAC等级，不再弹出权限确认",
                Glyph = "\uEA18", Group = "安全设置", IsSelected = false, IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                ValueName = "ConsentPromptBehaviorAdmin", Value = 0, ValueKind = RegistryValueKind.DWord
            },
        ];
    }

    public static List<PcSetupAction> GetPrivacyActions()
    {
        return
        [
            new RegistryAction
            {
                Id = "priv-telemetry", Name = "关闭遥测数据收集", Description = "禁用Windows诊断数据遥测服务",
                Glyph = "\uE72E", Group = "隐私保护", IsSelected = true, IsDangerous = false, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                ValueName = "AllowTelemetry", Value = 0, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "priv-activity", Name = "关闭活动历史记录", Description = "禁止Windows收集活动历史(时间线)",
                Glyph = "\uE72E", Group = "隐私保护", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Privacy",
                ValueName = "ActivityHistoryEnabled", Value = 0, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "priv-advertising", Name = "关闭广告ID", Description = "禁用Windows广告标识符追踪",
                Glyph = "\uE72E", Group = "隐私保护", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                ValueName = "Enabled", Value = 0, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "priv-location", Name = "关闭位置追踪", Description = "禁用Windows位置服务",
                Glyph = "\uE72E", Group = "隐私保护", IsSelected = false, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location",
                ValueName = "Value", Value = "Deny", ValueKind = RegistryValueKind.String
            },
            new RegistryAction
            {
                Id = "priv-feedback", Name = "关闭反馈通知", Description = "禁用Windows反馈中心定期通知",
                Glyph = "\uE72E", Group = "隐私保护", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Siuf\Rules",
                ValueName = "NumberOfSIUFInPeriod", Value = 0, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "priv-typing", Name = "关闭输入法数据收集", Description = "禁止输入法将键入数据发送到微软",
                Glyph = "\uE72E", Group = "隐私保护", IsSelected = true, IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Input\TIPC",
                ValueName = "Enabled", Value = 0, ValueKind = RegistryValueKind.DWord
            },
        ];
    }

    public static List<PcSetupAction> GetNetworkActions()
    {
        return
        [
            new RegistryAction
            {
                Id = "net-nagle", Name = "禁用Nagle算法", Description = "禁用TCP Nagle算法以降低网络延迟(游戏推荐)",
                Glyph = "\uE704", Group = "网络优化", IsSelected = false, IsDangerous = false, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces",
                ValueName = "TcpAckFrequency", Value = 1, ValueKind = RegistryValueKind.DWord
            },
            new RegistryAction
            {
                Id = "net-ttl", Name = "优化TTL值", Description = "设置TCP默认TTL为64，优化网络跳数",
                Glyph = "\uE704", Group = "网络优化", IsSelected = false, IsDangerous = false, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                ValueName = "DefaultTTL", Value = 64, ValueKind = RegistryValueKind.DWord
            },
            new PowerShellAction
            {
                Id = "net-dns-flush", Name = "刷新DNS缓存", Description = "清除DNS解析缓存，解决部分网络问题",
                Glyph = "\uE704", Group = "网络优化", IsSelected = false, IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "ipconfig /flushdns\nWrite-Host \"  DNS缓存已刷新\""
            },
            new PowerShellAction
            {
                Id = "net-reset", Name = "重置网络适配器", Description = "重置Winsock和网络适配器到默认状态",
                Glyph = "\uE704", Group = "网络优化", IsSelected = false, IsDangerous = true, RequiresAdmin = true,
                UseRunAs = true,
                Script = "netsh winsock reset\nnetsh int ip reset\nWrite-Host \"  网络适配器已重置，需要重启生效\""
            },
            new RegistryAction
            {
                Id = "net-ipv6", Name = "禁用IPv6", Description = "禁用IPv6协议，仅使用IPv4(部分网络环境需要)",
                Glyph = "\uE704", Group = "网络优化", IsSelected = false, IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters",
                ValueName = "DisabledComponents", Value = 255, ValueKind = RegistryValueKind.DWord
            },
        ];
    }

    public static List<PcSetupAction> GetAllOptimizeActions()
    {
        var list = new List<PcSetupAction>();
        list.AddRange(GetVisualOptimizeActions());
        list.AddRange(GetFileExplorerActions());
        list.AddRange(GetSystemTuneActions());
        list.AddRange(GetPrivacyActions());
        list.AddRange(GetNetworkActions());
        list.AddRange(GetSecurityActions());
        return list;
    }

    public static void ApplyVisualPreset(List<PcSetupAction> actions, string presetName)
    {
        var presets = GetVisualPresets();
        var preset = presets.FirstOrDefault(p => p.Name == presetName);
        if (preset is null) return;
        var visualIds = new HashSet<string>(actions.Where(a => a.Group == "视觉效果").Select(a => a.Id));
        foreach (var action in actions)
        {
            if (!visualIds.Contains(action.Id)) continue;
            if (preset.OptimizeItemStates.TryGetValue(action.Id, out var selected))
                action.IsSelected = selected;
        }
    }
}
