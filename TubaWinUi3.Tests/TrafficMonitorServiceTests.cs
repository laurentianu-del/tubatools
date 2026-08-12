using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class TrafficMonitorServiceTests
{
    private static TrafficConnectionInfo Conn(string key, long speedIn, long speedOut)
        => new() { Key = key, SpeedIn = speedIn, SpeedOut = speedOut };

    [Fact]
    public void SortAndCap_OrdersByActivityDescending_AndCaps()
    {
        var list = new[]
        {
            Conn("a", 10, 0),
            Conn("b", 0, 5),
            Conn("c", 1, 100),
            Conn("d", 0, 0)
        };

        var result = TrafficMonitorService.SortAndCap(list, 2);

        Assert.Equal(["c", "a"], result.Select(x => x.Key));
    }

    [Fact]
    public void SortAndCap_TieBreak_ByKey()
    {
        var list = new[] { Conn("b", 5, 5), Conn("a", 5, 5) };

        var result = TrafficMonitorService.SortAndCap(list, 10);

        Assert.Equal(["a", "b"], result.Select(x => x.Key));
    }

    [Fact]
    public void Recorder_Add_KeepsLatestAndCaps()
    {
        var recorder = new TrafficSnapshotRecorder();
        var start = new DateTime(2026, 1, 1, 0, 0, 0);

        for (int i = 0; i < TrafficSnapshotRecorder.MaxSnapshots + 50; i++)
            recorder.Add(new TrafficSnapshot { Time = start.AddSeconds(i) });

        Assert.Equal(TrafficSnapshotRecorder.MaxSnapshots, recorder.Count);
        // 最旧 50 条被丢弃：第一条应为第 51 条
        Assert.True(recorder.TryGet(0, out var first));
        Assert.Equal(start.AddSeconds(50), first.Time);
        // 最新一条保留
        Assert.NotNull(recorder.Latest);
        Assert.Equal(start.AddSeconds(TrafficSnapshotRecorder.MaxSnapshots + 49), recorder.Latest!.Time);
    }

    [Fact]
    public void Recorder_TryGet_OutOfRange_ReturnsFalse()
    {
        var recorder = new TrafficSnapshotRecorder();
        recorder.Add(new TrafficSnapshot { Time = DateTime.Now });

        Assert.False(recorder.TryGet(-1, out _));
        Assert.False(recorder.TryGet(1, out _));
        Assert.True(recorder.TryGet(0, out _));
    }

    [Fact]
    public void Recorder_Clear_Empties()
    {
        var recorder = new TrafficSnapshotRecorder();
        recorder.Add(new TrafficSnapshot { Time = DateTime.Now });

        recorder.Clear();

        Assert.Equal(0, recorder.Count);
        Assert.Null(recorder.Latest);
        Assert.False(recorder.TryGet(0, out _));
    }
}
