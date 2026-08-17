using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

/// <summary>一组内存统计 (物理内存 / 虚拟内存 / 系统工作集)。</summary>
public sealed class MemoryStats
{
    public long PhysicalUsed { get; set; }
    public long PhysicalAvailable { get; set; }
    public long PhysicalTotal { get; set; }

    public long VirtualUsed { get; set; }
    public long VirtualAvailable { get; set; }
    public long VirtualTotal { get; set; }

    public long WorkingSetUsed { get; set; }
    public long WorkingSetAvailable { get; set; }
    public long WorkingSetTotal { get; set; }
}

/// <summary>单个进程的内存占用信息。</summary>
public sealed class ProcessMemoryInfo
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public long WorkingSet { get; init; }
    public long PrivateMemory { get; init; }
}

/// <summary>分页文件磁盘实际占用信息。</summary>
public sealed class PageFileUsageInfo
{
    public string Name { get; init; } = "";
    public long AllocatedMB { get; init; }
    public long CurrentUsageMB { get; init; }
}

/// <summary>内存列表统计 (待机/已修改页), 用于计算清理量。</summary>
public sealed class MemoryListInfo
{
    public long StandbyBytes { get; init; }
    public long ModifiedBytes { get; init; }

    /// <summary>待机列表 + 已修改列表合计, 即"清理待机内存"可释放的总量。</summary>
    public long Total => StandbyBytes + ModifiedBytes;
}

/// <summary>一个分页文件条目 (虚拟内存设置)。</summary>
public sealed class PageFileEntry
{
    /// <summary>分页文件路径, 如 "C:\pagefile.sys"。</summary>
    public string FilePath { get; set; } = "";
    /// <summary>系统管理大小 (PagingFiles 中的 "0 0")。</summary>
    public bool SystemManaged { get; set; }
    /// <summary>无分页文件 (PagingFiles 中的单个 "0")。</summary>
    public bool Disabled { get; set; }
    public long InitialMB { get; set; }
    public long MaximumMB { get; set; }

    public string DriveLetter => string.IsNullOrWhiteSpace(FilePath) ? "" : FilePath.Trim()[0].ToString();

    public string TypeLabel => SystemManaged ? "系统管理" : Disabled ? "无分页文件" : "自定义";

    public string Description
    {
        get
        {
            if (SystemManaged) return "系统管理";
            if (Disabled) return "无分页文件";
            return $"初始 {InitialMB} MB / 最大 {MaximumMB} MB";
        }
    }
}

/// <summary>内存管理服务: 内存统计、内存清理、虚拟内存(分页文件)查看与设置。</summary>
public static class MemoryManagerService
{
    // ---------- NtSetSystemInformation ----------
    private const int SystemMemoryListInformation = 0x50;
    private const int SystemFileCacheInformation = 0x15;

    private const uint MemoryEmptyWorkingSets = 2;
    private const uint MemoryFlushModifiedList = 3;
    private const uint MemoryPurgeStandbyList = 4;

    // ---------- Token 特权 ----------
    private const uint SE_PRIVILEGE_ENABLED = 0x2;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;

