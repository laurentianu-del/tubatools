using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>
/// 主动拦截（流氓软件拦截器审核）：跳转「流氓软件的克星」页面并定位到主动拦截分区，
/// 查看被后端自动屏蔽的新增右键项，可放行/重新屏蔽/删除记录。
/// </summary>
public sealed class ActiveInterceptTool : IBuiltinTool
{
    public string Id => "active-intercept";
    public string Name => "主动拦截";
    public string Description => "后台常驻拦截新增的第三方右键菜单（先拦截再审核），可审核放行被拦截项（NativeAOT 后端，最小占用）";
    public string Glyph => "\uE899";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(RogueCleanerPage), "activeintercept");
        return Task.CompletedTask;
    }
}
