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
    public long PagedMemory { get; init; }
    public long NonpagedMemory { get; init; }
    public long VirtualMemory { get; init; }
}

/// <summary>物理内存列表分解 (对应 RAMMap"使用量"页): 已使用/已修改/待机/空闲/零页/错误页。</summary>
public sealed class MemoryListBreakdown
{
    public long ZeroBytes { get; init; }
    public long FreeBytes { get; init; }
    public long StandbyBytes { get; init; }
    public long ModifiedBytes { get; init; }
    public long BadBytes { get; init; }

    /// <summary>已使用 = 系统可见物理内存 - 各列表合计。</summary>
    public long InUseBytes { get; init; }
    /// <summary>硬件保留 = 安装容量 - 系统可见容量。</summary>
    public long HardwareReservedBytes { get; init; }
    public long PhysicalTotalBytes { get; init; }
}

/// <summary>性能计数器分解 (分页池/非分页池/系统缓存/待机优先级等)。</summary>
public sealed class MemoryPerfBreakdown
{
    public long PoolPagedBytes { get; set; }
    public long PoolNonpagedBytes { get; set; }
    public long SystemCacheBytes { get; set; }
    public long SystemCacheResidentBytes { get; set; }
    public long SystemCodeBytes { get; set; }
    public long SystemDriverBytes { get; set; }
    public long PoolPagedResidentBytes { get; set; }
    public long CachePeakBytes { get; set; }

    public long StandbyReserveBytes { get; set; }
    public long StandbyNormalBytes { get; set; }
    public long StandbyCoreBytes { get; set; }
    public long ModifiedListBytes { get; set; }
    public long FreeZeroListBytes { get; set; }

    public long CommittedBytes { get; set; }
    public long CommitLimitBytes { get; set; }
    public long AvailableBytes { get; set; }
}

/// <summary>物理内存条模块信息 (对应 RAMMap"物理范围"页, 优化版展示模块级数据)。</summary>
public sealed class PhysicalMemoryModule
{
    public string DeviceLocator { get; init; } = "";
    public string BankLabel { get; init; } = "";
    public long CapacityBytes { get; init; }
    public string Speed { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string PartNumber { get; init; } = "";
    public string TypeName { get; init; } = "";

    /// <summary>已安装内存总容量。</summary>
    public long TotalInstalledBytes => CapacityBytes;
}

/// <summary>分页文件磁盘实际占用信息。</summary>
public sealed class PageFileUsageInfo
{
    public string Name { get; init; } = "";
    public long AllocatedMB { get; init; }
    public long CurrentUsageMB { get; init; }
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

/// <summary>
/// 内存管理服务: 内存统计、内存清理、虚拟内存(分页文件)查看与设置。
/// 清理功能全部由随工具箱分发的 Sysinternals RAMMap 命令行驱动, 不再直接调用未文档化的 ntdll API。
/// RAMMap 退出码含义: 0 = 成功; 非 0 = RAMMap 自身失败; -1 = 未找到 RAMMap; -2 = 启动失败或执行超时。
/// </summary>
public static class MemoryManagerService
{
    // ---------- 内存统计 (RAMMap 命令行不输出统计, 沿用系统 API) ----------

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

    /// <summary>NtQuerySystemInformation(SystemMemoryListInformation) 返回的内存列表页数 (ULONG_PTR)。
    /// 仅用作只读查询 (对应 RAMMap 的"使用量"页数据), 不执行任何修改操作。</summary>
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

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);

    private const int SystemMemoryListInformation = 0x50;

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
                        PrivateMemory = p.PrivateMemorySize64,
                        PagedMemory = p.PagedMemorySize64,
                        NonpagedMemory = p.NonpagedSystemMemorySize64,
                        VirtualMemory = p.VirtualMemorySize64
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
                            PrivateMemory = p.PrivateMemorySize64,
                            PagedMemory = p.PagedMemorySize64,
                            NonpagedMemory = p.NonpagedSystemMemorySize64,
                            VirtualMemory = p.VirtualMemorySize64
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

    // ---------- RAMMap 分析面板: 只读数据 ----------

    /// <summary>仅通过 GlobalMemoryStatusEx 取物理内存总量 (不枚举进程, 供高频刷新使用)。</summary>
    private static long GetPhysicalTotal()
    {
        try
        {
            var ms = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            GlobalMemoryStatusEx(ref ms);
            return (long)ms.TotalPhys;
        }
        catch { return 0; }
    }

