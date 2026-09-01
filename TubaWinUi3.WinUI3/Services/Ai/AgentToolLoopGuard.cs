namespace TubaWinUi3.Services.Ai;

/// <summary>
/// Agent 工具循环护栏（会话级）：
/// - 重复调用检测：同一会话中「工具 + 参数」完全相同的第二次调用直接拦截、不真正执行，
///   阻止模型对同一操作反复调用（反复 launch_tool 同一工具 / web_search 同一关键词）空转烧轮次，
///   这是 Agent 死循环的主要放大环节之一（终止决策全押在模型自觉上，这里由引擎强制兜底）。
/// - 空参数调用（查询类，如实时温度）豁免去重——允许重复取最新值。
/// - 会话边界：新对话（AiAgentPage.ResetToNewChat）时调用 <see cref="Reset"/> 清空登记。
/// 线程安全：AI 工具调用可能并发（多轮并行），登记操作加锁。
/// </summary>
internal static class AgentToolLoopGuard
{
    private static readonly object Sync = new();
    private static readonly HashSet<string> Seen = new(StringComparer.Ordinal);

    /// <summary>
    /// 登记一次工具调用；返回 true 表示重复（应拦截），false 表示首次（可正常执行）。
    /// 签名 = toolName + '\u0000' + 规范化参数（递归按键排序序列化）。
    /// </summary>
    public static bool IsDuplicate(string toolName, string normalizedArgs)
    {
        lock (Sync)
            return !Seen.Add(toolName + "\u0000" + normalizedArgs);
    }

    /// <summary>清空登记（新对话/会话切换时调用）。</summary>
    public static void Reset()
    {
        lock (Sync)
            Seen.Clear();
    }
}
