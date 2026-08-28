using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;
using TubaWinUi3.Controls.AgentChat;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Agent;
using TubaWinUi3.Services.Ai;

namespace TubaWinUi3.Pages;

/// <summary>
/// AI 智能代理页面（FieldCure ChatPanel 重构版）。
///
/// 渲染与交互全部由组件库接管：
/// - 消息流式渲染 / Markdown（WebView2）/ 思考块 / 内联工具调用 / 续写按钮
/// - 危险操作 ToolApprovalPanel 确认（RequiresConfirmation 由工具适配器按「完全访问」动态计算）
/// - 输入控制台（Enter 发送 / Shift+Enter 换行 / 停止按钮）
///
/// 本页只保留图吧工具箱自己的壳与数据面：
/// - 顶栏：提供商/模型切换、完全访问开关、技能菜单、新对话/历史、token 统计
/// - Provider：<see cref="TubaChatProvider"/>（OpenAI 兼容端点 → StreamEvent 流）
/// - 工具：现有 AgentToolRegistry 全部工具经 <see cref="AgentToolAdapter"/> 注册
/// - 持久化：沿用 AiAssistantService 的 messages.json / display.json / skills.json / memory.md
/// - 技能：触发词检测 → 系统提示词注入强制指令 + web_search 拦截（适配器层）
/// </summary>
public sealed partial class AiAgentPage : UserControl
{
    private readonly DispatcherQueue _dq;
    private readonly TubaChatProvider _provider = new();
    private readonly List<IAssistTool> _toolAdapters = [];
    private readonly HashSet<string> _activeSkillIds = [];

    /// <summary>当前会话待持久化的消息（user/assistant 文本按发送顺序）。</summary>
    private readonly List<PersistedMessage> _persisted = [];

    private string _conversationId = Guid.NewGuid().ToString("N")[..12];
    private string _title = "新对话";
    private ConversationMemory? _memory;
    private bool _isProcessing;
    private bool _suppressToggleEvent;
    private bool _syncingCombos;
    private DispatcherTimer? _saveTimer;

    // 会话级 token 统计（多轮累加，随 display.json meta 条目恢复）
    private int _sessionPromptTokens;
    private int _sessionCompletionTokens;
    private int _sessionCacheHits;
    private int _sessionCacheMisses;

    private static string HistoryDir => Path.Combine(ConfigManager.GetDataDir(), "AiAssistant");

    private sealed record PersistedMessage(string Role, string Content, string? Thinking);

