namespace TubaWinUi3.Services;

public sealed class SpeedTestTool : IBuiltinTool
{
    public string Id => "speed-test";
    public string Name => "网速测试";
    public string Description => "在线测试网络下载和上传速度。";
    public string Glyph => "\uEB3E";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    private const string SpeedTestUrl = "https://test.ustc.edu.cn/";

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        TubaWinUi3.Pages.BrowserWindow.Open(SpeedTestUrl, "网速测试");
        return Task.CompletedTask;
    }
}
