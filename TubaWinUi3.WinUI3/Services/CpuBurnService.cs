using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class CpuBurnService
{
    private static CancellationTokenSource? _cts;
    private static volatile bool _running;
    private static readonly List<BurnSample> _samples = [];
    private static readonly object _sampleLock = new();

    public static bool IsRunning => _running;
    public static List<BurnSample> GetSamples() { lock (_sampleLock) return [.. _samples]; }

    public static async Task RunBurnAsync(TimeSpan duration, IProgress<BurnSample>? progress, CancellationToken ct)
    {
        if (_running) return;
        _running = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_sampleLock) _samples.Clear();

        var coreCount = Environment.ProcessorCount;
        var workers = new Task[coreCount];
        var workerCts = new CancellationTokenSource();

        for (var i = 0; i < coreCount; i++)
        {
            var coreIdx = i;
            workers[i] = Task.Run(() =>
            {
                var x = (double)coreIdx;
                while (!workerCts.Token.IsCancellationRequested)
                {
                    x = Math.Sin(x) * Math.Cos(x) + Math.Sqrt(Math.Abs(x) + 1);
                    x = Math.Tanh(x) * Math.Log(Math.Abs(x) + 1);
                    for (var j = 0; j < 100; j++)
                    {
                        x = Math.Sin(x + j) * Math.Cos(x - j);
                    }
                }
            }, workerCts.Token);
        }

        var monitor = LiteMonitorService.Instance;
        monitor.EnsureInit();

        var startTime = DateTime.Now;
        try
        {
            while (DateTime.Now - startTime < duration && !ct.IsCancellationRequested)
            {
                var sample = await Task.Run(() =>
                {
                    var s = monitor.Read(false);
                    return new BurnSample
                    {
                        Time = DateTime.Now,
                        Temp = s.CpuTemp,
                        Power = s.CpuPower,
                        Clock = s.CpuClock,
                        Load = s.CpuLoad
                    };
                }, ct);

                lock (_sampleLock) _samples.Add(sample);
                progress?.Report(sample);
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            workerCts.Cancel();
            try { await Task.WhenAll(workers); } catch { }
            _running = false;
            _cts = null;
        }
    }

    public static void Stop()
    {
        _cts?.Cancel();
    }
}
