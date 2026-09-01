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
}
