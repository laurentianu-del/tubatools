using System.Text;
using System.Text.Json;
using FieldCure.Ai.Providers.Models;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Services.Ai;

/// <summary>
/// 现有 Agent 工具 → ChatPanel <see cref="IAssistTool"/> 适配器：
/// - JSON Schema 直接取自 <see cref="AIFunction"/> 自动生成的声明（不再手写）;
/// - 危险操作确认策略与旧 AgentRuntime 完全一致：AlwaysConfirm 必须确认；
///   计划/危险工具在「完全访问」模式下豁免；IsFullAccess 实时读取，开关立即生效；
/// - web_search 在技能强制触发（配电脑查价）时按旧引擎同款文案拦截，强制改用浏览器工具。
/// </summary>
internal sealed class AgentToolAdapter : IAssistTool
{
    /// <summary>任意工具开始执行（供 UI 联动，如 bot 头像 orbit 态）。</summary>
    public static event Action<string>? ToolExecutionStarted;
    /// <summary>任意工具执行结束（无论成败均触发）。</summary>
    public static event Action<string>? ToolExecutionFinished;

    private readonly AgentTool _tool;
    private readonly string _parameterSchema;

    public AgentToolAdapter(AgentTool tool)
    {
        _tool = tool;
        _parameterSchema = tool.Function.JsonSchema.GetRawText();
    }

    public string Name => _tool.Name;
    public string DisplayName => _tool.DisplayName;
    public string Description => _tool.Function.Description ?? "";
    public string ParameterSchema => _parameterSchema;

    /// <summary>
    /// 是否需要用户确认（ChatPanel 据此弹出 ToolApprovalPanel）。
    /// 动态计算：完全访问开关改动的瞬间即生效，无需重建工具列表。
    /// </summary>
    public bool RequiresConfirmation =>
        _tool.AlwaysConfirm || (!AgentToolContext.IsFullAccess && (_tool.IsPlanTool || _tool.RequiresConfirmation));

    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        // 技能强制触发（「电脑选购」技能要求用浏览器查京东实时价格）→ 拦截 web_search，
        // 与 AgentRuntime 中的运行时拦截语义一致。
        if (_tool.Name == "web_search" && AgentToolContext.SkillTriggerActive)
        {
            return
                "web_search 已被禁用：当前任务触发了「电脑选购」技能，要求用浏览器查询京东实时价格（搜索返回的不是可购买的真实价格）。请改用浏览器工具：browser_navigate 打开 https://search.jd.com/Search?keyword=商品名（URL 编码），再用 browser_get_page / browser_run_js 提取价格。";
        }

        ToolExecutionStarted?.Invoke(_tool.Name);
        try
        {
            return await ExecuteCoreAsync(parameters, ct);
        }
        finally
        {
            ToolExecutionFinished?.Invoke(_tool.Name);
        }
    }

    private async Task<string> ExecuteCoreAsync(JsonElement parameters, CancellationToken ct)
    {
        var args = AgentArgsJson.ParseToDictionary(parameters.GetRawText());

        // 重复调用护栏：非空参数且签名完全相同的第二次调用直接拦截（不真正执行），
        // 打破模型"反复调用同一操作"的循环（死循环放大环节之一）；
        // 空参数（查询类，如实时温度）豁免——允许重复取最新值。
        var normalized = NormalizeArgs(parameters);
        if (normalized is not null && AgentToolLoopGuard.IsDuplicate(_tool.Name, normalized))
        {
            return "[重复调用已拦截] 相同参数的工具调用在本轮对话中已执行过，结果不会变化。请勿重复执行，直接基于已有结果继续完成用户的目标；确有必要时先向用户说明再进行下一步。";
        }

        var result = await _tool.Function.InvokeAsync(new AIFunctionArguments(args), ct);
        var text = result?.ToString();

        // 空结果标记：避免模型把"无内容返回"误判为执行失败而无限重试（死循环放大环节之一）
        return string.IsNullOrWhiteSpace(text) ? "（工具已执行，未返回内容）" : text;
    }

    /// <summary>
    /// 参数规范化：对象递归按键排序序列化（{ "b":1,"a":2 } 与 { "a":2,"b":1 } 视为相同）。
    /// 空对象/非对象返回 null，表示不做去重（查询类工具允许重复取最新值）。
    /// </summary>
    private static string? NormalizeArgs(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.EnumerateObject().Any())
            return null;

        using var doc = JsonDocument.Parse(parameters.GetRawText());
        var sb = new StringBuilder();
        AppendNormalized(doc.RootElement, sb);
        return sb.ToString();
    }

    private static void AppendNormalized(JsonElement el, StringBuilder sb)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
            {
                sb.Append('{');
                var first = true;
                foreach (var prop in el.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonSerializer.Serialize(prop.Name)).Append(':');
                    AppendNormalized(prop.Value, sb);
                }
                sb.Append('}');
                break;
            }
            case JsonValueKind.Array:
            {
                sb.Append('[');
                var first = true;
                foreach (var item in el.EnumerateArray())
                {
                    if (!first) sb.Append(',');
                    first = false;
                    AppendNormalized(item, sb);
                }
                sb.Append(']');
                break;
            }
            default:
                sb.Append(el.GetRawText()); // 字符串/数字/布尔/null 原样（含引号，保真）
                break;
        }
    }
}