using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class AdapterInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public OperationalStatus Status { get; init; }
    public long Speed { get; init; }
    public List<IPAddress> Addresses { get; init; } = [];
    public List<IPAddress> Gateways { get; init; } = [];
    public PhysicalAddress Mac { get; init; } = PhysicalAddress.None;
    public bool IsUp => Status == OperationalStatus.Up;
    public bool HasInternet => Gateways.Count > 0 && IsUp;
    public bool IsWifi => Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
                       || Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
                       || Description.Contains("WLAN", StringComparison.OrdinalIgnoreCase)
                       || Description.Contains("802.11", StringComparison.OrdinalIgnoreCase)
                       || Name.Contains("WLAN", StringComparison.OrdinalIgnoreCase)
                       || Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase);
    public string TypeLabel => IsWifi ? "Wi-Fi" : "有线";
    public Color AccentColor => IsWifi
        ? Color.FromArgb(255, 96, 165, 250)
        : Color.FromArgb(255, 74, 222, 128);
}

public sealed class AdapterStats
{
    public int Index { get; init; }
    public long BytesReceived { get; init; }
    public long BytesSent { get; init; }
    public long SpeedDownload { get; init; }
    public long SpeedUpload { get; init; }
}

public static class NetworkAdapterProxyService
{
    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int ESTABLISHED = 5;

