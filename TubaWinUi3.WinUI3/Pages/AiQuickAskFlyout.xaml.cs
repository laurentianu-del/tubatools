using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Controls.AgentChat;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Agent;
using TubaWinUi3.Services.Ai;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>
/// 标题栏快捷问询面板（轻量版）：与完整版共享 AgentSession 引擎。
/// 保留紧凑的代码构建 UI，支持流式输出、步骤指示与危险操作确认卡片。
/// </summary>
public sealed partial class AiQuickAskFlyout : UserControl
{
    private bool _syncingCombos;
    private readonly DispatcherQueue _dq;
    private readonly StackPanel _chatList;
    private AgentSession? _session;
    private CancellationTokenSource _cts = new();
    private bool _isProcessing;
    private bool _awaitingConfirmation;
    private Border? _streamingBubble;
    private TextBlock? _streamingTb;
    private StringBuilder? _streamingContent;
    private IReadOnlyList<AgentConfirmationRequest>? _pendingRequests;

    public AiQuickAskFlyout()
    {
        InitializeComponent();
        _dq = DispatcherQueue.GetForCurrentThread();
        _chatList = ChatList;

        UpdateServiceStatus();
        InitProviderCombos();
        LoadLatestConversation();
    }

    /// <summary>同步提供商/模型下拉框与当前选中状态（全局持久化，切换对下一条消息生效）。</summary>
    private void InitProviderCombos()
    {
        _syncingCombos = true;
        try
        {
            var providers = AiProviderStore.GetProviders();
            var selectedId = AiProviderStore.SelectedProviderId;
            // 传副本：活列表原地修改会导致 ItemsSourceView 快照过期（E_INVALIDARG）
            FlyoutProviderCombo.ItemsSource = providers.ToList();
            FlyoutProviderCombo.SelectedItem = providers.FirstOrDefault(p => p.Id == selectedId) ?? providers.FirstOrDefault();

            var provider = AiProviderStore.SelectedProvider;
            var modelId = AiProviderStore.SelectedModelId;
            var models = provider.Models.ToList();
            FlyoutModelCombo.ItemsSource = models;
            FlyoutModelCombo.SelectedItem = models
                .FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault();
        }
        finally
        {
            _syncingCombos = false;
        }
    }

