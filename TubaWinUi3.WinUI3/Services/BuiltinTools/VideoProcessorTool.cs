namespace TubaWinUi3.Services;

public sealed class VideoProcessorTool : IBuiltinTool
{
    public string Id => "video-processor";
    public string Name => "视频处理";
    public string Description => "全能视频处理工具，支持格式转换、压缩、裁剪、合并、提取音频等，基于 FFmpeg。";
    public string Glyph => "\uE8B2";
    public string Category => "媒体工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        context.OnProgress?.Invoke("正在打开视频处理...");

        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            var window = new TubaWinUi3.Pages.VideoProcessorWindow();
            window.Activate();
        });

        return Task.CompletedTask;
    }
}
