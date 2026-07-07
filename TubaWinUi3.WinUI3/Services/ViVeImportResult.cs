namespace TubaWinUi3.Services;

public sealed class ViVeImportResult
{
	public bool Success { get; init; }
	public string? ErrorMessage { get; init; }
	public int RuntimeCount { get; init; }
	public int BootCount { get; init; }
}
