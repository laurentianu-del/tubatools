using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;

namespace TubaWinUi3.Services;

public sealed class BsodEntry
{
    public DateTimeOffset Time { get; init; }
    public string BugCheckCode { get; init; } = "";
    public string BugCheckParameter { get; init; } = "";
    public string CausingDriver { get; init; } = "";
    public string CausingAddress { get; init; } = "";
    public string Message { get; init; } = "";
    public int EventId { get; init; }
}

public sealed class BsodInsight
{
    public string BugCheckCode { get; init; } = "";
    public string Title { get; init; } = "";
    public string Severity { get; init; } = "";
    public string SeverityColor { get; init; } = "";
    public string Description { get; init; } = "";
    public string Suggestions { get; init; } = "";
}

public static class BsodAnalysisService
{
    private static readonly Dictionary<string, BsodInsight> KnownBugChecks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0x0000000a"] = new BsodInsight
        {
            BugCheckCode = "0x0000000A",
            Title = "IRQL_NOT_LESS_OR_EQUAL",
            Severity = "高",
            SeverityColor = "#EF4444",
            Description = "内核模式驱动或系统服务试图以过高的 IRQL 级别访问不可分页的内存地址。通常由有缺陷的驱动程序或不兼容的硬件引起。",
            Suggestions = "1. 更新或回滚最近安装的驱动程序\n2. 运行 Windows 内存诊断检查内存问题\n3. 使用 sfc /scannow 修复系统文件\n4. 检查新安装的硬件是否兼容"
        },
        ["0x0000001e"] = new BsodInsight
        {
            BugCheckCode = "0x0000001E",
            Title = "KMODE_EXCEPTION_NOT_HANDLED",
            Severity = "高",
            SeverityColor = "#EF4444",
            Description = "内核模式程序产生了未处理的异常。通常由驱动程序错误或内存访问违规引起。",
            Suggestions = "1. 检查蓝屏信息中提到的驱动文件名\n2. 更新或卸载相关驱动\n3. 检查内存条是否正常\n4. 确认系统没有超频"
        },
        ["0x00000050"] = new BsodInsight
        {
            BugCheckCode = "0x00000050",
            Title = "PAGE_FAULT_IN_NONPAGED_AREA",
            Severity = "高",
            SeverityColor = "#EF4444",
            Description = "系统试图访问不存在的分页内存，通常由错误的驱动程序、硬件故障或内存损坏引起。",
            Suggestions = "1. 运行 Windows 内存诊断\n2. 更新所有驱动程序\n3. 检查硬盘是否有坏道\n4. 卸载最近安装的软件或驱动"
        },
        ["0x0000007b"] = new BsodInsight
        {
            BugCheckCode = "0x0000007B",
            Title = "INACCESSIBLE_BOOT_DEVICE",
            Severity = "严重",
            SeverityColor = "#DC2626",
            Description = "Windows 无法访问系统启动设备。通常与存储控制器驱动、硬盘故障或 BIOS 设置有关。",
            Suggestions = "1. 检查 BIOS 中 SATA 模式设置（AHCI/RAID）\n2. 修复引导记录：bootrec /rebuildbcd\n3. 检查硬盘连接和数据线\n4. 在恢复环境中运行 chkdsk /f"
        },
        ["0x0000007e"] = new BsodInsight
        {
            BugCheckCode = "0x0000007E",
            Title = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
            Severity = "高",
            SeverityColor = "#EF4444",
            Description = "系统线程产生了未处理的异常。常见于显卡驱动不兼容或系统文件损坏。",
            Suggestions = "1. 更新显卡驱动程序\n2. 使用 DDU 彻底卸载旧驱动后重新安装\n3. 运行 sfc /scannow 和 DISM 修复\n4. 检查是否超频不稳定"
        },
        ["0x000000d1"] = new BsodInsight
        {
            BugCheckCode = "0x000000D1",
            Title = "DRIVER_IRQL_NOT_LESS_OR_EQUAL",
            Severity = "高",
            SeverityColor = "#EF4444",
            Description = "驱动程序试图访问过高 IRQL 级别的内存。通常由网络驱动或杀毒软件驱动引起。",
            Suggestions = "1. 检查蓝屏信息中提到的 .sys 文件\n2. 更新网络适配器驱动\n3. 暂时禁用杀毒软件测试\n4. 更新无线网卡驱动"
        },
        ["0x00000116"] = new BsodInsight
        {
            BugCheckCode = "0x00000116",
            Title = "VIDEO_TDR_FAILURE",
            Severity = "中",
            SeverityColor = "#F59E0B",
            Description = "显卡驱动未在规定时间内响应。通常是显卡驱动崩溃、显卡过热或硬件故障。",
            Suggestions = "1. 更新或回滚显卡驱动\n2. 检查显卡温度是否过高\n3. 使用 DDU 清理后重新安装驱动\n4. 检查显卡供电是否充足"
        },
        ["0x00000139"] = new BsodInsight
        {
            BugCheckCode = "0x00000139",
            Title = "KERNEL_SECURITY_CHECK_FAILURE",
            Severity = "中",
            SeverityColor = "#F59E0B",
            Description = "内核检测到关键数据结构损坏。通常由驱动程序错误、内存问题或系统文件损坏引起。",
            Suggestions = "1. 运行 sfc /scannow 修复系统文件\n2. 运行 Windows 内存诊断\n3. 更新所有驱动程序\n4. 检查磁盘错误：chkdsk /f"
        },
        ["0x0000013a"] = new BsodInsight
        {
            BugCheckCode = "0x0000013A",
            Title = "KERNEL_MODE_HEAP_CORRUPTION",
            Severity = "高",
            SeverityColor = "#EF4444",
            Description = "内核模式堆损坏。通常由驱动程序越界写入内存或内存硬件故障引起。",
            Suggestions = "1. 更新所有驱动程序到最新版本\n2. 运行内存诊断工具\n3. 卸载最近安装的驱动或软件\n4. 使用 Driver Verifier 定位问题驱动"
        },
        ["0x0000003b"] = new BsodInsight
        {
            BugCheckCode = "0x0000003B",
            Title = "SYSTEM_SERVICE_EXCEPTION",
            Severity = "中",
            SeverityColor = "#F59E0B",
            Description = "系统服务在执行时产生异常。通常由驱动程序错误或杀毒软件冲突引起。",
            Suggestions = "1. 更新或卸载杀毒软件\n2. 检查蓝屏信息中提到的驱动文件\n3. 运行 sfc /scannow\n4. 确保系统已安装最新更新"
        },
        ["0x00000024"] = new BsodInsight
        {
            BugCheckCode = "0x00000024",
            Title = "NTFS_FILE_SYSTEM",
            Severity = "中",
            SeverityColor = "#F59E0B",
            Description = "NTFS 文件系统出错。通常由硬盘故障、文件系统损坏或磁盘控制器驱动问题引起。",
            Suggestions = "1. 运行 chkdsk /f /r 检查磁盘\n2. 检查硬盘健康状态（S.M.A.R.T.）\n3. 更新磁盘控制器驱动\n4. 检查硬盘数据线和供电"
        },
        ["0x0000007f"] = new BsodInsight
        {
            BugCheckCode = "0x0000007F",
            Title = "UNEXPECTED_KERNEL_MODE_TRAP",
            Severity = "高",
            SeverityColor = "#EF4444",
            Description = "内核捕获了意外的 CPU 陷阱。通常由硬件故障（内存、CPU）或超频不稳定引起。",
            Suggestions = "1. 恢复 BIOS 默认设置，取消超频\n2. 运行内存诊断\n3. 检查 CPU 温度\n4. 更新 BIOS 固件"
        },
        ["0x000000ea"] = new BsodInsight
        {
            BugCheckCode = "0x000000EA",
            Title = "THREAD_STUCK_IN_DEVICE_DRIVER",
            Severity = "中",
            SeverityColor = "#F59E0B",
            Description = "设备驱动程序中的线程无限等待。通常与显卡驱动有关。",
            Suggestions = "1. 更新显卡驱动\n2. 降低显卡超频设置\n3. 检查显卡温度\n4. 使用 DDU 重新安装显卡驱动"
        },
        ["0x000000c4"] = new BsodInsight
        {
            BugCheckCode = "0x000000C4",
            Title = "DRIVER_VERIFIER_DETECTED_VIOLATION",
            Severity = "中",
            SeverityColor = "#F59E0B",
            Description = "驱动程序验证器检测到驱动违规。这是调试工具主动检测到驱动问题的结果。",
            Suggestions = "1. 查看蓝屏信息中提到的驱动文件名\n2. 更新或替换问题驱动\n3. 如不需要，关闭驱动验证器\n4. 联系驱动程序开发者"
        },
        ["0x00000109"] = new BsodInsight
        {
            BugCheckCode = "0x00000109",
            Title = "CRITICAL_STRUCTURE_CORRUPTION",
            Severity = "严重",
            SeverityColor = "#DC2626",
            Description = "内核检测到关键内核数据结构被修改。通常由驱动程序错误写入内核内存引起，也可能是内存硬件故障。",
            Suggestions = "1. 使用 Driver Verifier 定位问题驱动\n2. 运行内存诊断工具\n3. 卸载最近安装的驱动或软件\n4. 如有超频，请恢复默认设置"
        },
        ["0x000000ef"] = new BsodInsight
        {
            BugCheckCode = "0x000000EF",
            Title = "CRITICAL_PROCESS_DIED",
            Severity = "严重",
            SeverityColor = "#DC2626",
            Description = "关键系统进程意外终止。通常由系统文件损坏、驱动冲突或恶意软件引起。",
            Suggestions = "1. 运行 sfc /scannow 和 DISM /RestoreHealth\n2. 检查是否有恶意软件\n3. 修复系统文件或执行系统还原\n4. 检查硬盘健康状态"
        },
        ["0xc000021a"] = new BsodInsight
        {
            BugCheckCode = "0xC000021A",
            Title = "STATUS_SYSTEM_PROCESS_TERMINATED",
            Severity = "严重",
            SeverityColor = "#DC2626",
            Description = "Winlogon 或 CSRSS 子系统意外终止。通常由系统文件损坏或第三方软件替换了系统 DLL 引起。",
            Suggestions = "1. 执行系统文件检查：sfc /scannow\n2. 尝试系统还原到之前的还原点\n3. 检查最近安装的软件是否替换了系统文件\n4. 如无法进入系统，使用恢复环境修复"
        },
        ["0x00000101"] = new BsodInsight
        {
            BugCheckCode = "0x00000101",
            Title = "CLOCK_WATCHDOG_TIMEOUT",
            Severity = "中",
            SeverityColor = "#F59E0B",
            Description = "CPU 核心未在规定时间内响应时钟中断。通常由 CPU 超频不稳定、BIOS 设置问题或硬件故障引起。",
            Suggestions = "1. 恢复 BIOS 默认设置\n2. 取消 CPU 超频\n3. 更新 BIOS 固件\n4. 检查 CPU 供电和散热"
        },
        ["0x00000133"] = new BsodInsight
        {
            BugCheckCode = "0x00000133",
            Title = "DPC_WATCHDOG_VIOLATION",
            Severity = "中",
            SeverityColor = "#F59E0B",
            Description = "延迟过程调用（DPC）执行时间过长。通常由存储驱动或固件问题引起。",
            Suggestions = "1. 更新存储（SSD/HDD）固件和驱动\n2. 检查 SATA/AHCI 驱动\n3. 更新芯片组驱动\n4. 检查磁盘健康状态"
        },
        ["0x0000009f"] = new BsodInsight
        {
            BugCheckCode = "0x0000009F",
            Title = "DRIVER_POWER_STATE_FAILURE",
            Severity = "低",
            SeverityColor = "#22C55E",
            Description = "驱动程序电源状态不一致。通常在系统休眠/唤醒时发生，驱动未正确处理电源状态转换。",
            Suggestions = "1. 更新芯片组和电源管理驱动\n2. 更新显卡驱动\n3. 禁用快速启动测试\n4. 检查 BIOS 电源管理设置"
        },
        ["0x00000141"] = new BsodInsight
        {
            BugCheckCode = "0x00000141",
            Title = "VIDEO_ENGINE_TIMEOUT_DETECTED",
            Severity = "中",
            SeverityColor = "#F59E0B",
            Description = "显卡引擎超时未响应。与 VIDEO_TDR_FAILURE 类似，通常由显卡驱动或硬件问题引起。",
            Suggestions = "1. 更新显卡驱动\n2. 检查显卡温度和供电\n3. 降低显卡频率测试\n4. 使用 DDU 重新安装显卡驱动"
        },
        ["0x000000fe"] = new BsodInsight
        {
            BugCheckCode = "0x000000FE",
            Title = "BUGCODE_USB_DRIVER",
            Severity = "低",
            SeverityColor = "#22C55E",
            Description = "USB 驱动程序出错。通常由 USB 设备故障、USB 驱动不兼容或供电不足引起。",
            Suggestions = "1. 更新 USB 控制器驱动\n2. 拔掉不必要的 USB 设备测试\n3. 检查 USB 设备供电\n4. 更新芯片组驱动"
        }
    };

    public static async Task<List<BsodEntry>> GetBsodEventsAsync()
    {
        return await Task.Run(GetBsodEvents);
    }

    public static List<BsodEntry> GetBsodEvents()
    {
        var entries = new List<BsodEntry>();

        try
        {
            CollectFromEventLog(entries, "System", 1001);
        }
        catch { }

        try
        {
            CollectFromEventLog(entries, "System", 1003);
        }
        catch { }

        entries.Sort((a, b) => b.Time.CompareTo(a.Time));
        return entries;
    }

    private static void CollectFromEventLog(List<BsodEntry> entries, string logName, int eventId)
    {
        var query = $"*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting' or @Name='BugCheck'] and EventID={eventId}]]";

        try
        {
            using var logReader = new EventLogReader(new EventLogQuery(logName, PathType.LogName, query));
            EventRecord? record;
            while ((record = logReader.ReadEvent()) is not null)
            {
                using (record)
                {
                    var entry = ParseEventRecord(record, eventId);
                    if (entry is not null)
                        entries.Add(entry);
                }
            }
        }
        catch { }

        if (entries.Count == 0)
        {
            try
            {
                using var log = new EventLog(logName);
                for (int i = log.Entries.Count - 1; i >= Math.Max(0, log.Entries.Count - 500); i--)
                {
                    try
                    {
                        var entry = log.Entries[i];
                        if (entry.InstanceId == eventId || entry.InstanceId == 1001 || entry.InstanceId == 1003)
                        {
                            var bsod = ParseEventLogEntry(entry);
                            if (bsod is not null)
                                entries.Add(bsod);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        if (entries.Count == 0)
        {
            try
            {
                using var log = new EventLog(logName);
                for (int i = log.Entries.Count - 1; i >= Math.Max(0, log.Entries.Count - 2000); i--)
                {
                    try
                    {
                        var entry = log.Entries[i];
                        if (entry.Source == "Microsoft-Windows-Kernel-Power" && entry.InstanceId == 41)
                        {
                            entries.Add(new BsodEntry
                            {
                                Time = entry.TimeGenerated,
                                BugCheckCode = "0x00000000",
                                BugCheckParameter = "未正常关机（Kernel-Power 41）",
                                CausingDriver = "",
                                CausingAddress = "",
                                Message = "系统未正常关机，可能是蓝屏后自动重启导致",
                                EventId = 41
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static BsodEntry? ParseEventRecord(EventRecord record, int eventId)
    {
        try
        {
            var bugCheckCode = "";
            var parameters = "";
            var causingDriver = "";

            if (record.Properties.Count > 0)
            {
                for (int i = 0; i < record.Properties.Count; i++)
                {
                    var prop = record.Properties[i];
                    if (i == 0 && prop.Value is not null)
                    {
                        try
                        {
                            var val = Convert.ToUInt64(prop.Value.ToString());
                            bugCheckCode = $"0x{val:X8}";
                        }
                        catch
                        {
                            bugCheckCode = prop.Value?.ToString() ?? "";
                        }
                    }
                    else if (prop.Value is not null)
                    {
                        var val = prop.Value.ToString();
                        if (!string.IsNullOrEmpty(val))
                            parameters += (parameters.Length > 0 ? ", " : "") + val;
                    }
                }
            }

            var message = record.FormatDescription() ?? "";

            return new BsodEntry
            {
                Time = record.TimeCreated ?? DateTimeOffset.Now,
                BugCheckCode = string.IsNullOrEmpty(bugCheckCode) ? "未知" : bugCheckCode,
                BugCheckParameter = parameters,
                CausingDriver = causingDriver,
                CausingAddress = "",
                Message = message,
                EventId = eventId
            };
        }
        catch { }
        return null;
    }

    private static BsodEntry? ParseEventLogEntry(EventLogEntry entry)
    {
        try
        {
            var message = entry.Message ?? "";
            var bugCheckCode = "";
            var parameters = "";
            var causingDriver = "";

            var codeMatch = System.Text.RegularExpressions.Regex.Match(
                message, @"0x[0-9a-fA-F]{8}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (codeMatch.Success)
                bugCheckCode = codeMatch.Value;

            var driverMatch = System.Text.RegularExpressions.Regex.Match(
                message, @"(?i)(?:caused by|由.*?引起|driver|驱动)[^\n]*?(\w+\.sys)");
            if (driverMatch.Success)
                causingDriver = driverMatch.Groups[1].Value;

            return new BsodEntry
            {
                Time = entry.TimeGenerated,
                BugCheckCode = string.IsNullOrEmpty(bugCheckCode) ? "未知" : bugCheckCode,
                BugCheckParameter = parameters,
                CausingDriver = causingDriver,
                CausingAddress = "",
                Message = message,
                EventId = (int)entry.InstanceId
            };
        }
        catch { }
        return null;
    }

    public static BsodInsight? GetInsight(string bugCheckCode)
    {
        if (string.IsNullOrEmpty(bugCheckCode) || bugCheckCode == "未知")
            return null;

        var normalized = bugCheckCode.ToLowerInvariant();
        if (KnownBugChecks.TryGetValue(normalized, out var insight))
            return insight;

        var codeNum = normalized.TrimStart('0', 'x');
        if (KnownBugChecks.TryGetValue($"0x{codeNum}", out insight))
            return insight;

        return new BsodInsight
        {
            BugCheckCode = bugCheckCode,
            Title = "未知蓝屏类型",
            Severity = "未知",
            SeverityColor = "#6B7280",
            Description = $"此蓝屏错误代码 {bugCheckCode} 未在常见类型数据库中。建议在线搜索该代码以获取更多信息。",
            Suggestions = "1. 在搜索引擎中搜索该错误代码\n2. 查看蓝屏详细参数中的驱动文件名\n3. 使用 WinDbg 分析 dump 文件\n4. 检查 Windows 事件查看器获取更多上下文"
        };
    }

    public static List<BsodInsight> GetInsightsForEntries(List<BsodEntry> entries)
    {
        var insights = new List<BsodInsight>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var code = entry.BugCheckCode;
            if (seen.Contains(code)) continue;
            seen.Add(code);

            var insight = GetInsight(code);
            if (insight is not null)
                insights.Add(insight);
        }

        return insights;
    }
}
