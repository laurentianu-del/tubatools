namespace TubaWinUi3.Models;

public sealed class BenchmarkLeaderboardEntry
{
    public int Rank { get; set; }

    public BenchmarkReportEntry Report { get; set; } = new();
}