    /// <summary>物理内存列表分解 (对应 RAMMap"使用量"页): 已使用/已修改/待机/空闲/零页/错误页。</summary>
    /// <param name="installedBytes">安装容量 (来自 GetPhysicalModules, 用于计算硬件保留); 传 0 时以系统可见容量代替。</param>
    public static MemoryListBreakdown GetMemoryListBreakdown(long installedBytes = 0)
    {
        var info = new SystemMemoryListInfo();
        var size = Marshal.SizeOf<SystemMemoryListInfo>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (NtQuerySystemInformation(SystemMemoryListInformation, ptr, size, out _) != 0)
                return new MemoryListBreakdown();
            info = Marshal.PtrToStructure<SystemMemoryListInfo>(ptr);

            var pageSize = info.PageSize.ToInt64();
            long zero = info.ZeroPageCount.ToInt64() * pageSize;
            long free = info.FreePageCount.ToInt64() * pageSize;
            long standby = info.StandbyPageCount.ToInt64() * pageSize;
            long modified = (info.ModifiedPageCount.ToInt64() + info.ModifiedNoWritePageCount.ToInt64()) * pageSize;
            long bad = info.BadPageCount.ToInt64() * pageSize;

            var total = GetPhysicalTotal();
            var installed = installedBytes > 0 ? installedBytes : total;
            var inUse = Math.Max(0, total - (zero + free + standby + modified + bad));
            return new MemoryListBreakdown
            {
                ZeroBytes = zero,
                FreeBytes = free,
                StandbyBytes = standby,
                ModifiedBytes = modified,
                BadBytes = bad,
                InUseBytes = inUse,
                HardwareReservedBytes = Math.Max(0, installed - total),
                PhysicalTotalBytes = total
            };
        }
        catch
        {
            return new MemoryListBreakdown();
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>读取"Memory"分类性能计数器 (英文名在任意语言系统均可用), 失败项返回 0。</summary>
    public static MemoryPerfBreakdown GetPerfBreakdown()
    {
        return new MemoryPerfBreakdown
        {
            PoolPagedBytes = ReadPerf("Pool Paged Bytes"),
            PoolNonpagedBytes = ReadPerf("Pool Nonpaged Bytes"),
            PoolPagedResidentBytes = ReadPerf("Pool Paged Resident Bytes"),
            SystemCacheBytes = ReadPerf("Cache Bytes"),
            SystemCacheResidentBytes = ReadPerf("System Cache Resident Bytes"),
            SystemCodeBytes = ReadPerf("System Code Resident Bytes"),
            SystemDriverBytes = ReadPerf("System Driver Resident Bytes"),
            CachePeakBytes = ReadPerf("Cache Bytes Peak"),
            StandbyReserveBytes = ReadPerf("Standby Cache Reserve Bytes"),
            StandbyNormalBytes = ReadPerf("Standby Cache Normal Priority Bytes"),
            StandbyCoreBytes = ReadPerf("Standby Cache Core Bytes"),
            ModifiedListBytes = ReadPerf("Modified Page List Bytes"),
            FreeZeroListBytes = ReadPerf("Free & Zero Page List Bytes"),
            CommittedBytes = ReadPerf("Committed Bytes"),
            CommitLimitBytes = ReadPerf("Commit Limit"),
            AvailableBytes = ReadPerf("Available Bytes")
        };
    }

    private static readonly Dictionary<string, PerformanceCounter> PerfCounterCache = new(StringComparer.OrdinalIgnoreCase);

    private static long ReadPerf(string counterName)
    {
        try
        {
            if (!PerfCounterCache.TryGetValue(counterName, out var counter))
            {
                counter = new PerformanceCounter("Memory", counterName, readOnly: true);
                PerfCounterCache[counterName] = counter;
            }
            return (long)counter.RawValue;
        }
        catch { return 0; }
    }

    /// <summary>物理内存条模块信息 (WMI Win32_PhysicalMemory, 对应 RAMMap"物理范围"页的优化版展示)。</summary>
    public static List<PhysicalMemoryModule> GetPhysicalModules()
    {
        var list = new List<PhysicalMemoryModule>();
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT DeviceLocator, BankLabel, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, MemoryType FROM Win32_PhysicalMemory");
            foreach (var obj in searcher.Get())
            {
                var speed = SafeToLong(obj["Speed"]);
                var configured = SafeToLong(obj["ConfiguredClockSpeed"]);
                list.Add(new PhysicalMemoryModule
                {
                    DeviceLocator = obj["DeviceLocator"]?.ToString() ?? "",
                    BankLabel = obj["BankLabel"]?.ToString() ?? "",
                    CapacityBytes = SafeToLong(obj["Capacity"]),
                    Speed = speed > 0 ? $"{(configured > 0 ? configured : speed)} MHz" : "--",
                    Manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "",
                    PartNumber = obj["PartNumber"]?.ToString()?.Trim() ?? "",
                    TypeName = MemoryTypeName(SafeToLong(obj["MemoryType"]))
                });
            }
        }
        catch { }
        return list;
    }

