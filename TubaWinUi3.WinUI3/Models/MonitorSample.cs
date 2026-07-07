namespace TubaWinUi3.Models;

public sealed class MonitorSample
{
    public float CpuLoad = -1, CpuTemp = -1, CpuClock = -1, CpuPower = -1;
    public string CpuName = "";

    public float GpuLoad = -1, GpuTemp = -1, GpuClock = -1, GpuPower = -1, GpuVramLoad = -1, GpuVramUsedGB = -1;
    public string GpuName = "";

    public float MemLoad = -1, MemUsedGB = -1, MemTotalGB = -1;

    public float DiskReadMBs = -1, DiskWriteMBs = -1, DiskTemp = -1;

    public float NetUpMBs = -1, NetDownMBs = -1;

    public float BatPercent = -1, BatPower = -1;
    public bool BatCharging;

    public float Fps = -1;
    public string FpsProcess = "";
}

public sealed class FpsSnapshot
{
    public string ProcessName = "";
    public double CurrentFps;
    public double AvgFps;
    public double MinFps;
    public double MaxFps;
    public double OnePercentLow;
    public double PointOnePercentLow;
    public int TotalFrames;
    public double TotalSeconds;
    public List<double> FrameTimes = [];
}