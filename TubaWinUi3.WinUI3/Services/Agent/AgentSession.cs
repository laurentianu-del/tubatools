using System.Text;
using Microsoft.Extensions.AI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 一次 AI 对话会话：封装 Agent 循环、确认流、上下文记忆与持久化。
/// 事件在工作线程触发，UI 层通过 DispatcherQueue marshal。
/// </summary>
public sealed class AgentSession : IDisposable
{
    private readonly List<ChatMessage> _history = [];
    private readonly List<AgentStep> _groupSteps = [];
    private readonly List<ConversationDisplayItem> _displayItems = [];
    private readonly StringBuilder _textSb = new();
    private readonly ConversationMemory _memory;
    private CancellationTokenSource _cts = new();
    private bool _awaitingConfirmation;
    private System.Diagnostics.Stopwatch? _groupTimer;
    private int _groupPromptTokens;
    private int _groupCompletionTokens;
    /// <summary>上一组是否已结算。为 false 表示处于确认暂停期（步骤组保持打开）。</summary>
    private bool _groupCompleted = true;
    /// <summary>当前正在填充的步骤链展示项。</summary>
    private ConversationDisplayItem? _openStepsItem;

    public string Id { get; private set; }
    public string Title { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsDefaultModel => AiService.IsUsingDefaultModel;

    /// <summary>本会话累计 token 消耗（跨轮次累加，不受步骤组重置影响）。</summary>
    public int TotalPromptTokens { get; private set; }
    public int TotalCompletionTokens { get; private set; }
    public int TotalTokens => TotalPromptTokens + TotalCompletionTokens;

    /// <summary>流式文本增量。</summary>
    public event Action<string>? TextChunk;

    /// <summary>步骤开始。</summary>
    public event Action<AgentStep>? StepStarted;

    /// <summary>步骤结束（成功/失败/拒绝/取消）。</summary>
    public event Action<AgentStep>? StepCompleted;

    /// <summary>需要用户确认（循环暂停）。</summary>
    public event Action<IReadOnlyList<AgentConfirmationRequest>>? ConfirmationsRequested;

    /// <summary>错误（连接失败/轮次上限）。</summary>
    public event Action<string>? Error;

    /// <summary>本轮正常完成（无待确认）。</summary>
    public event Action? RunCompleted;

    /// <summary>新一轮 API 请求开始（UI 据此重置「思考/排队」状态）。</summary>
    public event Action? RoundStarted;

    /// <summary>整条步骤链完成（UI 据此折叠为摘要节点）。</summary>
    public event Action<AgentStepGroupSummary>? StepGroupCompleted;

    /// <summary>会话记忆文件读写（供记忆工具访问）。</summary>
    internal ConversationMemory Memory => _memory;

    private static string HistoryDir => Path.Combine(ConfigManager.GetDataDir(), "AiAssistant");

    private AgentSession(string id, string title)
    {
        Id = id;
        Title = title;
        _memory = new ConversationMemory(Path.Combine(HistoryDir, $"{id}.memory.md"));
    }

    public static AgentSession CreateNew()
    {
        var session = new AgentSession(Guid.NewGuid().ToString("N")[..12], "新对话");
        session.EnsureSystemPrompt();
        return session;
    }

    public static AgentSession Load(ConversationMeta meta)
    {
        var session = new AgentSession(meta.Id, meta.Title);
        var aiMessages = AiAssistantService.LoadConversation(meta.Id);
        session._history.AddRange(AgentMessageConverter.ToChatMessages(aiMessages));
        var display = AiAssistantService.LoadConversationDisplay(meta.Id);
        session._displayItems.AddRange(display);
        // 恢复会话级 token 统计（meta 条目，多轮累计展示用）
        var metaItem = display.FirstOrDefault(i => i.Type == "meta");
        if (metaItem is not null)
        {
            session.TotalPromptTokens = metaItem.PromptTokens;
            session.TotalCompletionTokens = metaItem.CompletionTokens;
        }
        session.EnsureSystemPrompt();
        return session;
    }

    /// <summary>发送用户消息并运行 Agent 循环（工作线程执行；异常会向上抛出由 UI 处理）。</summary>
    public async Task SendAsync(string userText)
    {
        if (IsRunning || _awaitingConfirmation || string.IsNullOrWhiteSpace(userText)) return;

        IsRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        AgentToolContext.Current = this;
        _groupSteps.Clear();
        _groupTimer = System.Diagnostics.Stopwatch.StartNew();
        _groupPromptTokens = 0;
        _groupCompletionTokens = 0;
        _groupCompleted = true;
        _displayItems.Add(new ConversationDisplayItem { Type = "text", Role = "user", Content = userText });
        AgentDebugLog.Info($"SendAsync 开始：{AgentToolHelpers.Truncate(userText, 60)}");

        try
        {
            EnsureSystemPrompt();

            // 标题：第一条用户消息
            if (_history.Count <= 1 && Title == "新对话")
                Title = userText.Length > 30 ? userText[..30] + "…" : userText;

            // 上下文预算：先本地截断，超预算滚动摘要
            using var client = AgentClientFactory.CreateClient();
            var prepared = await AgentMemory.PrepareHistoryAsync(client, _history, ct: ct);
            _history.Clear();
            _history.AddRange(prepared);

            _history.Add(new ChatMessage(ChatRole.User, userText));

            var cb = BuildCallbacks();
            await AgentRuntime.RunLoopAsync(_history, cb, ct);
        }
        catch (OperationCanceledException)
        {
            // 用户取消：结算当前打开的步骤链（含已取消步骤），UI 折叠
            CompleteGroup(raise: true);
            throw;
        }
        finally
        {
            FinalizeOpenTextItem();
            IsRunning = false;
            AgentToolContext.Current = null;
        }
    }

    /// <summary>应用确认决策（已确认的会真正执行），然后继续循环。</summary>
    public async Task ResumeConfirmationsAsync(IReadOnlyList<AgentConfirmationDecision> decisions)
    {
        if (IsRunning || !_awaitingConfirmation || decisions.Count == 0)
        {
            AgentDebugLog.Error($"ResumeConfirmations 被跳过：IsRunning={IsRunning}, awaiting={_awaitingConfirmation}, decisions={decisions.Count}");
            return;
        }

        AgentDebugLog.Info($"ResumeConfirmations 开始，决策 {decisions.Count} 条");
        _awaitingConfirmation = false;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        AgentToolContext.Current = this;

        try
        {
            var cb = BuildCallbacks();
            await AgentRuntime.ResumeLoopAsync(_history, decisions, cb, ct);
            AgentDebugLog.Info("ResumeConfirmations 结束");
        }
        catch (OperationCanceledException)
        {
            CompleteGroup(raise: true);
            throw;
        }
        finally
        {
            FinalizeOpenTextItem();
            IsRunning = false;
            AgentToolContext.Current = null;
        }
    }

    /// <summary>取消当前运行。</summary>
    public void Cancel() => _cts.Cancel();

    /// <summary>持久化：协议历史（messages.json）+ 展示记录（display.json，含步骤链与 token 统计）。</summary>
    public void Save()
    {
        if (_history.Count == 0) return;
        try
        {
            AiAssistantService.SaveConversation(Id, Title, AgentMessageConverter.ToAiMessages(_history));
            // 会话级 token 统计写入 meta 条目（多轮累计，加载时恢复）
            if (TotalTokens > 0)
            {
                var meta = _displayItems.FirstOrDefault(i => i.Type == "meta");
                if (meta is null)
                {
                    meta = new ConversationDisplayItem { Type = "meta" };
                    _displayItems.Add(meta);
                }
                meta.PromptTokens = TotalPromptTokens;
                meta.CompletionTokens = TotalCompletionTokens;
            }
            AiAssistantService.SaveConversationDisplay(Id, _displayItems);
        }
        catch { }
    }

    public void Dispose()
    {
        _cts.Dispose();
        Save();
    }

    // ---------- 内部 ----------

    private AgentRunCallbacks BuildCallbacks() => new()
    {
        OnTextChunk = chunk =>
        {
            _textSb.Append(chunk);
            TextChunk?.Invoke(chunk);
        },
        OnRoundStarted = () =>
        {
            // 新一轮：若上一组已结算则重置统计（确认暂停期内保持打开，与新步骤同组）
            if (_groupCompleted)
            {
                _groupSteps.Clear();
                _groupPromptTokens = 0;
                _groupCompletionTokens = 0;
                _groupTimer = System.Diagnostics.Stopwatch.StartNew();
            }
            RoundStarted?.Invoke();
        },
        OnStepStarted = step =>
        {
            _groupSteps.Add(step);
            if (_openStepsItem is null)
            {
                // 首个工具步骤：定稿本轮文本，并新建步骤链展示项（位置紧随文本之后）
                FinalizeOpenTextItem();
                _openStepsItem = new ConversationDisplayItem { Type = "steps" };
                _displayItems.Add(_openStepsItem);
            }
            StepStarted?.Invoke(step);
        },
        OnStepCompleted = step => StepCompleted?.Invoke(step),
        OnConfirmationsRequested = requests =>
        {
            _awaitingConfirmation = true;
            // 暂停轮不结算：确认后的执行与暂停轮同属一组
            _groupCompleted = false;
            ConfirmationsRequested?.Invoke(requests);
        },
        OnError = error => Error?.Invoke(error),
        OnCompleted = _ => RunCompleted?.Invoke(),
        OnUsage = usage =>
        {
            _groupPromptTokens += usage.PromptTokens;
            _groupCompletionTokens += usage.CompletionTokens;
            TotalPromptTokens += usage.PromptTokens;
            TotalCompletionTokens += usage.CompletionTokens;
        },
        OnRoundCompleted = () => CompleteGroup(raise: true)
    };

    /// <summary>结算当前步骤组：快照展示项 + 触发 UI 折叠事件。</summary>
    private void CompleteGroup(bool raise)
    {
        _groupCompleted = true;
        if (_groupSteps.Count == 0)
        {
            _openStepsItem = null;
            return;
        }

        _groupTimer?.Stop();
        var summary = new AgentStepGroupSummary
        {
            Total = _groupSteps.Count,
            Success = _groupSteps.Count(s => s.Status == AgentStepStatus.Success),
            Failed = _groupSteps.Count(s => s.Status is AgentStepStatus.Failed or AgentStepStatus.Rejected),
            Duration = _groupTimer?.Elapsed,
            PromptTokens = _groupPromptTokens,
            CompletionTokens = _groupCompletionTokens,
            ByTool = _groupSteps
                .GroupBy(s => s.DisplayName)
                .ToDictionary(g => g.Key, g => g.Count())
        };

        if (_openStepsItem is not null)
        {
            _openStepsItem.Steps = _groupSteps.Select(AgentStepSnapshot.From).ToList();
            _openStepsItem.SummaryText = summary.ToDisplayText();
            _openStepsItem.DurationSeconds = summary.Duration?.TotalSeconds;
            _openStepsItem.PromptTokens = summary.PromptTokens;
            _openStepsItem.CompletionTokens = summary.CompletionTokens;
            _openStepsItem = null;
        }

        if (raise)
            StepGroupCompleted?.Invoke(summary);
    }

    /// <summary>定稿当前流式文本展示项（无文本则不产生条目）。</summary>
    private void FinalizeOpenTextItem()
    {
        if (_textSb.Length == 0) return;
        _displayItems.Add(new ConversationDisplayItem
        {
            Type = "text",
            Role = "assistant",
            Content = _textSb.ToString()
        });
        _textSb.Clear();
    }

    private static string BuildSystemContent()
    {
        var content = AgentPrompts.SystemPrompt + "\n\n" +
                      CliToolboxCatalog.Default.BuildIndexContext() + "\n\n" +
                      AiAssistantService.BuildSystemContext() + "\n\n" +
                      AiAssistantService.BuildSystemInfoContext();
        return content;
    }

    private void EnsureSystemPrompt()
    {
        var content = BuildSystemContent();
        var notes = _memory.Read();
        if (!string.IsNullOrWhiteSpace(notes))
            content += "\n\n## 会话记忆（由 read_memory / write_memory / clear_memory 维护，回答时参考）\n" + notes;

        if (_history.Count == 0 || _history[0].Role != ChatRole.System)
            _history.Insert(0, new ChatMessage(ChatRole.System, content));
        else
            _history[0] = new ChatMessage(ChatRole.System, content);
    }
}
