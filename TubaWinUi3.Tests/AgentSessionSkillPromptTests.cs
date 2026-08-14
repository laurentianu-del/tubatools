using System.Reflection;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

/// <summary>
/// 验证技能注入链路：注册技能 → 创建会话 → 系统提示词必须包含「已加载技能」段。
/// 防止"技能没生效"类回归（模型看不到技能指导）。
/// </summary>
[Collection("AgentSkillRegistry")]
public class AgentSessionSkillPromptTests
{
    private static readonly FieldInfo SkillsField =
        typeof(AgentSkillRegistry).GetField("_skills", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo HistoryField =
        typeof(AgentSession).GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static void ClearRegistry()
        => ((List<AgentSkill>)SkillsField.GetValue(null)!).Clear();

    [Fact]
    public void CreateNew_SystemPrompt_IncludesActiveSkillContext()
    {
        ClearRegistry();
        AgentSkillRegistry.RegisterDefaults();

        var session = AgentSession.CreateNew();
        var history = (List<ChatMessage>)HistoryField.GetValue(session)!;

        Assert.True(history.Count > 0 && history[0].Role == ChatRole.System, "系统提示词应为首条消息");
        var system = history[0].Text ?? "";

        // 技能段已注入
        Assert.Contains("## 已加载技能", system);
        Assert.Contains("电脑选购", system);
        Assert.Contains("browser_navigate", system);
        Assert.Contains("browser_wait_for_login", system);
        Assert.Contains("search.jd.com", system);
        // 技能段紧跟主提示词（位置在工具箱 CLI 索引标题之前，保证模型优先注意）
        var skillsIdx = system.IndexOf("## 已加载技能", StringComparison.Ordinal);
        var indexIdx = system.IndexOf("## 工具箱命令行工具（以下工具", StringComparison.Ordinal);
        Assert.True(skillsIdx < indexIdx,
            $"技能段位置异常：skills@{skillsIdx} index@{indexIdx}\n系统提示词开头：\n{system[..Math.Min(600, system.Length)]}");

        // 系统上下文含当前时间（AI 需要知道现在的年月，避免用过时价格/知识）
        Assert.Contains(DateTime.Now.Year.ToString(), system);
        Assert.Contains("当前时间：", system);
        session.Dispose();
    }

    [Fact]
    public void SetSkillEnabled_Off_RemovesSkillFromSystemPrompt()
    {
        ClearRegistry();
        AgentSkillRegistry.RegisterDefaults();

        var session = AgentSession.CreateNew();
        session.SetSkillEnabled(PcBuildSkill.Id, false);

        var history = (List<ChatMessage>)HistoryField.GetValue(session)!;
        var system = history[0].Text ?? "";

        Assert.DoesNotContain("## 已加载技能", system);
        Assert.DoesNotContain("电脑选购", system);
        session.Dispose();
    }
}
