using Microsoft.UI.Xaml;

namespace TubaWinUi3.Services;

public sealed class WindowsActivationTool : IBuiltinTool
{
    public string Id => "windows-activation";
    public string Name => "KMS 激活";
    public string Description => "打开 KMS 服务器监控页面，查看全球 KMS 服务器状态与可用性，获取激活所需的服务器地址。";
    public string Glyph => "\uE895";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    private const string KmsPageUrl = "https://monitor.yerong.org/kms/";

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        TubaWinUi3.Pages.BrowserWindow.Open(KmsPageUrl, "KMS 服务器监控");
        return Task.CompletedTask;
    }
}
