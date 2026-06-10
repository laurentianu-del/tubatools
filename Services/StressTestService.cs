using System.Diagnostics;

namespace TubaWinUi3.Services;

public sealed class StressTestService
{
    private CancellationTokenSource? _cpuCts;
    private Task[] _cpuTasks = [];
    private volatile bool _cpuRunning;

    public bool IsCpuRunning => _cpuRunning;

    public void StartCpuStress(int? threadCount = null)
    {
        StopCpuStress();
        _cpuCts = new CancellationTokenSource();
        var token = _cpuCts.Token;
        var threads = threadCount ?? Environment.ProcessorCount;
        _cpuRunning = true;
        _cpuTasks = new Task[threads];

        for (int i = 0; i < threads; i++)
        {
            _cpuTasks[i] = Task.Run(() => CpuWorker(token), token);
        }
    }

    public void StopCpuStress()
    {
        _cpuCts?.Cancel();
        _cpuCts?.Dispose();
        _cpuCts = null;
        _cpuRunning = false;
        try { Task.WaitAll(_cpuTasks, 500); } catch { }
        _cpuTasks = [];
    }

    private static void CpuWorker(CancellationToken token)
    {
        var size = 256;
        var a = new double[size * size];
        var b = new double[size * size];
        var c = new double[size * size];

        var rng = new Random();
        for (int i = 0; i < a.Length; i++)
        {
            a[i] = rng.NextDouble();
            b[i] = rng.NextDouble();
        }

        while (!token.IsCancellationRequested)
        {
            for (int i = 0; i < size; i++)
            {
                for (int k = 0; k < size; k++)
                {
                    var aik = a[i * size + k];
                    for (int j = 0; j < size; j++)
                    {
                        c[i * size + j] += aik * b[k * size + j];
                    }
                }
            }

            for (int i = 0; i < c.Length; i++)
                c[i] = 0;
        }
    }
}