    private void FlyoutProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos) return;
        if (FlyoutProviderCombo.SelectedItem is not AiProvider provider) return;

        var prev = AiProviderStore.SelectedProviderId;
        if (provider.Id == prev) return;

        AiProviderStore.SetSelected(provider.Id);
        InitProviderCombos();
        UpdateServiceStatus();
    }

    private void FlyoutModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos) return;
        if (FlyoutProviderCombo.SelectedItem is not AiProvider provider) return;
        if (FlyoutModelCombo.SelectedItem is not AiModelOption model) return;

        var prev = AiProviderStore.SelectedModelId;
        if (model.Id.Equals(prev, StringComparison.OrdinalIgnoreCase)) return;

        AiProviderStore.SetSelected(provider.Id, model.Id);
        UpdateServiceStatus();
    }

    private void UpdateServiceStatus()
    {
        if (AiService.IsUsingDefaultModel)
        {
            ServiceStatusText.Text = "自带模型（可在设置中配置 AI 服务）";
        }
        else
        {
            var provider = AiProviderStore.SelectedProvider;
            var (_, model, _) = AiService.GetConfig();
            ServiceStatusText.Text = $"已连接 {provider.Name} · {model}";
        }
    }

    private AgentSession CreateSession()
    {
        var session = AgentSession.CreateNew();
        HookSession(session);
        return session;
    }

    private void HookSession(AgentSession session)
    {
        session.TextChunk += chunk => _dq.TryEnqueue(() => SafeInvoke(() => AppendChunk(chunk)));
        session.StepStarted += step => _dq.TryEnqueue(() => SafeInvoke(() => AddToolCallIndicator(step)));
        session.StepCompleted += step => _dq.TryEnqueue(() => SafeInvoke(() => AddToolResultIndicator(step)));
        session.ConfirmationsRequested += requests => _dq.TryEnqueue(() => SafeInvoke(() => OnConfirmations(requests)));
        session.Error += error => _dq.TryEnqueue(() => SafeInvoke(() =>
        {
            FinalizeStreaming();
            AddErrorMessage(error);
        }));
        session.RunCompleted += () => _dq.TryEnqueue(() => SafeInvoke(FinalizeStreaming));
    }

    /// <summary>UI 线程回调安全壳：异常转为错误提示，防止 XAML 崩溃。</summary>
    private void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AgentDebugLog.Error("快捷面板 UI 回调异常", ex);
            try { AddErrorMessage($"界面处理出错：{ex.Message}"); } catch { }
        }
    }

    private void LoadLatestConversation()
    {
        var conversations = AiAssistantService.ListConversations();
        if (conversations.Count > 0)
        {
            LoadConversation(conversations[0], scrollToBottom: false);
            return;
        }

        _session?.Dispose();
        _session = CreateSession();
        AddSystemMessage("你好！我是图吧助手，可以快速提问。\n\n例如：新电脑怎么验机、电脑卡顿怎么办、最新处理器性能对比…");
    }

    private void LoadConversation(ConversationMeta meta, bool scrollToBottom = true)
    {
        _session?.Dispose();
        _session = AgentSession.Load(meta);
        HookSession(_session);
        _awaitingConfirmation = false;
        _chatList.Children.Clear();

        // 展示记录（文本 + 步骤链按原始顺序恢复）；旧会话无记录时回退到协议消息
        var display = AiAssistantService.LoadConversationDisplay(meta.Id);
        if (display.Count > 0)
        {
            foreach (var item in display)
            {
                if (item.Type == "meta") continue; // token 统计条目，不渲染
                if (item.Type == "steps")
                {
                    AddPersistedStepChain(item);
                }
                else if (item.Role == "user")
                {
                    AddUserMessage(item.Content, scrollToBottom: false);
                }
                else if (item.Role == "assistant" && !string.IsNullOrWhiteSpace(item.Content))
                {
                    AddAssistantBubble(item.Content, scrollToBottom: false);
                }
            }
        }
        else
        {
            foreach (var msg in AiAssistantService.LoadConversation(meta.Id))
            {
                if (msg.Role == "system") continue;
                if (msg.Role == "user")
                {
                    if (msg.Content.StartsWith("[ACTION_CONFIRMED]", StringComparison.OrdinalIgnoreCase)) continue;
                    if (msg.Content.StartsWith("[TOOL_RESULT]", StringComparison.OrdinalIgnoreCase)) continue;
                    AddUserMessage(msg.Content, scrollToBottom: false);
                }
                else if (msg.Role == "assistant")
                {
                    var cleanContent = msg.Content;
                    if (string.IsNullOrWhiteSpace(cleanContent)) continue;
                    AddAssistantBubble(cleanContent, scrollToBottom: false);
                }
            }
        }

        if (_chatList.Children.Count == 0)
        {
            AddSystemMessage("新对话已开始。请输入你的问题。", scrollToBottom: false);
        }

        if (scrollToBottom)
            ScrollToBottom();
    }

    /// <summary>从展示记录恢复步骤链节点（折叠态）。</summary>
    private void AddPersistedStepChain(ConversationDisplayItem item)
    {
        var chain = new StepChainControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 368
        };
        var run = new RunVm();
        foreach (var snap in item.Steps)
            run.Steps.Add(new StepRowVm(snap.ToAgentStep()));
        run.SummaryText = string.IsNullOrWhiteSpace(item.SummaryText)
            ? $"{item.Steps.Count} 步完成"
            : item.SummaryText;
        run.IsExpanded = false;
        chain.RunVm = run;
        chain.ShowCollapsed();
        _chatList.Children.Add(chain);
    }

    private void NewConversationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        _session?.Dispose();
        _session = CreateSession();
        _awaitingConfirmation = false;
        _chatList.Children.Clear();
        AddSystemMessage("新对话已开始。请输入你的问题。");
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _session?.Save();

        var flyout = new MenuFlyout();
        var conversations = AiAssistantService.ListConversations();

        if (conversations.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = "暂无历史记录",
                IsEnabled = false
            });
        }
        else
        {
            foreach (var conv in conversations.Take(20))
            {
                var item = new MenuFlyoutItem
                {
                    Text = $"{conv.Title}  ({conv.CreatedAt:MM/dd HH:mm})",
                    Tag = conv
                };
                item.Click += (_, _) => LoadConversation(conv);
                flyout.Items.Add(item);
            }
        }

        flyout.ShowAt(sender as FrameworkElement);
    }

    private void QuickQuestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string q)
        {
            InputBox.Text = q;
            SendAsync(q);
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => SendCurrentInput();

    private void InputBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.QueryText))
            SendCurrentInput();
    }

    private void SendCurrentInput()
    {
        var text = InputBox.Text?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(text))
            SendAsync(text);
    }

    private void SetInputEnabled(bool enabled)
    {
        InputBox.IsEnabled = enabled;
        SendButton.IsEnabled = enabled;
    }

    private async void SendAsync(string text)
    {
        if (_isProcessing || _awaitingConfirmation) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        _session ??= CreateSession();
        InputBox.Text = "";
        _isProcessing = true;
        SetInputEnabled(false);

        AddUserMessage(text);

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        BeginStreaming();

        try
        {
            await Task.Run(() => _session.SendAsync(text), ct);
            FinalizeStreaming();
        }
        catch (OperationCanceledException)
        {
            FinalizeStreaming();
            AddSystemMessage("已取消");
        }
        catch (Exception ex)
        {
            FinalizeStreaming();
            AddErrorMessage(AgentErrorPolicy.FormatApiError(ex));
        }
        finally
        {
            _isProcessing = false;
            SetInputEnabled(true);
            _session.Save();
        }
    }

    private void OnConfirmations(IReadOnlyList<AgentConfirmationRequest> requests)
    {
        FinalizeStreaming();
        _awaitingConfirmation = true;
        _pendingRequests = requests;

        // 复用旧确认卡片（[ACTION] 协议文本 → CreateActionCard）
        var actionContent = "[ACTION]\n" + System.Text.Json.JsonSerializer.Serialize(requests.Select(a => new
        {
            kind = a.Kind switch
            {
                "run_command" => "run_command",
                "write_reg" => "write_reg",
                "write_file" => "write_reg",
                "delete_file" => "run_command",
                "move_file" => "run_command",
                "copy_file" => "run_command",
                "download_file" => "run_command",
                "launch_tool" => "launch_tool",
                _ => "info"
            },
            description = $"{a.DisplayName}：{a.Summary}",
            detail = a.Detail,
            reason = a.Reason,
            timeout = 60
        }));

        var card = AiMarkdownRenderer.CreateActionCard(actionContent, onAllResolved: results =>
        {
            var decisions = new List<AgentConfirmationDecision>();
            for (var i = 0; i < results.Count && i < _pendingRequests?.Count; i++)
            {
                decisions.Add(new AgentConfirmationDecision
                {
                    Request = _pendingRequests[i],
                    Confirmed = results[i].confirmed
                });
            }
            _ = ResumeAfterConfirmationsAsync(decisions);
        });
        card.MaxWidth = 368;
        _chatList.Children.Add(card);
        ScrollToBottom();
    }

    private async Task ResumeAfterConfirmationsAsync(IReadOnlyList<AgentConfirmationDecision> decisions)
    {
        if (_session is null || !_awaitingConfirmation || decisions.Count == 0) return;
        _awaitingConfirmation = false;
        _isProcessing = true;
        SetInputEnabled(false);

        BeginStreaming();

        try
        {
            await Task.Run(() => _session.ResumeConfirmationsAsync(decisions));
            FinalizeStreaming();
        }
        catch (OperationCanceledException)
        {
            FinalizeStreaming();
            AddSystemMessage("已取消");
        }
        catch (Exception ex)
        {
            FinalizeStreaming();
            AddErrorMessage(AgentErrorPolicy.FormatApiError(ex));
        }
        finally
        {
            _isProcessing = false;
            SetInputEnabled(true);
            _session.Save();
        }
    }

    // ---------- 气泡构建（保持原有紧凑样式） ----------

    private void BeginStreaming()
    {
        _streamingContent = new StringBuilder();
        _streamingTb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            IsTextSelectionEnabled = true
        };

        var cursor = new Border
        {
            Width = 2,
            Height = 16,
            Background = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
        headerRow.Children.Add(_streamingTb);
        headerRow.Children.Add(cursor);

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(headerRow);

        _streamingBubble = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(12, 12, 12, 4),
            Padding = new Thickness(16, 10, 16, 10),
            MaxWidth = 368,
            Child = stack
        };

        _chatList.Children.Add(_streamingBubble);
        ScrollToBottom();
    }

    private void AppendChunk(string chunk)
    {
        if (_streamingContent is null || _streamingTb is null) return;
        _streamingContent.Append(chunk);
        _streamingTb.Text = _streamingContent.ToString();
        ScrollToBottom();
    }

    private void FinalizeStreaming()
    {
        if (_streamingBubble is null) return;
        var bubble = _streamingBubble;
        var content = _streamingContent?.ToString() ?? "";
        _streamingBubble = null;
        _streamingTb = null;
        _streamingContent = null;

        var idx = _chatList.Children.IndexOf(bubble);
        if (idx < 0) return;
        _chatList.Children.RemoveAt(idx);

        if (string.IsNullOrWhiteSpace(content)) return;

        AddAssistantBubble(content, insertAt: idx);
    }

    private void AddAssistantBubble(string content, bool scrollToBottom = true, int? insertAt = null)
    {
        var rendered = AiMarkdownRenderer.Render(content);
        rendered.MaxWidth = 368;

        var border = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(12, 12, 12, 4),
            Padding = new Thickness(16, 10, 16, 10),
            Child = rendered
        };

        if (insertAt is int idx && idx >= 0 && idx <= _chatList.Children.Count)
            _chatList.Children.Insert(idx, border);
        else
            _chatList.Children.Add(border);

        if (scrollToBottom)
            ScrollToBottom();
    }

    private void AddUserMessage(string text, bool scrollToBottom = true)
    {
        var border = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(12, 12, 4, 12),
            Padding = new Thickness(16, 10, 16, 10),
            MaxWidth = 320,
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
            }
        };

        _chatList.Children.Add(border);
        if (scrollToBottom)
            ScrollToBottom();
    }

    private void AddSystemMessage(string text, bool scrollToBottom = true)
    {
        var border = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10),
            MaxWidth = 368,
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            }
        };

        _chatList.Children.Add(border);
        if (scrollToBottom)
            ScrollToBottom();
    }

    private void AddErrorMessage(string text)
    {
        var border = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(40, 196, 43, 28)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10),
            MaxWidth = 368,
            Child = new ScrollViewer
            {
                MaxHeight = 180,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    IsTextSelectionEnabled = true,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28))
                }
            }
        };

        _chatList.Children.Add(border);
        ScrollToBottom();
    }

    private void AddToolCallIndicator(AgentStep step)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        stack.Children.Add(new FontIcon
        {
            Glyph = step.Glyph,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        });

        var displayText = string.IsNullOrWhiteSpace(step.Summary)
            ? $"调用工具：{step.DisplayName}"
            : $"{step.DisplayName}：{step.Summary}";

        stack.Children.Add(new TextBlock
        {
            Text = displayText,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 300
        });

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = stack
        };

        _chatList.Children.Add(border);
        ScrollToBottom();
    }

    private void AddToolResultIndicator(AgentStep step)
    {
        var text = step.Status == AgentStepStatus.Failed
            ? (step.Error ?? "执行失败")
            : step.Status == AgentStepStatus.Rejected
                ? "（用户已拒绝）"
                : step.Result;

        if (string.IsNullOrWhiteSpace(text)) return;

        var truncated = text.Length > 300 ? text.Substring(0, 300) + "..." : text;

        var tb = new TextBlock
        {
            Text = truncated,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            MaxHeight = 80,
            FontFamily = new FontFamily("Cascadia Code, Consolas")
        };

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(16, 0, 0, 0),
            MaxWidth = 344,
            Child = new ScrollViewer
            {
                Content = tb,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 80
            }
        };

        _chatList.Children.Add(border);
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        if (ChatScrollViewer.ScrollableHeight <= 0) return;
        ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
    }

    private void FullAssistantButton_Click(object sender, RoutedEventArgs e)
    {
        var tool = new AiAssistantTool();
        MainWindow.ActiveToolName = tool.Name;
        tool.ExecuteAsync(new BuiltinToolContext
        {
            XamlRoot = XamlRoot
        });
    }

    private void AiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.NavigateToSettings("AiApiEndpoint");
    }
}