    public AiAgentPage()
    {
        InitializeComponent();
        _dq = DispatcherQueue.GetForCurrentThread();

        // ===== ChatPanel 组件库接线 =====
        _toolAdapters.AddRange(AgentToolRegistry.Tools.Select(t => (IAssistTool)new AgentToolAdapter(t)));
        Chat.Provider = _provider;
        Chat.RegisteredTools = _toolAdapters;
        Chat.MaxToolCallRounds = 30; // 与旧 AgentRuntime.DefaultMaxRounds 一致
        Chat.AllowAttachments = false;
        Chat.IsWorkspaceEnabled = false;
        Chat.IsKnowledgeBaseEnabled = false;
        Chat.ShowTitleBar = false;
        Chat.ShowModelSelector = false;
        Chat.ShowProfileSelector = false;
        Chat.AutoTitle = false;
        Chat.AutoSummarize = false;
        Chat.Theme = ChatTheme.System;
        Chat.UserMessageSubmitted += OnUserMessageSubmitted;
        Chat.MessageAdded += OnMessageAdded;
        Chat.EmptyStateContent = BuildWelcomeContent();
        TubaChatProvider.UsageReported += OnUsageReported;
        AgentToolContext.MemoryModified += OnMemoryModified;

        _suppressToggleEvent = true;
        FullAccessToggle.IsOn = AgentToolContext.IsFullAccess;
        _suppressToggleEvent = false;

        RebuildSystemPrompt();
        UpdateRunState();
        RefreshProviderCombos();
        LoadLatestConversation();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // 页面淡入
        var sb = new Storyboard();
        var opacity = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(300)) };
        Storyboard.SetTarget(opacity, this);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        sb.Children.Add(opacity);
        sb.Begin();
    }

    /// <summary>页面卸载（内置工具关闭时调用）：停止防抖保存、取消事件订阅并释放 ChatPanel。</summary>
    public void Unload()
    {
        SaveNow();
        _saveTimer?.Stop();
        Chat.UserMessageSubmitted -= OnUserMessageSubmitted;
        Chat.MessageAdded -= OnMessageAdded;
        TubaChatProvider.UsageReported -= OnUsageReported;
        AgentToolContext.MemoryModified -= OnMemoryModified;
        AgentToolContext.SkillTriggerActive = false;
        AgentToolContext.ActiveMemory = null;
        Chat.Dispose();
    }

    // ---------- 发送流 ----------

    /// <summary>用户提交消息：技能触发检测 → 系统提示词注入 → 状态切换。（ChatPanel 内部随后流式执行）</summary>
    private void OnUserMessageSubmitted(object? sender, MessageSentEventArgs e)
    {
        _isProcessing = true;
        UpdateInputState();
        UpdateRunState();

        // 技能触发：命中触发词 → 系统提示词末尾注入强指令 + 本次发送内禁用 web_search
        var (trigger, fragments) = BuildTriggerPayload(e.Text ?? "");
        AgentToolContext.SkillTriggerActive = !string.IsNullOrEmpty(trigger);
        RebuildSystemPrompt(trigger, fragments);
    }

    /// <summary>消息加入树：用户消息立即入持久化；assistant 回复等定稿（IsStreaming=false）再保存。</summary>
    private void OnMessageAdded(object? sender, ChatMessage msg)
    {
        if (msg.Role == ChatRole.User)
        {
            AddPersisted("user", msg.Content, null);
            TrySetTitleFromFirstMessage(msg.Content ?? "");
        }
        else if (msg.Role == ChatRole.Assistant)
        {
            // 工具调用包装消息（中间轮，同流渲染）不入持久化
            if (msg.ToolCalls is { Count: > 0 }) return;
            msg.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChatMessage.IsStreaming) && !msg.IsStreaming)
                    OnAssistantFinalized(msg);
            };
        }
        ScheduleSave();
    }

    /// <summary>本轮回复定稿：保存文本（含思考链）、复位状态、刷新记忆注入。</summary>
    private void OnAssistantFinalized(ChatMessage msg)
    {
        AddPersisted("assistant", msg.Content ?? "", msg.ThinkingContent);
        _isProcessing = false;
        AgentToolContext.SkillTriggerActive = false;
        Chat.MemoryText = _memory?.Read() ?? "";
        UpdateRunState();
        UpdateInputState();
        ScheduleSave();
    }

    private void AddPersisted(string role, string content, string? thinking)
    {
        if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(thinking)) return;
        _persisted.Add(new PersistedMessage(role, content ?? "", thinking));
    }

    /// <summary>首条用户消息 → 会话标题（可后续在历史菜单重命名）。</summary>
    private void TrySetTitleFromFirstMessage(string text)
    {
        if (_title != "新对话" || string.IsNullOrWhiteSpace(text)) return;
        _title = text.Length > 30 ? text[..30] + "…" : text;
        TitleText.Text = _title;
    }

    // ---------- 技能触发 ----------

    /// <summary>检测技能触发并重建系统提示词（web_search 拦截由工具适配层按静态开关执行）。</summary>
    private (string? Trigger, string? Fragments) BuildTriggerPayload(string userText)
        => SkillTriggerHelper.BuildTriggerPayload(userText, _activeSkillIds);

    /// <summary>重建 ChatPanel 系统提示词（主提示词 + 已加载技能索引 + 触发注入）。</summary>
    private void RebuildSystemPrompt(string? trigger = null, string? fragments = null)
        => Chat.SystemPrompt = SkillTriggerHelper.BuildSystemPrompt(_activeSkillIds, trigger, fragments);

    // ---------- 技能菜单 ----------

    private void SkillsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshSkillsPanel();
        if (Resources["SkillsFlyout"] is Flyout flyout)
            flyout.ShowAt(SkillsButton);
    }

    /// <summary>按当前会话的技能状态重建菜单项（技能默认全部加载）。</summary>
    private void RefreshSkillsPanel()
    {
        if (Resources["SkillsFlyout"] is not Flyout { Content: StackPanel panel }) return;
        panel.Children.Clear();

        // 顶部说明：消除"技能默认关闭"的误解
        panel.Children.Add(new TextBlock
        {
            Text = "技能默认全部启用；取消勾选即禁用（勾选/取消后立即生效，下一条消息按新状态执行）",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 2, 8, 8),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        foreach (var skill in AgentSkillRegistry.All)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new FontIcon
            {
                Glyph = skill.Glyph,
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            var texts = new StackPanel { Spacing = 1 };
            texts.Children.Add(new TextBlock
            {
                Text = skill.DisplayName,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });
            texts.Children.Add(new TextBlock
            {
                Text = skill.Description,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 230,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            content.Children.Add(texts);

            var check = new CheckBox
            {
                Content = content,
                IsChecked = _activeSkillIds.Contains(skill.Id),
                Tag = skill.Id,
                MinWidth = 0,
                Padding = new Thickness(0, 6, 0, 6)
            };
            check.Checked += SkillToggle_Changed;
            check.Unchecked += SkillToggle_Changed;
            panel.Children.Add(check);
        }
    }

    private void SkillToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string id } cb) return;
        var skill = AgentSkillRegistry.Find(id);
        if (skill is null) return;

        if (cb.IsChecked == true) _activeSkillIds.Add(id);
        else _activeSkillIds.Remove(id);
        SaveSkills();
        // 系统提示词即时重建，下一条消息按新状态执行
        AgentToolContext.SkillTriggerActive = false;
        RebuildSystemPrompt();
    }

    // ---------- 会话控制 ----------

    private void LoadLatestConversation()
    {
        var conversations = AiAssistantService.ListConversations();
        if (conversations.Count > 0)
            LoadConversation(conversations[0]);
    }

    private void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        ResetToNewChat();
    }

    /// <summary>重置为新对话：清空 ChatPanel 与内存状态，生成新的会话 Id。</summary>
    private void ResetToNewChat()
    {
        _saveTimer?.Stop();
        _conversationId = Guid.NewGuid().ToString("N")[..12];
        _title = "新对话";
        TitleText.Text = _title;
        _persisted.Clear();
        ResetTokenStats();
        LoadSkills(_conversationId); // 新会话默认全部技能
        _memory = null;
        AgentToolContext.ActiveMemory = null;
        AgentToolContext.SkillTriggerActive = false;
        Chat.MemoryText = null;
        Chat.ClearConversation();
        RebuildSystemPrompt();
        Chat.FocusInput();
        UpdateInputState();
    }

    private async void LoadConversation(ConversationMeta meta)
    {
        if (_isProcessing) return;

        _saveTimer?.Stop();
        _conversationId = meta.Id;
        _title = meta.Title;
        TitleText.Text = meta.Title;
        _persisted.Clear();
        ResetTokenStats();
        LoadSkills(meta.Id);
        _memory = new ConversationMemory(Path.Combine(HistoryDir, $"{meta.Id}.memory.md"));
        AgentToolContext.ActiveMemory = _memory;
        AgentToolContext.SkillTriggerActive = false;
        Chat.MemoryText = _memory.Read() ?? "";

        // 恢复会话级 token 统计（meta 条目；旧会话的步骤链记录不再渲染）
        var display = AiAssistantService.LoadConversationDisplay(meta.Id);
        var metaItem = display.FirstOrDefault(i => i.Type == "meta");
        if (metaItem is not null)
        {
            _sessionPromptTokens = metaItem.PromptTokens;
            _sessionCompletionTokens = metaItem.CompletionTokens;
            _sessionCacheHits = metaItem.CacheHitTokens;
            _sessionCacheMisses = metaItem.CacheMissTokens;
        }
        UpdateTokenUsage();
        RebuildSystemPrompt();

        // 清空并回填消息树（user/assistant 文本 + 思考链按序恢复；工具轮次为内部协议不恢复）
        Chat.ClearConversation();
        await Task.Delay(200); // 让 ClearConversation 的渲染器复位先完成
        var messages = AiAssistantService.LoadConversation(meta.Id);
        foreach (var m in messages)
        {
            if (m.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(m.Content)) continue;
            Chat.AddRestoredMessage(
                m.Role == "user" ? ChatRole.User : ChatRole.Assistant,
                m.Content,
                thinkingContent: m.ReasoningContent);
        }
        await WhenChatReadyAsync();
        await Chat.RenderRestoredMessagesAsync();
        Chat.FocusInput();
        UpdateInputState();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        SaveNow();

        var flyout = AiHistoryMenu.Build(
            AiAssistantService.ListConversations(),
            onOpen: LoadConversation,
            onRename: OnRenameConversation,
            onDelete: OnDeleteConversation);

        flyout.ShowAt(sender as FrameworkElement);
    }

    /// <summary>重命名会话：当前打开的会话同步更新内存标题，并立即持久化到 meta.json。</summary>
    private async void OnRenameConversation(ConversationMeta conv)
    {
        var newTitle = await AiHistoryMenu.PromptRenameAsync(XamlRoot, conv.Title);
        if (newTitle is null) return;

        if (_conversationId == conv.Id)
        {
            _title = newTitle;
            TitleText.Text = newTitle;
        }
        AiAssistantService.RenameConversation(conv.Id, newTitle);
    }

    /// <summary>删除会话：确认后移除全部关联文件；删的是当前会话则回到新对话。</summary>
    private async void OnDeleteConversation(ConversationMeta conv)
    {
        if (!await AiHistoryMenu.ConfirmDeleteAsync(XamlRoot, conv.Title)) return;

        AiAssistantService.DeleteConversation(conv.Id);
        if (_conversationId == conv.Id)
            ResetToNewChat();
    }

    // ---------- 持久化 ----------

    /// <summary>防抖保存（1.2s），覆盖连续流式增长；定稿/切换/卸载时立即保存。</summary>
    private void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            _saveTimer.Tick += (_, _) => SaveNow();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer?.Stop();
        if (_persisted.Count == 0) return;
        try
        {
            var messages = _persisted
                .Select(p => new AiChatMessage
                {
                    Role = p.Role,
                    Content = p.Content,
                    ReasoningContent = p.Thinking
                })
                .ToList();
            AiAssistantService.SaveConversation(_conversationId, _title, messages);

            // 会话级 token 统计写入 meta 条目（多轮累计，加载时恢复）
            if (_sessionPromptTokens + _sessionCompletionTokens > 0)
            {
                AiAssistantService.SaveConversationDisplay(_conversationId,
                [
                    new ConversationDisplayItem
                    {
                        Type = "meta",
                        PromptTokens = _sessionPromptTokens,
                        CompletionTokens = _sessionCompletionTokens,
                        CacheHitTokens = _sessionCacheHits,
                        CacheMissTokens = _sessionCacheMisses,
                    }
                ]);
            }
            SaveSkills();
        }
        catch { }
    }

    private static string SkillsPath(string id) => Path.Combine(HistoryDir, $"{id}.skills.json");

    /// <summary>加载会话技能状态：文件缺失时默认全部技能激活。</summary>
    private void LoadSkills(string id)
    {
        _activeSkillIds.Clear();
        try
        {
            if (File.Exists(SkillsPath(id)))
            {
                var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SkillsPath(id)));
                if (ids is not null)
                    foreach (var skillId in ids)
                        if (AgentSkillRegistry.Find(skillId) is not null)
                            _activeSkillIds.Add(skillId);
            }
            else
            {
                foreach (var skill in AgentSkillRegistry.All)
                    _activeSkillIds.Add(skill.Id);
            }
        }
        catch
        {
            foreach (var skill in AgentSkillRegistry.All)
                _activeSkillIds.Add(skill.Id);
        }
    }

    private void SaveSkills()
    {
        try
        {
            var path = SkillsPath(_conversationId);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(_activeSkillIds.OrderBy(x => x).ToList()));
        }
        catch { }
    }

    // ---------- 空状态 / 欢迎 ----------

    private UIElement BuildWelcomeContent()
    {
        var panel = new StackPanel
        {
            MaxWidth = 680,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 14,
            Padding = new Thickness(24, 16, 24, 16)
        };

        if (AiService.IsUsingDefaultModel)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "⚠️ 自带模型可能出现排队/限额满速，质量低下等问题。推荐使用 DeepSeek V4 Pro。\n\n你可以在 设置 → AI 服务 中配置自己的 API Key 和模型。",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = "你好！我是图吧助手（智能代理版），可以帮你诊断系统问题、优化配置、执行操作、搜索最新资讯。\n\n我可以：\n- 诊断问题并**自动执行**修复操作（危险操作会先请你确认）\n- 读写文件、执行命令、下载文件\n- 联网搜索最新硬件评测、驱动、价格\n- **配电脑/装机时自动上京东查实时价格**（「电脑选购」技能，可在顶部「技能」菜单开关）\n- 制定多步任务计划并逐项执行\n- 记住你的偏好与任务进度（会话记忆）\n\n直接在下方输入你的需求！",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        var parts = AgentSkillRegistry.All.Select(s => $"{s.DisplayName}（{s.Description}）");
        panel.Children.Add(new TextBlock
        {
            Text = $"🔧 当前已加载技能：{string.Join("、", parts)}\n可在顶部「技能」菜单随时开关，咨询对应场景时我将按技能要求执行。",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        });
        return panel;
    }

    // ---------- 提供商 / 模型切换 ----------

    /// <summary>同步顶栏提供商/模型下拉框与当前选中状态（选中状态全局持久化）。</summary>
    private void RefreshProviderCombos()
    {
        _syncingCombos = true;
        try
        {
            var providers = AiProviderStore.GetProviders();
            var selectedId = AiProviderStore.SelectedProviderId;
            // 传副本（见 SettingsPage.RefreshAiProviderList：活列表原地修改会导致
            // ItemsSourceView 快照过期，同步设置 SelectedItem 抛 E_INVALIDARG）
            ProviderCombo.ItemsSource = providers.ToList();
            ProviderCombo.SelectedItem = providers.FirstOrDefault(p => p.Id == selectedId) ?? providers.FirstOrDefault();

            var provider = AiProviderStore.SelectedProvider;
            var modelId = AiProviderStore.SelectedModelId;
            ModelCombo.ItemsSource = provider.Models.ToList();
            ModelCombo.SelectedItem = provider.Models.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
                                      ?? provider.Models.FirstOrDefault();
        }
        finally
        {
            _syncingCombos = false;
        }
        UpdateServiceStatus();
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos || _isProcessing) return;
        if (ProviderCombo.SelectedItem is not AiProvider provider) return;

        var prevId = AiProviderStore.SelectedProviderId;
        if (provider.Id == prevId) return;

        AiProviderStore.SetSelected(provider.Id);
        RefreshProviderCombos();
    }

    private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos || _isProcessing) return;
        if (ProviderCombo.SelectedItem is not AiProvider provider) return;
        if (ModelCombo.SelectedItem is not AiModelOption model) return;

        var prev = AiProviderStore.SelectedModelId;
        if (model.Id.Equals(prev, StringComparison.OrdinalIgnoreCase)) return;

        AiProviderStore.SetSelected(provider.Id, model.Id);
        UpdateServiceStatus();
    }

    /// <summary>跳转设置页 AI 服务配置（提供商 / API Key / 模型）。</summary>
    private void AiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.NavigateToSettings("AiApiEndpoint");
    }

    private void UpdateServiceStatus()
    {
        if (AiService.IsUsingDefaultModel)
        {
            ModelStatusText.Text = "默认模型（建议在设置中配置 AI 服务）";
            ModelStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        }
        else
        {
            var provider = AiProviderStore.SelectedProvider;
            var (_, model, _) = AiService.GetConfig();
            ModelStatusText.Text = $"已连接 {provider.Name} · {model}";
            ModelStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }
    }

    private void FullAccessToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        AgentToolContext.IsFullAccess = FullAccessToggle.IsOn;
        UpdateRunState();
    }

    // ---------- 状态 / 统计 ----------

    private void UpdateInputState()
    {
        ProviderCombo.IsEnabled = !_isProcessing;
        ModelCombo.IsEnabled = !_isProcessing;
        UpdateRunState();
    }

    private void UpdateRunState()
    {
        if (_isProcessing)
        {
            RunStateText.Text = "执行中";
            RunStateText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }
        else
        {
            RunStateText.Text = AgentToolContext.IsFullAccess ? "完全访问" : "受控执行";
            RunStateText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }
    }

    /// <summary>Provider 每轮完成上报用量 → 会话级累计（跨轮次累加，与旧引擎语义一致）。</summary>
    private void OnUsageReported(TokenUsage? usage)
    {
        if (usage is null) return;
        _dq.TryEnqueue(() =>
        {
            _sessionPromptTokens += usage.InputTokens;
            _sessionCompletionTokens += usage.OutputTokens;
            if (usage.CacheReadInputTokens is { } hit)
            {
                _sessionCacheHits += (int)hit;
                // 未命中数缺失时按 prompt = hit + miss 推算（DeepSeek/GLM 语义）
                _sessionCacheMisses = Math.Max(0, _sessionPromptTokens - _sessionCacheHits);
            }
            UpdateTokenUsage();
        });
    }

    /// <summary>记忆工具写入后刷新 ChatPanel.MemoryText，让后续轮次立即读到新记忆。</summary>
    private void OnMemoryModified()
    {
        _dq.TryEnqueue(() =>
        {
            if (_memory is { } m)
                Chat.MemoryText = m.Read() ?? "";
        });
    }

    /// <summary>顶栏 token 气泡：实时展示本会话累计消耗（多轮累加，附缓存命中统计）。</summary>
    private void UpdateTokenUsage()
    {
        var tokens = _sessionPromptTokens + _sessionCompletionTokens;
        if (tokens <= 0)
        {
            TokenUsageBubble.Visibility = Visibility.Collapsed;
            return;
        }
        TokenUsageBubble.Visibility = Visibility.Visible;
        var text = $"{FormatTokens(tokens)} tokens";
        if (_sessionCacheHits > 0)
        {
            var total = _sessionCacheHits + _sessionCacheMisses;
            var pct = total > 0 ? (int)Math.Round(_sessionCacheHits * 100.0 / total) : 100;
            text += $" · 缓存命中 {FormatTokens(_sessionCacheHits)} ({pct}%)";
        }
        TokenUsageText.Text = text;
    }

    private void ResetTokenStats()
        => _sessionPromptTokens = _sessionCompletionTokens = _sessionCacheHits = _sessionCacheMisses = 0;

    private static string FormatTokens(int tokens)
        => tokens >= 1000 ? $"{tokens / 1000.0:F1}k" : tokens.ToString();

    /// <summary>等待 ChatPanel 的 WebView2 渲染器初始化（最多 10 秒）。</summary>
    private async Task WhenChatReadyAsync()
    {
        for (var i = 0; i < 100 && !Chat.IsInitialized; i++)
            await Task.Delay(100);
    }
}