    /// <summary>LUID = LowPart + HighPart, 与 winnt.h 的 LUID 布局一致。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    /// <summary>TOKEN_PRIVILEGES = PrivilegeCount + LUID_AND_ATTRIBUTES, 原生共 16 字节。
    /// 注意不能把 Luid 写成 long: x64 下 8 字节对齐会插入 4 字节 padding 导致布局错位。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemCacheInfo64
    {
        public long MinimumWorkingSet;
        public long MaximumWorkingSet;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemCacheInfo32
    {
        public uint MinimumWorkingSet;
        public uint MaximumWorkingSet;
    }

    /// <summary>NtQuerySystemInformation(SystemMemoryListInformation) 返回的内存列表页数 (ULONG_PTR)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemMemoryListInfo
    {
        public IntPtr ZeroPageCount;
        public IntPtr FreePageCount;
        public IntPtr StandbyPageCount;
        public IntPtr ModifiedPageCount;
        public IntPtr ModifiedNoWritePageCount;
        public IntPtr BadPageCount;
        public IntPtr PageSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("ntdll.dll")]
    private static extern uint NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TokenPrivileges newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    // ---------- 文档化 API (收紧工作集回退方案) ----------

    private const uint ProcessSetQuota = 0x0100;
    private const uint ProcessQueryInformation = 0x0400;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    private const string MemoryManagementKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

    /// <summary>获取当前内存统计。</summary>
    public static MemoryStats GetStats()
    {
        var ms = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref ms);

        var physicalTotal = (long)ms.TotalPhys;
        var physicalAvail = (long)ms.AvailPhys;

        long workingSet = 0;
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id != 0 && p.Id != 4)
                        workingSet += p.WorkingSet64;
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }

        return new MemoryStats
        {
            PhysicalUsed = physicalTotal - physicalAvail,
            PhysicalAvailable = physicalAvail,
            PhysicalTotal = physicalTotal,

            VirtualTotal = (long)ms.TotalPageFile,
            VirtualAvailable = (long)ms.AvailPageFile,
            VirtualUsed = (long)ms.TotalPageFile - (long)ms.AvailPageFile,

            WorkingSetTotal = physicalTotal,
            WorkingSetUsed = workingSet,
            WorkingSetAvailable = Math.Max(0, physicalTotal - workingSet)
        };
    }

    /// <summary>获取按工作集排序的内存占用最高的进程。</summary>
    public static List<ProcessMemoryInfo> GetTopProcesses(int count = 10)
    {
        var list = new List<ProcessMemoryInfo>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0 || p.Id == 4) continue;
                    if (p.WorkingSet64 <= 0) continue;
                    list.Add(new ProcessMemoryInfo
                    {
                        Pid = p.Id,
                        Name = string.IsNullOrWhiteSpace(p.ProcessName) ? "(未知)" : p.ProcessName,
                        WorkingSet = p.WorkingSet64,
                        PrivateMemory = p.PrivateMemorySize64
                    });
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }

        return list
            .OrderByDescending(x => x.WorkingSet)
            .Take(count)
            .ToList();
    }

    /// <summary>一次进程枚举同时获取统计与排行, 避免重复遍历。</summary>
    public static (MemoryStats Stats, List<ProcessMemoryInfo> Procs) GetSnapshot(int topN = 10)
    {
        var ms = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref ms);

        var physicalTotal = (long)ms.TotalPhys;
        var physicalAvail = (long)ms.AvailPhys;

        long workingSet = 0;
        var all = new List<ProcessMemoryInfo>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0 || p.Id == 4) continue;
                    var ws = p.WorkingSet64;
                    if (ws > 0)
                    {
                        workingSet += ws;
                        all.Add(new ProcessMemoryInfo
                        {
                            Pid = p.Id,
                            Name = string.IsNullOrWhiteSpace(p.ProcessName) ? "(未知)" : p.ProcessName,
                            WorkingSet = ws,
                            PrivateMemory = p.PrivateMemorySize64
                        });
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }

        var stats = new MemoryStats
        {
            PhysicalUsed = physicalTotal - physicalAvail,
            PhysicalAvailable = physicalAvail,
            PhysicalTotal = physicalTotal,

            VirtualTotal = (long)ms.TotalPageFile,
            VirtualAvailable = (long)ms.AvailPageFile,
            VirtualUsed = (long)ms.TotalPageFile - (long)ms.AvailPageFile,

            WorkingSetTotal = physicalTotal,
            WorkingSetUsed = workingSet,
            WorkingSetAvailable = Math.Max(0, physicalTotal - workingSet)
        };

        return (stats, all.OrderByDescending(x => x.WorkingSet).Take(topN).ToList());
    }

    /// <summary>分页文件磁盘实际占用。</summary>
    public static List<PageFileUsageInfo> GetPageFileUsage()
    {
        var list = new List<PageFileUsageInfo>();
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage");
            foreach (var obj in searcher.Get())
            {
                list.Add(new PageFileUsageInfo
                {
                    Name = obj["Name"]?.ToString() ?? "",
                    AllocatedMB = SafeToLong(obj["AllocatedBaseSize"]),
                    CurrentUsageMB = SafeToLong(obj["CurrentUsage"])
                });
            }
        }
        catch { }
        return list;
    }

    private static long SafeToLong(object? value)
    {
        if (value is null) return 0;
        try { return Convert.ToInt64(value); } catch { return 0; }
    }

    /// <summary>查询待机列表与已修改列表大小 (页数 × 页大小)。</summary>
    public static MemoryListInfo GetMemoryLists()
    {
        var info = new SystemMemoryListInfo();
        var size = Marshal.SizeOf<SystemMemoryListInfo>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (NtQuerySystemInformation(SystemMemoryListInformation, ptr, size, out _) != 0)
                return new MemoryListInfo();
            info = Marshal.PtrToStructure<SystemMemoryListInfo>(ptr);
            var pageSize = info.PageSize.ToInt64();
            return new MemoryListInfo
            {
                StandbyBytes = info.StandbyPageCount.ToInt64() * pageSize,
                ModifiedBytes = (info.ModifiedPageCount.ToInt64() + info.ModifiedNoWritePageCount.ToInt64()) * pageSize
            };
        }
        catch
        {
            return new MemoryListInfo();
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>上次清理失败时的 NTSTATUS (0 表示成功), 供 UI 展示错误码。</summary>
    public static uint LastCleanStatus { get; private set; }

    /// <summary>清理待机内存 (待机列表 + 已修改列表), 返回释放的字节数; 失败返回 -1。</summary>
    public static long CleanStandbyMemory()
    {
        // 清空内存列表需要 SeProfileSingleProcessPrivilege, 管理员令牌中该特权默认是禁用的, 必须先启用
        if (!EnablePrivilege("SeProfileSingleProcessPrivilege") || !EnablePrivilege("SeIncreaseQuotaPrivilege"))
        {
            LastCleanStatus = 0xC0000061; // STATUS_PRIVILEGE_NOT_HELD
            return -1;
        }

        var before = GetMemoryLists();
        var s1 = SetMemoryListCommand(MemoryPurgeStandbyList);
        var s2 = SetMemoryListCommand(MemoryFlushModifiedList);
        var after = GetMemoryLists();

        LastCleanStatus = s1 != 0 ? s1 : s2;
        if (s1 != 0 || s2 != 0) return -1;
        return Math.Max(0, before.Total - after.Total);
    }

    /// <summary>收紧系统工作集 (清空所有进程工作集 + 收缩系统文件缓存), 返回释放的字节数; 失败返回 -1。</summary>
    public static long TrimWorkingSets()
    {
        if (!EnablePrivilege("SeProfileSingleProcessPrivilege") || !EnablePrivilege("SeIncreaseQuotaPrivilege"))
        {
            LastCleanStatus = 0xC0000061;
            return -1;
        }

        var before = GetTotalWorkingSet();
        var s1 = SetMemoryListCommand(MemoryEmptyWorkingSets);
        if (s1 != 0)
        {
            // 内核级调用失败 (如缺少特权) 时, 回退到文档化 API 逐进程清空工作集
            EmptyAllProcessWorkingSets();
        }
        var cacheStatus = TrimSystemFileCache();
        var after = GetTotalWorkingSet();

        LastCleanStatus = s1 != 0 ? s1 : cacheStatus;
        if (s1 != 0 && cacheStatus != 0) return -1;
        return Math.Max(0, before - after);
    }

    /// <summary>全部清理, 返回释放的字节数; 失败返回 -1。</summary>
    public static long CleanAll()
    {
        var standby = CleanStandbyMemory();
        var trim = TrimWorkingSets();
        if (standby < 0 && trim < 0) return -1;
        return Math.Max(0, standby) + Math.Max(0, trim);
    }

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    // ---------- 虚拟内存 (分页文件) ----------

    /// <summary>是否自动管理分页文件。</summary>
    public static bool IsAutomaticPageFile()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MemoryManagementKey);
            if (key is null) return true;
            var value = key.GetValue("AutomaticManagedPagefile");
            if (value is int i) return i != 0;
            return true;
        }
        catch { return true; }
    }

    /// <summary>读取当前分页文件配置。</summary>
    public static List<PageFileEntry> GetPageFileEntries()
    {
        var result = new List<PageFileEntry>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MemoryManagementKey);
            if (key is null) return result;

            if (key.GetValue("AutomaticManagedPagefile") is int auto && auto != 0)
            {
                // 自动管理: 从 ExistingPageFiles 读取现有分页文件
                if (key.GetValue("ExistingPageFiles") is string[] existing)
                {
                    foreach (var path in existing)
                        result.Add(new PageFileEntry { FilePath = path, SystemManaged = true });
                }
                return result;
            }

            if (key.GetValue("PagingFiles") is string[] entries)
                result.AddRange(entries.Select(ParsePageFileEntry));
        }
        catch { }
        return result;
    }

    /// <summary>解析一行 PagingFiles 注册表条目, 如 "C:\pagefile.sys 0 0"。</summary>
    public static PageFileEntry ParsePageFileEntry(string line)
    {
        var entry = new PageFileEntry { SystemManaged = true };
        if (string.IsNullOrWhiteSpace(line)) return entry;

        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return entry;

        entry.FilePath = parts[0];

        if (parts.Length == 1)
        {
            entry.SystemManaged = false;
            entry.Disabled = true;
            return entry;
        }

        var minOk = long.TryParse(parts[1], out var min);
        var maxOk = long.TryParse(parts.Length > 2 ? parts[2] : "", out var max);

        if (minOk && maxOk && min == 0 && max == 0)
        {
            entry.SystemManaged = true;
            entry.Disabled = false;
        }
        else if (minOk && !maxOk)
        {
            entry.SystemManaged = false;
            entry.Disabled = true;
        }
        else if (minOk && maxOk)
        {
            entry.SystemManaged = false;
            entry.Disabled = false;
            entry.InitialMB = min;
            entry.MaximumMB = max;
        }
        else
        {
            entry.SystemManaged = false;
            entry.Disabled = true;
        }
        return entry;
    }

    /// <summary>应用分页文件设置。auto 为 true 时由系统自动管理; 否则按 entries 写入。</summary>
    public static bool ApplyPageFileConfig(bool auto, List<PageFileEntry> entries)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MemoryManagementKey, writable: true);
            if (key is null) return false;

            key.SetValue("AutomaticManagedPagefile", auto ? 1 : 0, RegistryValueKind.DWord);

            if (!auto)
            {
                var lines = entries
                    .Select(ToRegistryLine)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();
                key.SetValue("PagingFiles", lines, RegistryValueKind.MultiString);
            }

            return true;
        }
        catch { return false; }
    }

    private static string ToRegistryLine(PageFileEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.FilePath)) return "";
        if (entry.SystemManaged) return $"{entry.FilePath} 0 0";
        if (entry.Disabled) return $"{entry.FilePath} 0";
        return $"{entry.FilePath} {entry.InitialMB} {entry.MaximumMB}";
    }

    /// <summary>打开系统的"性能选项-虚拟内存"设置对话框。</summary>
    public static void OpenSystemVirtualMemorySettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "SystemPropertiesPerformance.exe",
                UseShellExecute = true
            });
        }
        catch { }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        double gb = bytes / 1024.0 / 1024.0 / 1024.0;
        if (gb >= 1) return $"{gb:F1} GB";
        double mb = bytes / 1024.0 / 1024.0;
        if (mb >= 1) return $"{mb:F1} MB";
        return $"{bytes / 1024.0:F0} KB";
    }

    public static double BytesToGb(long bytes) => bytes / 1024.0 / 1024.0 / 1024.0;

    // ---------- 内部实现 ----------

    /// <summary>所有进程工作集总和 (不含 Idle/System), 用于对比收紧前后的变化。</summary>
    private static long GetTotalWorkingSet()
    {
        long total = 0;
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id != 0 && p.Id != 4)
                        total += p.WorkingSet64;
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
        return total;
    }

    private static uint SetMemoryListCommand(uint command)
    {
        return SetSystemInfo(SystemMemoryListInformation, ref command);
    }

    private static uint TrimSystemFileCache()
    {
        if (IntPtr.Size == 8)
        {
            var info = new SystemCacheInfo64 { MinimumWorkingSet = -1, MaximumWorkingSet = -1 };
            return SetSystemInfo(SystemFileCacheInformation, ref info);
        }
        else
        {
            var info = new SystemCacheInfo32 { MinimumWorkingSet = uint.MaxValue, MaximumWorkingSet = uint.MaxValue };
            return SetSystemInfo(SystemFileCacheInformation, ref info);
        }
    }

    /// <summary>文档化 API 回退: 用 EmptyWorkingSet 逐进程清空工作集 (需要 PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA)。</summary>
    private static void EmptyAllProcessWorkingSets()
    {
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0 || p.Id == 4) continue;
                    var handle = OpenProcess(ProcessQueryInformation | ProcessSetQuota, false, p.Id);
                    if (handle == IntPtr.Zero) continue;
                    try { EmptyWorkingSet(handle); }
                    finally { CloseHandle(handle); }
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
    }

    /// <summary>调用 NtSetSystemInformation, 返回 NTSTATUS (0 为成功), 失败不再静默吞掉。</summary>
    private static uint SetSystemInfo<T>(int infoClass, ref T data) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(data, ptr, false);
            return NtSetSystemInformation(infoClass, ptr, size);
        }
        catch
        {
            return uint.MaxValue;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static bool EnablePrivilege(string privilegeName)
    {
        try
        {
            var processHandle = GetCurrentProcess();
            if (!OpenProcessToken(processHandle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
                return false;
            try
            {
                if (!LookupPrivilegeValue(null!, privilegeName, out var luid))
                    return false;
                var priv = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };
                // AdjustTokenPrivileges 返回 TRUE 但部分特权未启用时, 需检查 GetLastError == ERROR_NOT_ALL_ASSIGNED
                if (!AdjustTokenPrivileges(token, false, ref priv, 0, IntPtr.Zero, IntPtr.Zero))
                    return false;
                return Marshal.GetLastWin32Error() != ERROR_NOT_ALL_ASSIGNED;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch
        {
            return false;
        }
    }
}