    /// <summary>SMBIOS 内存类型映射 (Win32_PhysicalMemory.MemoryType)。</summary>
    private static string MemoryTypeName(long type) => type switch
    {
        17 => "SDRAM",
        19 => "RDRAM",
        20 => "DDR",
        21 => "DDR2",
        22 => "BRAM",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        32 => "HBM",
        33 => "HBM2",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => "未知"
    };

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

    // ---------- 内存清理: 全部通过 RAMMap 命令行驱动 ----------
    // RAMMap 随工具箱分发, 位于 Tools/内存工具/RAMMap (link.json 指向 其他工具/RAMMap)。
    // 命令行参数 (Sysinternals 官方):
    //   -s  清空待机列表 (purge standby)            -u  清空已修改页面列表 (写入磁盘)
    //   -e  清空进程工作集                          -t1 清空系统工作集 (收缩系统文件缓存)
    //   -t  清空已修改列表 + 工作集 (全部)          -m  清空优先 0 待机列表
    //   -c  压缩内存 (Win8+)                       -accepteula 静默接受 EULA

    /// <summary>RAMMap 启动超时时间; 正常一次清空操作秒级完成。</summary>
    private static readonly TimeSpan RamMapTimeout = TimeSpan.FromSeconds(60);

    /// <summary>在 Tools 目录中按当前架构查找 RAMMap 可执行文件, 找不到返回 null。</summary>
    public static string? FindRamMapExe()
    {
        var root = ToolCatalog.ToolsRoot;
        if (!Directory.Exists(root)) return null;

        var dirs = new[]
        {
            Path.Combine(root, "内存工具", "RAMMap"),
            Path.Combine(root, "其他工具", "RAMMap")
        };

        // 按当前架构优先; ARM64 可回退 x64/32 位, x64 可回退 32 位 (同架构 exe 无法在低架构进程启动)
        var ranked = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => new[] { "RAMMap64a.exe", "RAMMap64.exe", "RAMMap.exe" },
            Architecture.X64 => new[] { "RAMMap64.exe", "RAMMap.exe" },
            _ => new[] { "RAMMap.exe" }
        };

        foreach (var dir in dirs)
        {
            foreach (var name in ranked)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }

    /// <summary>运行一次 RAMMap 命令并等待退出。</summary>
    /// <returns>0 成功; 非 0 RAMMap 退出码; -1 未找到 RAMMap; -2 启动失败或超时。</returns>
    public static async Task<int> RunRamMapAsync(string arguments)
    {
        var exe = FindRamMapExe();
        if (exe is null) return -1;

        Process? p = null;
        try
        {
            p = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-accepteula {arguments}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            return -2;
        }
        if (p is null) return -2;

        using (p)
        using (var cts = new CancellationTokenSource(RamMapTimeout))
        {
            try
            {
                await p.WaitForExitAsync(cts.Token);
                return p.ExitCode;
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return -2;
            }
        }
    }

    /// <summary>清理待机内存: RAMMap -s 清空待机列表 + -u 将已修改页写入磁盘。</summary>
    public static async Task<int> CleanStandbyAsync()
    {
        var s = await RunRamMapAsync("-s");
        if (s != 0) return s;
        return await RunRamMapAsync("-u");
    }

    /// <summary>收紧系统工作集: RAMMap -e 清空进程工作集 + -t1 清空系统工作集 (收缩系统文件缓存)。</summary>
    public static async Task<int> TrimWorkingSetsAsync()
    {
        var e = await RunRamMapAsync("-e");
        if (e != 0) return e;
        return await RunRamMapAsync("-t1");
    }

    /// <summary>全部清理: 清理待机内存 + 收紧系统工作集。</summary>
    public static async Task<int> CleanAllAsync()
    {
        var standby = await CleanStandbyAsync();
        if (standby != 0) return standby;
        return await TrimWorkingSetsAsync();
    }

    // ---------- 虚拟内存 (分页文件) ----------

    private const string MemoryManagementKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

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
}