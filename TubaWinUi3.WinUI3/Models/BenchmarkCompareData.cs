namespace TubaWinUi3.Models;

public sealed class BenchmarkCompareData
{
    public BenchmarkReportEntry MyReport { get; set; } = new();

    public BenchmarkReportEntry OtherReport { get; set; } = new();
}
