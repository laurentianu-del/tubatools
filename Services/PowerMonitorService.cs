using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace TubaWinUi3.Services;

public sealed class PowerSample
{
    public DateTimeOffset Timestamp { get; init; }
    public float CpuLoad { get; init; }
    public float GpuLoad { get; init; }
    public float BatteryDischargeWatts { get; init; }
    public float BatteryChargeWatts { get; init; }
    public int BatteryPercent { get; init; }
    public bool IsCharging { get; init; }
    public string CpuName { get; init; } = "";
    public string GpuName { get; init; } = "";
}

public sealed class PowerMonitorService
{
    private static Computer? s_computer;
    private static readonly object s_computerLock = new();
    private static bool s_computerInitDone;
    private static bool s_rootWmiAvailable = true;

    private string? _cpuName;
    private string? _gpuName;
    private bool _namesLoaded;

    public PowerMonitorService()
    {
        EnsureComputerInit();
    }

    private static void EnsureComputerInit()
    {
        lock (s_computerLock)
        {
            if (s_computerInitDone) return;
            s_computerInitDone = true;

            try
            {
                s_computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsBatteryEnabled = true,
                    IsMotherboardEnabled = true
                };
                s_computer.Open();
            }
            catch { s_computer = null; }
        }
    }

    public async Task<PowerSample> ReadAsync()
    {
        var cpuLoadTask = Task.Run(ReadCpuLoad);
        var gpuLoadTask = Task.Run(ReadGpuLoad);
        var batTask = Task.Run(ReadBatteryWmi);
        var nameTask = Task.Run(LoadNamesOnce);
        var lhmTask = Task.Run(ReadFromLibreHardware);

        await Task.WhenAll(cpuLoadTask, gpuLoadTask, batTask, nameTask, lhmTask);

        var lhm = lhmTask.Result;

        float batDischarge = lhm.BatteryDischargeWatts;
        float batCharge = lhm.BatteryChargeWatts;
        int batPercent = lhm.BatteryPercent;
        bool isCharging = lhm.IsCharging;

        if (batPercent < 0)
        {
            batDischarge = batTask.Result.watts;
            batPercent = batTask.Result.percent;
            isCharging = batDischarge <= 0 && batPercent >= 0;
        }

        return new PowerSample
        {
            Timestamp = DateTimeOffset.Now,
            CpuLoad = lhm.CpuLoad >= 0 ? lhm.CpuLoad : cpuLoadTask.Result,
            GpuLoad = lhm.GpuLoad >= 0 ? lhm.GpuLoad : gpuLoadTask.Result,
            BatteryDischargeWatts = batDischarge,
            BatteryChargeWatts = batCharge,
            BatteryPercent = batPercent,
            IsCharging = isCharging,
            CpuName = _cpuName ?? lhm.CpuName ?? "未知 CPU",
            GpuName = _gpuName ?? lhm.GpuName ?? "未知 GPU"
        };
    }

    public PowerSample Read()
    {
        return ReadAsync().GetAwaiter().GetResult();
    }

    private (float CpuLoad, float GpuLoad, float BatteryDischargeWatts, float BatteryChargeWatts,
            int BatteryPercent, bool IsCharging, string? CpuName, string? GpuName) ReadFromLibreHardware()
    {
        float cpuLoad = -1, gpuLoad = -1;
        float batDischarge = 0, batCharge = 0;
        int batPercent = -1;
        bool isCharging = false;
        string? cpuName = null, gpuName = null;

        lock (s_computerLock)
        {
            if (s_computer == null)
                return (cpuLoad, gpuLoad, batDischarge, batCharge, batPercent, isCharging, cpuName, gpuName);

            try
            {
                foreach (IHardware hardware in s_computer.Hardware)
                    hardware.Update();

                foreach (IHardware hardware in s_computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        cpuName = hardware.Name;
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (!sensor.Value.HasValue) continue;

                            if (sensor.SensorType == SensorType.Load &&
                                sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                                cpuLoad = sensor.Value.Value;
                        }
                    }
                    else if (hardware.HardwareType == HardwareType.GpuNvidia ||
                             hardware.HardwareType == HardwareType.GpuAmd ||
                             hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        gpuName = hardware.Name;
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (!sensor.Value.HasValue) continue;

                            if (sensor.SensorType == SensorType.Load && gpuLoad < 0)
                                gpuLoad = sensor.Value.Value;
                        }
                    }
                    else if (hardware.HardwareType == HardwareType.Battery)
                    {
                        float batRate = 0, batVoltage = 0, batCurrent = 0;

                        foreach (var sensor in hardware.Sensors)
                        {
                            if (!sensor.Value.HasValue) continue;

                            if (sensor.SensorType == SensorType.Level &&
                                sensor.Name.Contains("Charge Level", StringComparison.OrdinalIgnoreCase))
                                batPercent = (int)Math.Round(sensor.Value.Value);

                            if (sensor.SensorType == SensorType.Power &&
                                sensor.Name.Contains("Rate", StringComparison.OrdinalIgnoreCase))
                                batRate = sensor.Value.Value;

                            if (sensor.SensorType == SensorType.Voltage)
                                batVoltage = sensor.Value.Value;

                            if (sensor.SensorType == SensorType.Current &&
                                sensor.Name.Contains("Current", StringComparison.OrdinalIgnoreCase))
                                batCurrent = sensor.Value.Value;
                        }

                        if (batRate != 0)
                        {
                            if (batRate > 0) { batCharge = batRate; isCharging = true; }
                            else { batDischarge = -batRate; isCharging = false; }
                        }
                        else if (batCurrent != 0 && batVoltage != 0)
                        {
                            var calculated = Math.Abs(batCurrent * batVoltage);
                            if (batCurrent > 0) { batCharge = calculated; isCharging = true; }
                            else { batDischarge = calculated; isCharging = false; }
                        }
                    }
                }
            }
            catch { }
        }

        return (cpuLoad, gpuLoad, batDischarge, batCharge, batPercent, isCharging, cpuName, gpuName);
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

    private static (float watts, int percent) ReadBatteryWmi()
    {
        var sysStatus = GetSystemPowerStatus();
        if (sysStatus.BatteryFlag == 0x80)
            return (0, -1);

        int percent = -1;
        long dischargeMw = 0;
        long chargeMw = 0;

        if (sysStatus.BatteryLifePercent != 0xFF)
            percent = (int)Math.Round(sysStatus.BatteryLifePercent / 255.0 * 100);

        bool acOnline = sysStatus.ACLineStatus == 1;

        if (s_rootWmiAvailable)
        {
            try
            {
                var scope = new ManagementScope(@"root\wmi");
                scope.Connect();
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DischargeRate, ChargeRate FROM BatteryStatus"));
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["DischargeRate"] is not null)
                        dischargeMw = Convert.ToInt64(obj["DischargeRate"]);
                    if (obj["ChargeRate"] is not null)
                        chargeMw = Convert.ToInt64(obj["ChargeRate"]);
                    break;
                }
            }
            catch { s_rootWmiAvailable = false; }
        }

        if (dischargeMw == 0 && chargeMw == 0)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT DischargeRate, ChargeRate FROM Win32_Battery");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["DischargeRate"] is not null)
                        dischargeMw = Convert.ToInt64(obj["DischargeRate"]);
                    if (obj["ChargeRate"] is not null)
                        chargeMw = Convert.ToInt64(obj["ChargeRate"]);
                    break;
                }
            }
            catch { }
        }

        if (percent < 0) return (0, -1);

        float watts = 0;
        if (dischargeMw > 0)
            watts = (float)Math.Round(dischargeMw / 1000.0, 1);
        else if (chargeMw > 0)
            watts = -(float)Math.Round(chargeMw / 1000.0, 1);

        return (watts, percent);
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
