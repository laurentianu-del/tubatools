namespace TubaWinUi3.Services;

public sealed class ViVeResult
{
	public bool Success { get; init; }
	public string? ErrorMessage { get; init; }

	public static ViVeResult Ok()
	{
		return new ViVeResult
		{
			Success = true
		};
	}

	public static ViVeResult Fail(string message)
	{
		return new ViVeResult
		{
			Success = false,
			ErrorMessage = message
		};
	}
}
