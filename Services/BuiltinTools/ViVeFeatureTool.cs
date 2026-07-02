using System.Threading.Tasks;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class ViVeFeatureTool : IBuiltinTool
{
	public string Id => "vive-feature";
	public string Name => "Windows 功能开关 (ViVeGUI)";
	public string Description => "管理 Windows A/B 实验性功能开关，启用/禁用/重置隐藏功能，支持导出导入配置。";
	public string Glyph => "\ue945";
	public string Category => "系统工具";
	public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

	public Task ExecuteAsync(BuiltinToolContext context)
	{
		context.OnProgress?.Invoke("正在打开 Windows 功能开关...");
		App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
		{
			new ViVeFeatureWindow().Activate();
		});
		return Task.CompletedTask;
	}
}
