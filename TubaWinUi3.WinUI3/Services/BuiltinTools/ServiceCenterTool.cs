namespace TubaWinUi3.Services;

public sealed class ServiceCenterTool : IBuiltinTool
{
    public string Id => "service-center";
    public string Name => "服务网点查询";
    public string Description => "查询各大品牌笔记本、台式机官方服务网点地址。";
    public string Glyph => "\uE80F";
    public string Category => "实用工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        context.OnProgress?.Invoke("正在打开服务网点查询...");

        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            var window = new TubaWinUi3.Pages.ServiceCenterWindow();
            window.Activate();
        });

        return Task.CompletedTask;
    }
}