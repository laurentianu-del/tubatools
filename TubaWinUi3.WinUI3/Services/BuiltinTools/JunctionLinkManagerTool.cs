namespace TubaWinUi3.Services;

public sealed class JunctionLinkManagerTool : IBuiltinTool
{
    public string Id => "junction-manager";
    public string Name => "超链接管理器";
    public string Description => "把桌面/下载/文档等用户文件夹重定向到其他盘，原位置自动创建超链接（Junction），可选迁移原文件，支持一键还原。";
    public string Glyph => "\uE71B";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.JunctionLinkManagerPage));
        return Task.CompletedTask;
    }
}