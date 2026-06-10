using Microsoft.UI.Xaml;

namespace TubaWinUi3.Services;

public sealed class HardwareSpooferTool : IBuiltinTool
{
    public string Id => "hardware-spoofer";
    public string Name => "配置修改器";
    public string Description => "修改注册表中的 CPU、GPU、系统等硬件信息显示，支持一键恢复原始配置。";
    public string Glyph => "";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        context.OnProgress?.Invoke("正在打开配置修改器...");

        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            var window = new TubaWinUi3.Pages.HardwareSpooferWindow();
            window.Activate();
        });

        return Task.CompletedTask;
    }
}
