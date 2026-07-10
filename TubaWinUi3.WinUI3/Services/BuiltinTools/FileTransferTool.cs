namespace TubaWinUi3.Services;

public sealed class FileTransferTool : IBuiltinTool
{
    public string Id => "file-transfer";
    public string Name => "文件传输助手";
    public string Description => "局域网/P2P文件传输，支持群组多设备互传，优先走局域网直连。";
    public string Glyph => "\uE8AB";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        context.OnProgress?.Invoke("正在打开文件传输助手...");

        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            var window = new TubaWinUi3.Pages.FileTransferWindow();
            window.Activate();
        });

        return Task.CompletedTask;
    }
}
