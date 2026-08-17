using Microsoft.Win32;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class SystemOptimizer
{
    public static List<PcSetupAction> GetVisualOptimizeActions()
    {
        return
        [
            new RegistryAction
            {
                Id = "visual-animation", Name = "窗口动画效果", Description = "开启后启用窗口打开/关闭/最小化动画，关闭则禁用",
                Glyph = "\uE7B3", Group = "视觉效果", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                ValueName = "VisualFXSetting", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 2
            },
            new RegistryAction
            {
                Id = "visual-transparency", Name = "透明效果(Mica/Acrylic)", Description = "开启后启用系统透明效果，关闭则禁用",
                Glyph = "\uE7B3", Group = "视觉效果", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                ValueName = "EnableTransparency", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "visual-shadow", Name = "窗口阴影效果", Description = "开启后启用窗口投影阴影，关闭则禁用",
                Glyph = "\uE7B3", Group = "视觉效果", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "ListviewShadow", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "visual-smooth-scroll", Name = "平滑滚动效果", Description = "开启后启用列表平滑滚动，关闭则禁用",
                Glyph = "\uE7B3", Group = "视觉效果", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Control Panel\Desktop",
                ValueName = "SmoothScroll", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "visual-desktop-shadow", Name = "桌面图标阴影", Description = "开启后启用桌面图标文字阴影，关闭则禁用",
                Glyph = "\uE7B3", Group = "视觉效果", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "ListviewShadow", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "visual-taskbar-center", Name = "任务栏居中对齐", Description = "开启后任务栏图标居中对齐，关闭则左对齐",
                Glyph = "\uE77B", Group = "视觉效果", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "TaskbarAl", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "visual-taskbar-small", Name = "任务栏小图标", Description = "开启后使用小尺寸任务栏图标，关闭则恢复大图标",
                Glyph = "\uE77B", Group = "视觉效果", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "TaskbarSi", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "visual-taskbar-glom", Name = "任务栏图标合并", Description = "开启后同程序窗口始终合并显示，关闭则不合并",
                Glyph = "\uE77B", Group = "视觉效果", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "TaskbarGlomLevel", Value = 2, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
        ];
    }

    public static List<PcSetupAction> GetFileExplorerActions()
    {
        return
        [
            new RegistryAction
            {
                Id = "file-show-ext", Name = "显示文件扩展名", Description = "开启后在资源管理器中显示文件扩展名，关闭则隐藏",
                Glyph = "\uE8B7", Group = "文件管理", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "HideFileExt", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "file-show-hidden", Name = "显示隐藏文件", Description = "开启后在资源管理器中显示隐藏的文件和文件夹，关闭则隐藏",
                Glyph = "\uE8B7", Group = "文件管理", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "Hidden", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 2
            },
            new RegistryAction
            {
                Id = "file-nav-pane-expand", Name = "导航窗格展开到文件夹", Description = "开启后资源管理器导航窗格自动展开当前文件夹，关闭则展开到根目录",
                Glyph = "\uE8B7", Group = "文件管理", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "NavPaneExpandToCurrentFolder", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "file-show-fullpath", Name = "标题栏显示完整路径", Description = "开启后在资源管理器标题栏显示完整文件路径，关闭则不显示",
                Glyph = "\uE8B7", Group = "文件管理", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState",
                ValueName = "FullPath", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "file-disable-ads", Name = "关闭资源管理器广告", Description = "开启后关闭资源管理器中的同步提供程序通知和广告，关闭则恢复",
                Glyph = "\uE8B7", Group = "文件管理", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "ShowSyncProviderNotifications", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "file-quick-access", Name = "打开资源管理器到此电脑", Description = "开启后资源管理器默认打开位置改为此电脑，关闭则为快速访问",
                Glyph = "\uE8B7", Group = "文件管理", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "LaunchTo", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "file-icons-only", Name = "始终显示图标不显示缩略图", Description = "开启后用图标替代文件缩略图提升大目录浏览速度，关闭则恢复缩略图",
                Glyph = "\uE8B7", Group = "文件管理", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "IconsOnly", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "file-no-share-wizard", Name = "关闭共享向导", Description = "开启后右键「共享」直接显示共享对话框，关闭则恢复向导",
                Glyph = "\uE8B7", Group = "文件管理", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "SharingWizard", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "file-no-recent", Name = "隐藏最近使用的文件", Description = "开启后开始菜单和跳转列表隐藏最近打开的文件记录，关闭则恢复",
                Glyph = "\uE8B7", Group = "文件管理", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "Start_TrackDocs", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "file-shortcut-arrow", Name = "快捷方式小箭头", Description = "开启后隐藏快捷方式图标上的小箭头，关闭则恢复(需刷新图标缓存或重启生效)",
                Glyph = "\uE8B7", Group = "文件管理", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons",
                ValueName = "29", Value = "", ValueKind = RegistryValueKind.String
            },
        ];
    }

    public static List<PcSetupAction> GetSystemTuneActions()
    {
        return
        [
            new PowerShellAction
            {
                Id = "tune-highperf", Name = "电源计划→高性能", Description = "切换电源计划为高性能模式(一次性)",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"
            },
            new PowerShellAction
            {
                Id = "tune-no-hibernate", Name = "关闭系统休眠", Description = "开启后禁用休眠并释放hiberfil.sys空间，关闭则重新启用休眠",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "powercfg -h off\nWrite-Host \"  已关闭系统休眠\"",
                OffScript = "powercfg -h on\nWrite-Host \"  已开启系统休眠\"",
                StateScript = "$v = (Get-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Power' -Name 'HibernateEnabled' -ErrorAction SilentlyContinue).HibernateEnabled; Write-Output ($v -eq 0)"
            },
            new RegistryAction
            {
                Id = "tune-no-restore", Name = "关闭系统还原", Description = "开启后禁用系统还原功能，关闭则重新启用",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore",
                ValueName = "DisableSR", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new PowerShellAction
            {
                Id = "tune-disable-startup", Name = "禁用多余启动项", Description = "禁用非系统必要的启动项(仅禁用第三方启动项)",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = true, RequiresAdmin = true,
                UseRunAs = true,
                Script = "$startupItems = Get-ItemProperty 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' -ErrorAction SilentlyContinue\n$systemKeywords = @('SecurityHealth','WindowsDefender','OneDrive','Shell','TaskManager')\nforeach ($prop in $startupItems.PSObject.Properties) {\n    if ($prop.Name -match '^(PSPath|PSParentPath|PSChildName|PSDrive|PSProvider)') continue\n    $isSystem = $false\n    foreach ($kw in $systemKeywords) { if ($prop.Value -match $kw) { $isSystem = $true; break } }\n    if (-not $isSystem) {\n        Remove-ItemProperty 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' -Name $prop.Name -ErrorAction SilentlyContinue\n        Write-Host \"  禁用启动项: $($prop.Name)\"\n    }\n}"
            },
            new RegistryAction
            {
                Id = "tune-disable-cortana", Name = "禁用Cortana搜索", Description = "开启后隐藏任务栏Cortana搜索框，关闭则恢复",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = false, RequiresAdmin = true,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Search",
                ValueName = "SearchboxTaskbarMode", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "tune-disable-news", Name = "关闭小组件/新闻", Description = "开启后禁用任务栏小组件和新闻资讯，关闭则恢复",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = false, RequiresAdmin = true,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "TaskbarDa", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "tune-disable-chat", Name = "关闭任务栏聊天", Description = "开启后禁用任务栏Teams聊天图标，关闭则恢复",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = false, RequiresAdmin = true,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "TaskbarMn", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "tune-disable-clipboard", Name = "关闭剪贴板历史", Description = "开启后禁用Windows剪贴板历史记录，关闭则恢复",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Clipboard",
                ValueName = "EnableClipboardHistory", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new PowerShellAction
            {
                Id = "tune-cleanup-temp", Name = "清理临时文件", Description = "清理Windows临时文件夹和用户临时文件夹",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "Remove-Item \"$env:TEMP\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nRemove-Item \"$env:LOCALAPPDATA\\Temp\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nRemove-Item \"C:\\Windows\\Temp\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nWrite-Host \"  临时文件清理完成\""
            },
            new RegistryAction
            {
                Id = "tune-superfetch", Name = "禁用SysMain(Superfetch)", Description = "开启后禁用SysMain服务减少磁盘占用，关闭则恢复(仅HDD建议)",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SYSTEM\CurrentControlSet\Services\SysMain",
                ValueName = "Start", Value = 4, ValueKind = RegistryValueKind.DWord, OffValue = 2
            },
            new RegistryAction
            {
                Id = "tune-shutdown-timeout", Name = "缩短关机等待超时", Description = "开启后关机等待从20秒缩短到3秒，关闭则恢复20秒",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = false, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SYSTEM\CurrentControlSet\Control",
                ValueName = "WaitToKillServiceTimeout", Value = 3000, ValueKind = RegistryValueKind.DWord, OffValue = 20000
            },
            new RegistryAction
            {
                Id = "tune-ntfs-lastaccess", Name = "关闭文件访问时间戳", Description = "开启后禁止NTFS更新最近访问时间减少磁盘写入，关闭则恢复(SSD更友好)",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = false, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SYSTEM\CurrentControlSet\Control\FileSystem",
                ValueName = "NtfsDisableLastAccessUpdate", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new PowerShellAction
            {
                Id = "tune-disable-8dot3", Name = "禁用8.3短文件名", Description = "开启后禁用NTFS 8.3短文件名生成，关闭则重新启用",
                Glyph = "\uE945", Group = "系统调优", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "fsutil behavior set disable8dot3 1\nWrite-Host \"  已禁用 8.3 短文件名\"",
                OffScript = "fsutil behavior set disable8dot3 0\nWrite-Host \"  已启用 8.3 短文件名\"",
                StateScript = "$v = (Get-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\FileSystem' -Name 'NtfsDisable8dot3NameCreation' -ErrorAction SilentlyContinue).NtfsDisable8dot3NameCreation; Write-Output ($v -eq 1)"
            },
        ];
    }

    public static List<PcSetupAction> GetSecurityActions()
    {
        return
        [
            new RegistryAction
            {
                Id = "sec-no-autoupdate", Name = "禁用 Windows 自动更新", Description = "⚠ 高危：开启后系统将不再自动安装安全补丁，关闭则恢复",
                Glyph = "\uEA18", Group = "安全设置", IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
                ValueName = "NoAutoUpdate", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "sec-no-uac", Name = "关闭 UAC 弹窗提示", Description = "⚠ 高危：开启后降低UAC等级不再弹出权限确认，关闭则恢复",
                Glyph = "\uEA18", Group = "安全设置", IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                ValueName = "ConsentPromptBehaviorAdmin", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 5
            },
            new PowerShellAction
            {
                Id = "sec-restore-point", Name = "创建系统还原点", Description = "优化前创建系统还原点，便于随时回滚系统设置(一次性)",
                Glyph = "\uEA18", Group = "安全设置", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "Checkpoint-Computer -Description '图吧工具箱优化前还原点' -RestorePointType MODIFY_SETTINGS -ErrorAction SilentlyContinue\nWrite-Host \"  系统还原点创建完成\""
            },
            new RegistryAction
            {
                Id = "sec-disable-smartscreen", Name = "关闭SmartScreen筛选器", Description = "⚠ 高危：开启后关闭SmartScreen网页与应用安全筛选，关闭则恢复",
                Glyph = "\uEA18", Group = "安全设置", IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SOFTWARE\Policies\Microsoft\Windows\System",
                ValueName = "EnableSmartScreen", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "sec-disable-defender", Name = "关闭Defender实时防护", Description = "⚠ 高危：开启后关闭Windows Defender实时防护，关闭则恢复(不推荐，仅游戏/调试场景)",
                Glyph = "\uEA18", Group = "安全设置", IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SOFTWARE\Policies\Microsoft\Windows Defender",
                ValueName = "DisableAntiSpyware", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
        ];
    }

    public static List<PcSetupAction> GetGameActions()
    {
        return
        [
            new RegistryAction
            {
                Id = "game-mode", Name = "开启游戏模式", Description = "开启后为游戏分配更多系统资源，关闭则恢复",
                Glyph = "\uE7FC", Group = "游戏优化", IsDangerous = false, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SOFTWARE\Microsoft\GameBar",
                ValueName = "AutoGameModeEnabled", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new PowerShellAction
            {
                Id = "game-dvr-off", Name = "关闭Xbox后台录制", Description = "开启后关闭游戏录制与后台捕获减少性能损耗，关闭则恢复",
                Glyph = "\uE7FC", Group = "游戏优化", IsDangerous = false, RequiresAdmin = false,
                UseRunAs = false,
                Script = "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR' -Name 'AppCaptureEnabled' -Value 0 -Type DWord -ErrorAction SilentlyContinue\nSet-ItemProperty -Path 'HKCU:\\System\\GameConfigStore' -Name 'GameDVR_Enabled' -Value 0 -Type DWord -ErrorAction SilentlyContinue\nWrite-Host \"  已关闭 Xbox 后台录制\"",
                OffScript = "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR' -Name 'AppCaptureEnabled' -Value 1 -Type DWord -ErrorAction SilentlyContinue\nSet-ItemProperty -Path 'HKCU:\\System\\GameConfigStore' -Name 'GameDVR_Enabled' -Value 1 -Type DWord -ErrorAction SilentlyContinue\nWrite-Host \"  已开启 Xbox 后台录制\"",
                StateScript = "$v = (Get-ItemProperty 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR' -Name 'AppCaptureEnabled' -ErrorAction SilentlyContinue).AppCaptureEnabled; Write-Output ($v -eq 0)"
            },
            new RegistryAction
            {
                Id = "game-fse", Name = "禁用全屏优化", Description = "开启后禁用全屏优化让游戏独占全屏，关闭则恢复",
                Glyph = "\uE7FC", Group = "游戏优化", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"System\GameConfigStore",
                ValueName = "GameDVR_FSEBehaviorMode", Value = 2, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "game-hw-sched", Name = "开启硬件加速GPU计划", Description = "开启后启用硬件加速GPU调度降低渲染延迟，关闭则恢复",
                Glyph = "\uE7FC", Group = "游戏优化", IsDangerous = false, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                ValueName = "HwSchMode", Value = 2, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new PowerShellAction
            {
                Id = "game-mouse", Name = "关闭鼠标加速", Description = "开启后关闭鼠标指针加速获得1:1手感，关闭则恢复",
                Glyph = "\uE7FC", Group = "游戏优化", IsDangerous = false, RequiresAdmin = false,
                UseRunAs = false,
                Script = "Set-ItemProperty -Path 'HKCU:\\Control Panel\\Mouse' -Name 'MouseSpeed' -Value '0' -ErrorAction SilentlyContinue\nSet-ItemProperty -Path 'HKCU:\\Control Panel\\Mouse' -Name 'MouseThreshold1' -Value '0' -ErrorAction SilentlyContinue\nSet-ItemProperty -Path 'HKCU:\\Control Panel\\Mouse' -Name 'MouseThreshold2' -Value '0' -ErrorAction SilentlyContinue\nWrite-Host \"  已关闭鼠标加速\"",
                OffScript = "Set-ItemProperty -Path 'HKCU:\\Control Panel\\Mouse' -Name 'MouseSpeed' -Value '1' -ErrorAction SilentlyContinue\nSet-ItemProperty -Path 'HKCU:\\Control Panel\\Mouse' -Name 'MouseThreshold1' -Value '6' -ErrorAction SilentlyContinue\nSet-ItemProperty -Path 'HKCU:\\Control Panel\\Mouse' -Name 'MouseThreshold2' -Value '10' -ErrorAction SilentlyContinue\nWrite-Host \"  已开启鼠标加速\"",
                StateScript = "$v = (Get-ItemProperty 'HKCU:\\Control Panel\\Mouse' -Name 'MouseSpeed' -ErrorAction SilentlyContinue).MouseSpeed; Write-Output ($v -eq 0)"
            },
            new PowerShellAction
            {
                Id = "game-ultimate-power", Name = "启用卓越性能电源计划", Description = "启用隐藏的卓越性能电源计划并切换生效(一次性，笔记本慎用)",
                Glyph = "\uE945", Group = "游戏优化", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 2>$null | Out-Null\npowercfg -setactive e9a42b02-d5df-448d-aa00-03f14749eb61\nWrite-Host \"  已切换到卓越性能电源计划\""
            },
        ];
    }

    public static List<PcSetupAction> GetCleanActions()
    {
        return
        [
            new PowerShellAction
            {
                Id = "clean-temp", Name = "清理临时文件", Description = "清理Windows和用户临时文件夹",
                Glyph = "\uE90F", Group = "资源清理", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "Remove-Item \"$env:TEMP\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nRemove-Item \"$env:LOCALAPPDATA\\Temp\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nRemove-Item \"C:\\Windows\\Temp\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nWrite-Host \"  临时文件清理完成\""
            },
            new PowerShellAction
            {
                Id = "clean-prefetch", Name = "清理预取缓存", Description = "清理Windows预取(Prefetch)缓存文件",
                Glyph = "\uE90F", Group = "资源清理", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "Remove-Item \"C:\\Windows\\Prefetch\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nWrite-Host \"  预取缓存清理完成\""
            },
            new PowerShellAction
            {
                Id = "clean-thumbnail", Name = "清理缩略图缓存", Description = "清理资源管理器缩略图缓存(thumbcache)",
                Glyph = "\uE90F", Group = "资源清理", IsDangerous = false, RequiresAdmin = false,
                UseRunAs = false,
                Script = "Remove-Item \"$env:LOCALAPPDATA\\Microsoft\\Windows\\Explorer\\thumbcache_*.db\" -Force -ErrorAction SilentlyContinue\nRemove-Item \"$env:LOCALAPPDATA\\Microsoft\\Windows\\Explorer\\iconcache_*.db\" -Force -ErrorAction SilentlyContinue\nWrite-Host \"  缩略图缓存清理完成\""
            },
            new PowerShellAction
            {
                Id = "clean-update-cache", Name = "清理Windows更新缓存", Description = "清理Windows Update下载缓存文件夹",
                Glyph = "\uE90F", Group = "资源清理", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "Stop-Service -Name wuauserv -Force -ErrorAction SilentlyContinue\nRemove-Item \"C:\\Windows\\SoftwareDistribution\\Download\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nStart-Service -Name wuauserv -ErrorAction SilentlyContinue\nWrite-Host \"  更新缓存清理完成\""
            },
            new PowerShellAction
            {
                Id = "clean-crash-dumps", Name = "清理崩溃转储", Description = "清理系统错误报告和崩溃转储(minidump)文件",
                Glyph = "\uE90F", Group = "资源清理", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "Remove-Item \"C:\\Windows\\Minidump\\*\" -Force -ErrorAction SilentlyContinue\nRemove-Item \"$env:ProgramData\\Microsoft\\Windows\\WER\\ReportArchive\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nRemove-Item \"$env:ProgramData\\Microsoft\\Windows\\WER\\ReportQueue\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nRemove-Item \"$env:LOCALAPPDATA\\Microsoft\\Windows\\WER\\ReportArchive\\*\" -Recurse -Force -ErrorAction SilentlyContinue\nWrite-Host \"  崩溃转储清理完成\""
            },
            new PowerShellAction
            {
                Id = "clean-recycle-bin", Name = "清空回收站", Description = "清空所有驱动器的回收站",
                Glyph = "\uE90F", Group = "资源清理", IsDangerous = true, RequiresAdmin = false,
                UseRunAs = false,
                Script = "Clear-RecycleBin -Force -ErrorAction SilentlyContinue\nWrite-Host \"  回收站已清空\""
            },
        ];
    }

    public static List<PcSetupAction> GetPrivacyActions()
    {
        return
        [
            new RegistryAction
            {
                Id = "priv-telemetry", Name = "关闭遥测数据收集", Description = "开启后禁用Windows诊断数据遥测服务，关闭则恢复",
                Glyph = "\uE72E", Group = "隐私保护", IsDangerous = false, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                ValueName = "AllowTelemetry", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "priv-activity", Name = "关闭活动历史记录", Description = "开启后禁止Windows收集活动历史(时间线)，关闭则恢复",
                Glyph = "\uE72E", Group = "隐私保护", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Privacy",
                ValueName = "ActivityHistoryEnabled", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "priv-advertising", Name = "关闭广告ID", Description = "开启后禁用Windows广告标识符追踪，关闭则恢复",
                Glyph = "\uE72E", Group = "隐私保护", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                ValueName = "Enabled", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "priv-location", Name = "关闭位置追踪", Description = "开启后禁用Windows位置服务，关闭则恢复",
                Glyph = "\uE72E", Group = "隐私保护", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location",
                ValueName = "Value", Value = "Deny", ValueKind = RegistryValueKind.String, OffValue = "Allow"
            },
            new RegistryAction
            {
                Id = "priv-feedback", Name = "关闭反馈通知", Description = "开启后禁用Windows反馈中心定期通知，关闭则恢复",
                Glyph = "\uE72E", Group = "隐私保护", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Siuf\Rules",
                ValueName = "NumberOfSIUFInPeriod", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "priv-typing", Name = "关闭输入法数据收集", Description = "开启后禁止输入法将键入数据发送到微软，关闭则恢复",
                Glyph = "\uE72E", Group = "隐私保护", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Input\TIPC",
                ValueName = "Enabled", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new PowerShellAction
            {
                Id = "priv-content-delivery", Name = "关闭开始菜单广告与建议", Description = "开启后关闭开始菜单、设置页中的应用推广与建议，关闭则恢复",
                Glyph = "\uE72E", Group = "隐私保护", IsDangerous = false, RequiresAdmin = false,
                UseRunAs = false,
                Script = "$base = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager'\nforeach ($name in @('SubscribedContent-338389Enabled','SubscribedContent-338388Enabled','SubscribedContent-338393Enabled','SubscribedContent-353694Enabled','SubscribedContent-310093Enabled','SystemPaneSuggestionsEnabled')) {\n    Set-ItemProperty -Path $base -Name $name -Value 0 -Type DWord -ErrorAction SilentlyContinue\n}\nWrite-Host \"  已关闭开始菜单广告与建议\"",
                OffScript = "$base = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager'\nforeach ($name in @('SubscribedContent-338389Enabled','SubscribedContent-338388Enabled','SubscribedContent-338393Enabled','SubscribedContent-353694Enabled','SubscribedContent-310093Enabled','SystemPaneSuggestionsEnabled')) {\n    Set-ItemProperty -Path $base -Name $name -Value 1 -Type DWord -ErrorAction SilentlyContinue\n}\nWrite-Host \"  已恢复开始菜单广告与建议\"",
                StateScript = "$v = (Get-ItemProperty 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager' -Name 'SubscribedContent-338389Enabled' -ErrorAction SilentlyContinue).'SubscribedContent-338389Enabled'; Write-Output ($v -eq 0)"
            },
            new RegistryAction
            {
                Id = "priv-settings-sync", Name = "关闭跨设备设置同步", Description = "开启后停止将系统设置同步到其他Windows设备，关闭则恢复",
                Glyph = "\uE72E", Group = "隐私保护", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\SettingSync",
                ValueName = "SettingsSynchronizationEnabled", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
            new RegistryAction
            {
                Id = "priv-mru", Name = "清除最近打开文件记录", Description = "开启后关闭开始菜单与任务栏的最近使用项目追踪，关闭则恢复",
                Glyph = "\uE72E", Group = "隐私保护", IsDangerous = false, RequiresAdmin = false,
                Hive = "HKCU", KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                ValueName = "Start_TrackEnabled", Value = 0, ValueKind = RegistryValueKind.DWord, OffValue = 1
            },
        ];
    }

    public static List<PcSetupAction> GetNetworkActions()
    {
        return
        [
            new RegistryAction
            {
                Id = "net-nagle", Name = "禁用Nagle算法", Description = "开启后禁用TCP Nagle算法降低网络延迟(游戏推荐)，关闭则恢复",
                Glyph = "\uE704", Group = "网络优化", IsDangerous = false, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces",
                ValueName = "TcpAckFrequency", Value = 1, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new RegistryAction
            {
                Id = "net-ttl", Name = "优化TTL值", Description = "开启后设置TCP默认TTL为64，关闭则恢复默认128",
                Glyph = "\uE704", Group = "网络优化", IsDangerous = false, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                ValueName = "DefaultTTL", Value = 64, ValueKind = RegistryValueKind.DWord, OffValue = 128
            },
            new PowerShellAction
            {
                Id = "net-dns-flush", Name = "刷新DNS缓存", Description = "清除DNS解析缓存，解决部分网络问题(一次性)",
                Glyph = "\uE704", Group = "网络优化", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "ipconfig /flushdns\nWrite-Host \"  DNS缓存已刷新\""
            },
            new PowerShellAction
            {
                Id = "net-reset", Name = "重置网络适配器", Description = "重置Winsock和网络适配器到默认状态",
                Glyph = "\uE704", Group = "网络优化", IsDangerous = true, RequiresAdmin = true,
                UseRunAs = true,
                Script = "netsh winsock reset\nnetsh int ip reset\nWrite-Host \"  网络适配器已重置，需要重启生效\""
            },
            new RegistryAction
            {
                Id = "net-ipv6", Name = "禁用IPv6", Description = "开启后仅使用IPv4(部分网络环境需要)，关闭则恢复IPv6",
                Glyph = "\uE704", Group = "网络优化", IsDangerous = true, RequiresAdmin = true,
                Hive = "HKLM", KeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters",
                ValueName = "DisabledComponents", Value = 255, ValueKind = RegistryValueKind.DWord, OffValue = 0
            },
            new PowerShellAction
            {
                Id = "net-dns-cache", Name = "增大DNS缓存容量", Description = "开启后扩大DNS解析缓存容量并延长缓存时间，关闭则恢复默认",
                Glyph = "\uE704", Group = "网络优化", IsDangerous = false, RequiresAdmin = true,
                UseRunAs = true,
                Script = "$base = 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Dnscache\\Parameters'\nNew-Item -Path $base -Force | Out-Null\nSet-ItemProperty -Path $base -Name 'CacheHashTableBucketSize' -Value 1 -Type DWord\nSet-ItemProperty -Path $base -Name 'CacheHashTableSize' -Value 384 -Type DWord\nSet-ItemProperty -Path $base -Name 'MaxCacheEntryTtlLimit' -Value 86400 -Type DWord\nSet-ItemProperty -Path $base -Name 'MaxNegativeCacheTtl' -Value 32 -Type DWord\nWrite-Host \"  DNS 缓存已扩容\"",
                OffScript = "$base = 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Dnscache\\Parameters'\nRemove-ItemProperty -Path $base -Name 'CacheHashTableBucketSize' -ErrorAction SilentlyContinue\nRemove-ItemProperty -Path $base -Name 'CacheHashTableSize' -ErrorAction SilentlyContinue\nRemove-ItemProperty -Path $base -Name 'MaxCacheEntryTtlLimit' -ErrorAction SilentlyContinue\nRemove-ItemProperty -Path $base -Name 'MaxNegativeCacheTtl' -ErrorAction SilentlyContinue\nWrite-Host \"  DNS 缓存已恢复默认\"",
                StateScript = "$v = (Get-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Dnscache\\Parameters' -Name 'CacheHashTableSize' -ErrorAction SilentlyContinue).CacheHashTableSize; Write-Output ($v -eq 384)"
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
        list.AddRange(GetGameActions());
        list.AddRange(GetCleanActions());
        list.AddRange(GetSecurityActions());
        return list;
    }
}
