using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

public sealed class PowerSample
{
    public DateTimeOffset Timestamp { get; init; }
    public float CpuLoad { get; init; }
    public float GpuLoad { get; init; }
    public float BatteryDischargeWatts { get; init; }
    public int BatteryPercent { get; init; }
    public string CpuName { get; init; } = "";
    public string GpuName { get; init; } = "";
}

public sealed class PowerMonitorService
{
    private string? _cpuName;
    private string? _gpuName;
    private bool _namesLoaded;
    private static bool s_rootWmiAvailable = true;

    public async Task<PowerSample> ReadAsync()
    {
        var cpuLoadTask = Task.Run(ReadCpuLoad);
        var gpuLoadTask = Task.Run(ReadGpuLoad);
        var batTask = Task.Run(ReadBattery);
        var nameTask = Task.Run(LoadNamesOnce);

        await Task.WhenAll(cpuLoadTask, gpuLoadTask, batTask, nameTask);

        return new PowerSample
        {
            Timestamp = DateTimeOffset.Now,
            CpuLoad = cpuLoadTask.Result,
            GpuLoad = gpuLoadTask.Result,
            BatteryDischargeWatts = batTask.Result.watts,
            BatteryPercent = batTask.Result.percent,
            CpuName = _cpuName ?? "未知 CPU",
            GpuName = _gpuName ?? "未知 GPU"
        };
    }

    public PowerSample Read()
    {
        return ReadAsync().GetAwaiter().GetResult();
    }

    private void LoadNamesOnce()
    {
        if (_namesLoaded) return;
        _cpuName ??= ReadCpuName();
        _gpuName ??= ReadGpuName();
        _namesLoaded = true;
    }

    private static float ReadCpuLoad()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["LoadPercentage"] is not null && float.TryParse(obj["LoadPercentage"].ToString(), out var v))
                    return v;
            }
        }
        catch { }
        return 0;
    }

    private static float ReadGpuLoad()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LoadPercentage FROM Win32_VideoController WHERE LoadPercentage IS NOT NULL");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["LoadPercentage"] is not null && float.TryParse(obj["LoadPercentage"].ToString(), out var v))
                    return v;
            }
        }
        catch { }
        return 0;
    }

    private static (float watts, int percent) ReadBattery()
    {
        var sysStatus = GetSystemPowerStatus();
        if (sysStatus.BatteryFlag == 0x80)
            return (0, -1);

        int percent = -1;
        long dischargeMw = 0;

        if (sysStatus.BatteryLifePercent != 0xFF)
            percent = (int)Math.Round(sysStatus.BatteryLifePercent / 255.0 * 100);

        if (s_rootWmiAvailable)
        {
            try
            {
                var scope = new ManagementScope(@"root\wmi");
                scope.Connect();
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DischargeRate FROM BatteryStatus"));
                foreach (ManagementObject obj in searcher.Get())
                {
                    var dr = obj["DischargeRate"];
                    if (dr is not null && Convert.ToInt64(dr) > 0)
                        dischargeMw = Convert.ToInt64(dr);
                    break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PowerMonitor] root/wmi BatteryStatus failed: {ex.Message}");
                s_rootWmiAvailable = false;
            }
        }

        if (dischargeMw == 0)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DischargeRate FROM Win32_Battery");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["DischargeRate"] is not null)
                        dischargeMw = Convert.ToInt64(obj["DischargeRate"]);
                    break;
                }
            }
            catch { }
        }

        if (percent < 0) return (0, -1);
        return (Math.Max(0, (float)Math.Round(dischargeMw / 1000.0, 1)), percent);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    private static SYSTEM_POWER_STATUS GetSystemPowerStatus()
    {
        GetSystemPowerStatus(out var status);
        return status;
    }

    private static string ReadCpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch { }
        return "未知 CPU";
    }

    private static string ReadGpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch { }
        return "未知 GPU";
    }
}
