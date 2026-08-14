using TubaWinUi3.Services.RogueCleaner;

namespace TubaWinUi3.Tests.RogueCleaner;

/// <summary>「流氓软件的克星」总览页筛选谓词测试（列表底部「展示全部」显示被隐藏条目）。</summary>
public class RogueCleanerViewFilterTests
{
    [Fact]
    public void MatchesStartupTab_OnlyMatchesStartupRelatedCategories()
    {
        Assert.True(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = "启动项" }));
        Assert.True(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = "计划任务" }));
        Assert.True(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = "后台服务" }));
        Assert.False(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = "右键菜单" }));
        Assert.False(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = "文件关联" }));
        Assert.False(RogueCleanerViewFilters.MatchesStartupTab(new Finding { Category = null }));
    }

    [Fact]
    public void MatchesPopupTab_OnlyMatchesPopupRelatedCategories()
    {
        Assert.True(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "正在运行" }));
        Assert.True(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "弹窗" }));
        Assert.True(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "守护进程" }));
        Assert.True(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "捆绑安装" }));
        Assert.False(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "右键菜单" }));
        Assert.False(RogueCleanerViewFilters.MatchesPopupTab(new Finding { Category = "" }));
    }

    [Fact]
    public void MatchesMainMenuList_ShowsResolvedThirdPartyAndUserAdded()
    {
        Assert.True(RogueCleanerViewFilters.MatchesMainMenuList(new ContextMenuEntry
        {
            AdvancedOnly = false,
            PresentationResolved = true,
            IsThirdParty = true
        }));
        Assert.True(RogueCleanerViewFilters.MatchesMainMenuList(new ContextMenuEntry
        {
            AdvancedOnly = false,
            PresentationResolved = true,
            UserAdded = true
        }));
    }

    [Fact]
    public void MatchesMainMenuList_HidesSystemUnresolvedAndAdvancedOnly()
    {
        Assert.False(RogueCleanerViewFilters.MatchesMainMenuList(new ContextMenuEntry
        {
            AdvancedOnly = false,
            PresentationResolved = true,
            IsThirdParty = false
        }));
        Assert.False(RogueCleanerViewFilters.MatchesMainMenuList(new ContextMenuEntry
        {
            AdvancedOnly = false,
            PresentationResolved = false,
            IsThirdParty = true
        }));
        Assert.False(RogueCleanerViewFilters.MatchesMainMenuList(new ContextMenuEntry
        {
            AdvancedOnly = true,
            PresentationResolved = true,
            IsThirdParty = true
        }));
    }
}
