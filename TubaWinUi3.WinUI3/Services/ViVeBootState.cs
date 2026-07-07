using TubaWinUi3.Services.ViVe;

namespace TubaWinUi3.Services;

public sealed class ViVeBootState
{
	public bool Success { get; init; }
	public BSD_FEATURE_CONFIGURATION_STATE State { get; init; }
	public string? ErrorMessage { get; init; }

	public string StateLabel => State switch
	{
		BSD_FEATURE_CONFIGURATION_STATE.Uninitialized => "未初始化",
		BSD_FEATURE_CONFIGURATION_STATE.BootPending => "等待重启",
		BSD_FEATURE_CONFIGURATION_STATE.LKGPending => "LKG 待处理",
		BSD_FEATURE_CONFIGURATION_STATE.RollbackPending => "回滚待处理",
		BSD_FEATURE_CONFIGURATION_STATE.Committed => "已提交",
		_ => State.ToString(),
	};
}
