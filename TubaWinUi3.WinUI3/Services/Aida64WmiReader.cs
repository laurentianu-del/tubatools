using System.Globalization;
using System.Management;

namespace TubaWinUi3.Services;

public sealed class Aida64WmiReader
{
    public (Aida64Data? Data, string? Error) Read()
    {
        try
        {
            var scope = new ManagementScope(@"root\WMI");
            scope.Connect();

            var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM AIDA64_SensorValues"));

            var data = new Aida64Data();

            foreach (ManagementObject obj in searcher.Get())
            {
                var id = obj["ID"]?.ToString();
                var val = obj["Value"]?.ToString();
                if (id is null || val is null) continue;

                switch (id)
                {
                    case "SCPUUTI": data.CpuUsage = ParseDouble(val); break;
                    case "SCPUCLK": data.CpuClock = ParseDouble(val); break;
                    case "TCPUPKG": data.CpuTemp = ParseDouble(val); break;
                    case "PCPUPKG": data.CpuPower = ParseDouble(val); break;
                    case "PCPUIAC": if (data.CpuPower < 0) data.CpuPower = ParseDouble(val); break;
                    case "SGPU1CLK": data.GpuClock = ParseDouble(val); break;
                    case "TCPUGTC": data.GpuTemp = ParseDouble(val); break;
                    case "PCPUGTC": data.GpuPower = ParseDouble(val); break;
                    case "SMEMUTI": data.MemUsage = ParseDouble(val); break;
                }

                obj.Dispose();
            }

            if (data.CpuTemp < 0)
            {
                scope = new ManagementScope(@"root\WMI");
                scope.Connect();
                searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM AIDA64_SensorValues WHERE ID='TCPU'"));
                foreach (ManagementObject obj in searcher.Get())
                {
                    var val = obj["Value"]?.ToString();
                    if (val is not null) data.CpuTemp = ParseDouble(val);
                    obj.Dispose();
                }
            }

            return (data, null);
        }
        catch (Exception ex)
        {
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static double ParseDouble(string s)
    {
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return v;
        return -1;
    }
}

public sealed class Aida64Data
{
    public double CpuUsage = -1;
    public double CpuClock = -1;
    public double CpuTemp = -1;
    public double CpuPower = -1;
    public double GpuUsage = -1;
    public double GpuTemp = -1;
    public double GpuPower = -1;
    public double GpuClock = -1;
    public double MemUsage = -1;
}
