using System.Text.Json;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Agent;
using TubaWinUi3.Services.Ai;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// Agent 工具循环护栏测试：重复调用拦截（防模型对同一操作反复调用陷入死循环）与空结果标记。
/// </summary>
public class AgentToolLoopGuardTests
{
    private static AgentToolAdapter MakeAdapter(string name, Delegate func)
    {
        var fn = AIFunctionFactory.Create(func, new AIFunctionFactoryOptions { Name = name });
        return new AgentToolAdapter(new AgentTool
        {
            Name = name,
            DisplayName = name,
            Glyph = "\uE73E",
            Function = fn
        });
    }

    /// <summary>核心：相同工具 + 相同参数的第二次调用被拦截，底层函数只执行一次。</summary>
    [Fact]
    public async Task SameToolAndArgs_SecondCall_IsBlockedAndNotExecuted()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        var adapter = MakeAdapter("web_search",
            (Func<string, string>)(query => { calls++; return $"结果:{query}"; }));

        using var doc = JsonDocument.Parse("""{"query":"显卡价格"}""");
        var args = doc.RootElement;

        var first = await adapter.ExecuteAsync(args);
        var second = await adapter.ExecuteAsync(args);

        Assert.Contains("结果:显卡价格", first);
        Assert.Contains("重复调用已拦截", second);
        Assert.Equal(1, calls); // 第二次未真正执行
    }

    /// <summary>参数键顺序不同视为同一签名（{"a":1,"b":2} 与 {"b":2,"a":1} 相同）。</summary>
    [Fact]
    public async Task ReorderedArgsKeys_TreatedAsSameSignature()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        var adapter = MakeAdapter("launch_tool",
            (Func<string, string>)(toolName => { calls++; return $"启动:{toolName}"; }));

        using var doc1 = JsonDocument.Parse("""{"toolName":"快速验机"}""");
        using var doc2 = JsonDocument.Parse("""{"toolName":"快速验机"}""");

        await adapter.ExecuteAsync(doc1.RootElement);
        var second = await adapter.ExecuteAsync(doc2.RootElement);

        Assert.Contains("重复调用已拦截", second);
        Assert.Equal(1, calls);
    }

    /// <summary>参数不同不拦截（同一工具不同用途的合法调用）。</summary>
    [Fact]
    public async Task DifferentArgs_AllowsExecution()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        var adapter = MakeAdapter("browser_navigate",
            (Func<string, string>)(url => { calls++; return $"已打开 {url}"; }));

        using var doc1 = JsonDocument.Parse("""{"url":"https://a.com"}""");
        using var doc2 = JsonDocument.Parse("""{"url":"https://b.com"}""");

        await adapter.ExecuteAsync(doc1.RootElement);
        await adapter.ExecuteAsync(doc2.RootElement);

        Assert.Equal(2, calls); // 参数不同，两次都执行
    }

    /// <summary>空参数（查询类，如实时温度）豁免去重——允许重复取最新值。</summary>
    [Fact]
    public async Task EmptyArgs_ExemptFromDedup()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        var adapter = MakeAdapter("get_cpu_temperature",
            (Func<string>)(() => { calls++; return $"{30 + calls}℃"; }));

        using var doc = JsonDocument.Parse("{}");
        await adapter.ExecuteAsync(doc.RootElement);
        var second = await adapter.ExecuteAsync(doc.RootElement);

        Assert.DoesNotContain("重复调用已拦截", second);
        Assert.Equal(2, calls); // 两次都真正执行
    }

    /// <summary>空结果标记：工具返回空串时给明确占位，不让模型误判为失败而无限重试。</summary>
    [Fact]
    public async Task EmptyResult_MarkedInsteadOfBlank()
    {
        AgentToolLoopGuard.Reset();
        var adapter = MakeAdapter("silent_tool", (Func<string>)(() => ""));

        using var doc = JsonDocument.Parse("{}");
        var result = await adapter.ExecuteAsync(doc.RootElement);

        Assert.Contains("未返回内容", result);
        Assert.NotEqual("", result);
    }

    /// <summary>Reset 后（新对话边界）同一调用可再次执行。</summary>
    [Fact]
    public async Task Reset_ClearsSeenCalls()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        var adapter = MakeAdapter("web_search",
            (Func<string, string>)(query => { calls++; return $"结果:{query}"; }));

        using var doc = JsonDocument.Parse("""{"query":"显卡价格"}""");
        await adapter.ExecuteAsync(doc.RootElement);
        await adapter.ExecuteAsync(doc.RootElement); // 被拦截
        Assert.Equal(1, calls);

        AgentToolLoopGuard.Reset(); // 新对话
        var afterReset = await adapter.ExecuteAsync(doc.RootElement);

        Assert.Contains("结果:显卡价格", afterReset);
        Assert.Equal(2, calls); // Reset 后重新执行
    }

    // ---------- 无进展检测（连续纯工具轮 → 注入终止指令） ----------

    /// <summary>连续纯工具轮递增，达到阈值后触发终止指令注入；未达阈值不触发。</summary>
    [Fact]
    public void ReportRound_PureToolRounds_IncrementUntilThreshold()
    {
        AgentToolLoopGuard.Reset();
        Assert.False(AgentToolLoopGuard.ShouldInjectStopDirective);

        // 阈值 - 1 轮：仍未触发
        for (var i = 1; i < AgentToolLoopGuard.NoProgressThreshold; i++)
        {
            AgentToolLoopGuard.ReportRound(hadUserText: false, hadToolCalls: true);
            Assert.False(AgentToolLoopGuard.ShouldInjectStopDirective);
        }

        // 第阈值轮：触发
        AgentToolLoopGuard.ReportRound(hadUserText: false, hadToolCalls: true);
        Assert.True(AgentToolLoopGuard.ShouldInjectStopDirective);
        Assert.Equal(AgentToolLoopGuard.NoProgressThreshold, AgentToolLoopGuard.ConsecutiveToolRounds);
    }

    /// <summary>模型产出用户可见文本 = 有进展，计数清零；无工具也无文本的空轮同样清零。</summary>
    [Fact]
    public void ReportRound_UserText_ResetsProgress()
    {
        AgentToolLoopGuard.Reset();
        AgentToolLoopGuard.ReportRound(false, true);
        AgentToolLoopGuard.ReportRound(false, true);
        AgentToolLoopGuard.ReportRound(true, true); // 模型开口了
        Assert.Equal(0, AgentToolLoopGuard.ConsecutiveToolRounds);

        AgentToolLoopGuard.ReportRound(false, false); // 空轮
        Assert.Equal(0, AgentToolLoopGuard.ConsecutiveToolRounds);
    }

    /// <summary>Reset（新对话）清空无进展计数。</summary>
    [Fact]
    public void ReportRound_Reset_ClearsProgress()
    {
        AgentToolLoopGuard.Reset();
        for (var i = 0; i < AgentToolLoopGuard.NoProgressThreshold; i++)
            AgentToolLoopGuard.ReportRound(false, true);
        Assert.True(AgentToolLoopGuard.ShouldInjectStopDirective);

        AgentToolLoopGuard.Reset();
        Assert.False(AgentToolLoopGuard.ShouldInjectStopDirective);
        Assert.Equal(0, AgentToolLoopGuard.ConsecutiveToolRounds);
    }

    // ---------- web_search 技能拦截计数 ----------

    /// <summary>web_search 被技能拦截：第二次起返回强硬终止文案，不再重复引导。</summary>
    [Fact]
    public async Task WebSearchBlocked_SecondTime_ReturnsHardStop()
    {
        AgentToolLoopGuard.Reset();
        var adapter = MakeAdapter("web_search", (Func<string, string>)(query => "结果"));
        using var doc = JsonDocument.Parse("""{"query":"RTX 5090"}""");

        var prev = AgentToolContext.SkillTriggerActive;
        try
        {
            AgentToolContext.SkillTriggerActive = true;

            var first = await adapter.ExecuteAsync(doc.RootElement);
            var second = await adapter.ExecuteAsync(doc.RootElement);

            Assert.Contains("web_search 已被禁用", first);
            Assert.Contains("已连续两次被拦截", second); // 第二次直接终止，不再给长引导
        }
        finally
        {
            AgentToolContext.SkillTriggerActive = prev;
        }
    }
}
