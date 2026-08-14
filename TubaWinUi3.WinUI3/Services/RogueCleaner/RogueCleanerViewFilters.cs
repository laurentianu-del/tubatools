using System;
using System.Linq;

namespace TubaWinUi3.Services.RogueCleaner;

/// <summary>「流氓软件的克星」各总览页的列表筛选谓词;页面与单元测试共用。</summary>
internal static class RogueCleanerViewFilters
{
    /// <summary>启动项管理 tab:只显示启动相关类别(启动/计划任务/后台服务)。</summary>
    public static bool MatchesStartupTab(Finding f)
        => ContainsAny(f.Category, "启动", "计划任务", "后台服务");

    /// <summary>弹窗/流氓诊断 tab:只显示弹窗与守护相关类别(正在运行/弹窗/守护/捆绑)。</summary>
    public static bool MatchesPopupTab(Finding f)
        => ContainsAny(f.Category, "正在运行", "弹窗", "守护", "捆绑");

    /// <summary>右键菜单主列表默认显示:已识别第三方 + 用户自建,排除系统内置、未识别与技术记录。</summary>
    public static bool MatchesMainMenuList(ContextMenuEntry e)
        => !e.AdvancedOnly && e.PresentationResolved && (e.IsThirdParty || e.UserAdded);

    private static bool ContainsAny(string? value, params string[] needles)
        => value != null && needles.Any(n => value.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
}
