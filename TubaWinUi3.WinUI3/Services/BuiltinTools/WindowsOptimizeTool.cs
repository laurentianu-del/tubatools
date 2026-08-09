using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class WindowsOptimizeTool : IBuiltinTool
{
    public string Id => "windows-optimize";
    public string Name => "Windows常规优化";
    public string Description => "系统性能、隐私、网络、游戏与清理优化，一键应用推荐方案";
    public string Glyph => "\uE90F";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(WindowsOptimizePage));
        return Task.CompletedTask;
    }
}
