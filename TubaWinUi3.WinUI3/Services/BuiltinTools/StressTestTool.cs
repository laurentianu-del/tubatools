using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;

namespace TubaWinUi3.Services;

public sealed class StressTestTool : IBuiltinTool
{
    public string Id => "stress-test";
    public string Name => "一键双烤";
    public string Description => "CPU / GPU 压力测试工具，支持一键双烤、CPU 单烤、GPU 单烤，实时监控温度、频率与功耗。";
    public string Glyph => "\uECAD";
    public string Category => "硬件信息";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        context.OnProgress?.Invoke("正在打开一键双烤...");

        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            var window = new StressTestWindow();
            window.Activate();
        });

        return Task.CompletedTask;
    }
}
