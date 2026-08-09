using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class CommonSoftwareTool : IBuiltinTool
{
    public string Id => "common-software";
    public string Name => "常用软件";
    public string Description => "精选常用软件目录，基于 winget 一键批量安装";
    public string Glyph => "\uE774";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(CommonSoftwarePage));
        return Task.CompletedTask;
    }
}