    [DllImport("iphlpapi.dll")]
    private static extern int GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder, int ulAf, int TableClass, int reserved);

    [DllImport("ws2_32.dll")]
    private static extern ushort ntohs(uint netshort);

    private static readonly string _policiesPath = Path.Combine(ConfigManager.GetDataDir(), "network_policies.json");
    private static List<AdapterStats> _prevStats = [];
    private static List<AdapterStats> _currentStats = [];
    private static CancellationTokenSource? _monitorCts;

    public static event Action<List<AdapterStats>>? StatsUpdated;

    #region Adapter Enumeration

    public static List<AdapterInfo> GetAdapters()
    {
        var result = new List<AdapterInfo>();

        var virtualKeywords = new[]
        {
            "Virtual", "Hyper-V", "VMware", "VirtualBox", "Docker", "WSL",
            "Tunnel", "6to4", "ISATAP", "Teredo", "Overlay", "WireGuard",
            "Bluetooth", "PAN", "Loopback", "Npcap", "WinDivert",
            "Local Area Connection*", "本地连接*", "vEthernet"
        };

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var desc = adapter.Description;
            var name = adapter.Name;

            var isVirtual = virtualKeywords.Any(k => desc.Contains(k, StringComparison.OrdinalIgnoreCase) || name.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (isVirtual) continue;

            var ipProps = adapter.GetIPProperties();
            int ifIndex = 0;
            try { ifIndex = ipProps.GetIPv4Properties()?.Index ?? 0; } catch { continue; }
            if (ifIndex == 0) continue;

            var addresses = ipProps.UnicastAddresses
                .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork
                         && !IPAddress.IsLoopback(u.Address)
                         && !u.Address.ToString().StartsWith("169.254."))
                .Select(u => u.Address).ToList();

            var gateways = ipProps.GatewayAddresses
                .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(g.Address))
                .Select(g => g.Address).ToList();

            var isRealNic = adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                         || adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

            if (!isRealNic) continue;

            result.Add(new AdapterInfo
            {
                Index = ifIndex,
                Name = adapter.Name,
                Description = adapter.Description,
                Status = adapter.OperationalStatus,
                Speed = adapter.Speed,
                Addresses = addresses,
                Gateways = gateways,
                Mac = adapter.GetPhysicalAddress()
            });
        }

        return result.OrderByDescending(a => a.HasInternet ? 0 : 1).ThenByDescending(a => a.IsUp ? 0 : 1).ToList();
    }

    #endregion

    #region Traffic Monitoring

    public static void StartMonitoring(int intervalMs = 1000)
    {
        StopMonitoring();
        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;

        _ = Task.Run(async () =>
        {
            _prevStats = GetRawStats();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(intervalMs, token);
                    var raw = GetRawStats();
                    var computed = new List<AdapterStats>();

                    for (int i = 0; i < raw.Count; i++)
                    {
                        var cur = raw[i];
                        var prev = _prevStats.FirstOrDefault(p => p.Index == cur.Index);
                        var dlSpeed = prev != null ? Math.Max(0, cur.BytesReceived - prev.BytesReceived) : 0;
                        var ulSpeed = prev != null ? Math.Max(0, cur.BytesSent - prev.BytesSent) : 0;

                        computed.Add(new AdapterStats
                        {
                            Index = cur.Index,
                            BytesReceived = cur.BytesReceived,
                            BytesSent = cur.BytesSent,
                            SpeedDownload = dlSpeed,
                            SpeedUpload = ulSpeed
                        });
                    }

                    _prevStats = raw;
                    _currentStats = computed;
                    StatsUpdated?.Invoke(computed);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }, token);
    }

    public static void StopMonitoring()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
    }

    private static List<AdapterStats> GetRawStats()
    {
        var result = new List<AdapterStats>();
        var indices = new HashSet<int>(GetAdapters().Select(a => a.Index));

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                int idx = 0;
                try { idx = adapter.GetIPProperties().GetIPv4Properties()?.Index ?? 0; } catch { continue; }
                if (!indices.Contains(idx)) continue;

                var stats = adapter.GetIPv4Statistics();
                result.Add(new AdapterStats
                {
                    Index = idx,
                    BytesReceived = stats.BytesReceived,
                    BytesSent = stats.BytesSent
                });
            }
            catch { }
        }

        return result;
    }

    public static AdapterStats? GetStatsForAdapter(int ifIndex)
    {
        return _currentStats.FirstOrDefault(s => s.Index == ifIndex);
    }

    #endregion

    #region Smart Routing

    public static void OptimizeRouting()
    {
        var adapters = GetAdapters();
        var internetAdapters = adapters.Where(a => a.HasInternet).ToList();
        if (internetAdapters.Count == 0) return;

        var wifi = internetAdapters.FirstOrDefault(a => a.IsWifi);
        var wired = internetAdapters.FirstOrDefault(a => !a.IsWifi);

        if (wired != null && wifi != null)
        {
            SetInterfaceMetric(wired.Index, 10);
            SetInterfaceMetric(wifi.Index, 50);
            return;
        }

        var best = internetAdapters.FirstOrDefault(a => a.Speed == internetAdapters.Max(x => x.Speed)) ?? internetAdapters[0];
        SetInterfaceMetric(best.Index, 10);
    }

    public static void BalanceRouting()
    {
        var adapters = GetAdapters();
        var internetAdapters = adapters.Where(a => a.HasInternet).ToList();
        if (internetAdapters.Count < 2) return;

        int metric = 10;
        foreach (var adapter in internetAdapters)
        {
            SetInterfaceMetric(adapter.Index, metric);
            metric += 5;
        }
    }

    public static void PrioritizeWifi()
    {
        var adapters = GetAdapters();
        var wifi = adapters.FirstOrDefault(a => a.IsWifi && a.HasInternet);
        var wired = adapters.FirstOrDefault(a => !a.IsWifi && a.HasInternet);

        if (wifi != null) SetInterfaceMetric(wifi.Index, 10);
        if (wired != null) SetInterfaceMetric(wired.Index, 50);
    }

    public static void PrioritizeWired()
    {
        var adapters = GetAdapters();
        var wifi = adapters.FirstOrDefault(a => a.IsWifi && a.HasInternet);
        var wired = adapters.FirstOrDefault(a => !a.IsWifi && a.HasInternet);

        if (wired != null) SetInterfaceMetric(wired.Index, 10);
        if (wifi != null) SetInterfaceMetric(wifi.Index, 50);
    }

    public static void ResetRouting()
    {
        var adapters = GetAdapters();
        foreach (var adapter in adapters.Where(a => a.HasInternet))
        {
            SetInterfaceMetric(adapter.Index, 0);
        }
    }

    #endregion

    #region Interface Metric

    public static bool SetInterfaceMetric(int ifIndex, int metric)
    {
        try
        {
            var adapter = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(a =>
                {
                    try { return a.GetIPProperties().GetIPv4Properties()?.Index == ifIndex; }
                    catch { return false; }
                });
            if (adapter == null) return false;

            var args = metric == 0
                ? $"interface ip set interface \"{adapter.Name}\" metric=automatic"
                : $"interface ip set interface \"{adapter.Name}\" metric={metric}";

            var psi = new ProcessStartInfo("netsh")
            {
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch { return false; }
    }

    #endregion

    #region Connection Tracking

    public static List<ConnectionEntry> GetActiveConnections()
    {
        var result = new List<ConnectionEntry>();
        var adapters = GetAdapters();
        var procCache = new Dictionary<int, string>();

        string GetName(int pid)
        {
            if (pid == 0 || pid == 4) return "System";
            if (procCache.TryGetValue(pid, out var n)) return n;
            try { using var p = System.Diagnostics.Process.GetProcessById(pid); return procCache[pid] = p.ProcessName; }
            catch { return procCache[pid] = $"PID:{pid}"; }
        }

        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) return result;

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(ptr, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return result;

            var count = Marshal.ReadInt32(ptr);
            var rowPtr = ptr + 4;
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                if (row.dwState != ESTABLISHED) { rowPtr += rowSize; continue; }

                var localAddr = new IPAddress(row.dwLocalAddr);
                var remoteAddr = new IPAddress(row.dwRemoteAddr);
                var localPort = ntohs(row.dwLocalPort);
                var remotePort = ntohs(row.dwRemotePort);
                var pid = (int)row.dwOwningPid;

                var adapter = adapters.FirstOrDefault(a => a.Addresses.Any(addr => addr.Equals(localAddr)));

                result.Add(new ConnectionEntry
                {
                    ProcessName = GetName(pid),
                    ProcessId = pid,
                    LocalAddress = localAddr.ToString(),
                    RemoteAddress = remoteAddr.ToString(),
                    RemotePort = remotePort,
                    AdapterName = adapter?.Name ?? "未知",
                    AdapterType = adapter?.TypeLabel ?? ""
                });

                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return result.OrderByDescending(c => c.RemotePort).Take(150).ToList();
    }

    #endregion

    #region Utility

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes >= 1L << 30) return $"{(double)bytes / (1L << 30):F2} GB";
        if (bytes >= 1L << 20) return $"{(double)bytes / (1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{(double)bytes / (1L << 10):F1} KB";
        return $"{bytes} B";
    }

    public static string FormatSpeedFriendly(long bytesPerSec)
    {
        if (bytesPerSec <= 0) return "0 B/s";
        if (bytesPerSec >= 1L << 20) return $"{(double)bytesPerSec / (1L << 20):F1} MB/s";
        if (bytesPerSec >= 1L << 10) return $"{(double)bytesPerSec / (1L << 10):F1} KB/s";
        return $"{bytesPerSec} B/s";
    }

    public static string FormatSpeed(long bytesPerSec)
    {
        if (bytesPerSec <= 0) return "0 bps";
        var bps = bytesPerSec * 8;
        if (bps >= 1_000_000_000) return $"{bps / 1_000_000_000.0:F1} Gbps";
        if (bps >= 1_000_000) return $"{bps / 1_000_000.0:F1} Mbps";
        if (bps >= 1_000) return $"{bps / 1_000.0:F1} Kbps";
        return $"{bps} bps";
    }

    #endregion
}

public sealed class ConnectionEntry
{
    public string ProcessName { get; init; } = "";
    public int ProcessId { get; init; }
    public string LocalAddress { get; init; } = "";
    public string RemoteAddress { get; init; } = "";
    public int RemotePort { get; init; }
    public string AdapterName { get; init; } = "";
    public string AdapterType { get; init; } = "";
}

[StructLayout(LayoutKind.Sequential)]
internal struct MIB_TCPROW_OWNER_PID
{
    public uint dwState;
    public uint dwLocalAddr;
    public uint dwLocalPort;
    public uint dwRemoteAddr;
    public uint dwRemotePort;
    public uint dwOwningPid;
}
