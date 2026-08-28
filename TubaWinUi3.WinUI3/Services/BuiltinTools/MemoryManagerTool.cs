namespace TubaWinUi3.Services;

public sealed class MemoryManagerTool : IBuiltinTool
{
    public string Id => "memory-manager";
    public string Name => "内存管理";
    public string Description => "RAMMap 的 WinUI 优化版: 使用量/进程/优先级/物理内存/文件缓存五大分析面板, 一键清理待机内存、收紧工作集 (由 Sysinternals RAMMap 命令行驱动), 定时自动清理, 支持查看与设置虚拟内存 (分页文件)。";
    public string Glyph => "\uE963";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.MemoryManagerPage));
        return Task.CompletedTask;
    }
}
