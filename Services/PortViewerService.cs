using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

public enum PortTcpState
{
    Closed = 1,
    Listen = 2,
    SynSent = 3,
    SynReceived = 4,
    Established = 5,
    FinWait1 = 6,
    FinWait2 = 7,
    CloseWait = 8,
    Closing = 9,
    LastAck = 10,
    TimeWait = 11,
    DeleteTcb = 12,
    Unknown = 0
}

public sealed class PortEntry
{
    public string Protocol { get; init; } = "";
    public IPAddress LocalAddress { get; init; } = IPAddress.None;
    public int LocalPort { get; init; }
    public IPAddress RemoteAddress { get; init; } = IPAddress.None;
    public int RemotePort { get; init; }
    public PortTcpState State { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
}

public static class PortViewerService
{
    public static async Task<List<PortEntry>> ScanAsync()
    {
        return await Task.Run(Scan);
    }

    public static bool KillProcess(int pid, out string error)
    {
        error = "";
        if (pid == 0)
        {
            error = "无法结束 System 进程";
            return false;
        }
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.Kill();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static List<PortEntry> Scan()
    {
        var entries = new List<PortEntry>();

        try
        {
            var tcpTable = GetTcpTable();
            foreach (var row in tcpTable)
            {
                var pid = row.OwningPid;
                entries.Add(new PortEntry
                {
                    Protocol = "TCP",
                    LocalAddress = row.LocalAddress,
                    LocalPort = row.LocalPort,
                    RemoteAddress = row.RemoteAddress,
                    RemotePort = row.RemotePort,
                    State = row.State,
                    ProcessId = pid,
                    ProcessName = GetProcessName(pid)
                });
            }
        }
        catch { }

        try
        {
            var udpTable = GetUdpTable();
            foreach (var row in udpTable)
            {
                var pid = row.OwningPid;
                entries.Add(new PortEntry
                {
                    Protocol = "UDP",
                    LocalAddress = row.LocalAddress,
                    LocalPort = row.LocalPort,
                    RemoteAddress = IPAddress.None,
                    RemotePort = 0,
                    State = PortTcpState.Unknown,
                    ProcessId = pid,
                    ProcessName = GetProcessName(pid)
                });
            }
        }
        catch { }

        return entries
            .OrderBy(e => e.Protocol)
            .ThenBy(e => e.LocalPort)
            .ToList();
    }

    private static string GetProcessName(int pid)
    {
        if (pid == 0) return "System";
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.ProcessName;
        }
        catch
        {
            return $"PID:{pid}";
        }
    }

    private static List<TcpRow> GetTcpTable()
    {
        var result = new List<TcpRow>();
        var dwSize = 0;
        GetTcpTable(IntPtr.Zero, ref dwSize, false);

        var ptr = Marshal.AllocHGlobal(dwSize);
        try
        {
            var ret = GetTcpTable(ptr, ref dwSize, false);
            if (ret != 0) return result;

            var rows = Marshal.ReadInt32(ptr);
            var rowPtr = ptr + 4;
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < rows; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                result.Add(new TcpRow(row));
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return result;
    }

    private static List<UdpRow> GetUdpTable()
    {
        var result = new List<UdpRow>();
        var dwSize = 0;
        GetUdpTable(IntPtr.Zero, ref dwSize, false);

        var ptr = Marshal.AllocHGlobal(dwSize);
        try
        {
            var ret = GetUdpTable(ptr, ref dwSize, false);
            if (ret != 0) return result;

            var rows = Marshal.ReadInt32(ptr);
            var rowPtr = ptr + 4;
            var rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();

            for (int i = 0; i < rows; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                result.Add(new UdpRow(row));
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return result;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetUdpTable(IntPtr pUdpTable, ref int pdwSize, bool bOrder);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    private sealed class TcpRow
    {
        public IPAddress LocalAddress;
        public int LocalPort;
        public IPAddress RemoteAddress;
        public int RemotePort;
        public PortTcpState State;
        public int OwningPid;

        public TcpRow(MIB_TCPROW_OWNER_PID row)
        {
            LocalAddress = new IPAddress(row.dwLocalAddr);
            LocalPort = ntohs(row.dwLocalPort);
            RemoteAddress = new IPAddress(row.dwRemoteAddr);
            RemotePort = ntohs(row.dwRemotePort);
            State = (PortTcpState)row.dwState;
            OwningPid = (int)row.dwOwningPid;
        }
    }

    private sealed class UdpRow
    {
        public IPAddress LocalAddress;
        public int LocalPort;
        public int OwningPid;

        public UdpRow(MIB_UDPROW_OWNER_PID row)
        {
            LocalAddress = new IPAddress(row.dwLocalAddr);
            LocalPort = ntohs(row.dwLocalPort);
            OwningPid = (int)row.dwOwningPid;
        }
    }

    [DllImport("ws2_32.dll")]
    private static extern ushort ntohs(uint netshort);
}
