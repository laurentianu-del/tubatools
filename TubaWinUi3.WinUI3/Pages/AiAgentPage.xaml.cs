using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TubaWinUi3.Controls.AgentChat;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Pages;

/// <summary>
/// 一条助手消息的构建状态（流式文本 / 内容宿主）。
/// </summary>
internal sealed class AssistantBubble
{
    public required Border Root { get; set; }
    public required TextBlock StreamingText { get; init; }
    public required StackPanel StreamingRow { get; init; }
    public required ContentControl ContentHost { get; init; }
}

/// <summary>
/// AI 智能代理完整页面：Agent 循环 + 代码构建消息气泡（项目已验证的可靠模式）
/// + 步骤链可视化（运行中展开、完成后自动折叠为摘要）+ 危险操作确认卡片。
/// 引擎为 AgentSession（多轮工具调用循环）。
/// </summary>
public sealed partial class AiAgentPage : UserControl
{
    private readonly DispatcherQueue _dq;

    private AgentSession? _session;
    private AssistantBubble? _streamingBubble;
    private StepChainControl? _activeChain;
    private bool _isProcessing;
    private bool _awaitingConfirmation;
    private bool _suppressToggleEvent;
    private string? _lastUserText;

    private static readonly (string Text, string Glyph)[] QuickQuestions =
    [
        ("新电脑怎么验机", "\uE950"),
        ("电脑卡顿怎么办", "\uE7E8"),
        ("内存占用过高怎么优化", "\uE8F1"),
        ("查看系统配置", "\uE770"),
    ];

    public AiAgentPage()
    {
        InitializeComponent();
        _dq = DispatcherQueue.GetForCurrentThread();

        MsgPanel.ChildrenTransitions = new TransitionCollection
        {
            new EntranceThemeTransition { FromVerticalOffset = 16, IsStaggeringEnabled = true },
            new RepositionThemeTransition()
        };

        _suppressToggleEvent = true;
        FullAccessToggle.IsOn = AgentToolContext.IsFullAccess;
        _suppressToggleEvent = false;
        UpdateTokenUsage();        BuildQuickPills();
        UpdateServiceStatus();
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

    /// <summary>页面卸载（内置工具关闭时调用）：保存并释放会话。</summary>
    public void Unload()
    {
        _session?.Dispose();
        _session = null;
    }

    // ---------- 会话 ----------

    private AgentSession CreateSession()
    {
        var session = AgentSession.CreateNew();
        HookSession(session);
        return session;
    }

    private void HookSession(AgentSession session)
    {
        session.TextChunk += chunk => _dq.TryEnqueue(() => SafeInvoke(() => AppendChunk(chunk)));
        session.StepStarted += step => _dq.TryEnqueue(() => SafeInvoke(() => OnStepStarted(step)));
        session.StepCompleted += step => _dq.TryEnqueue(() => SafeInvoke(() => OnStepCompleted(step)));
        session.ConfirmationsRequested += requests => _dq.TryEnqueue(() => SafeInvoke(() => OnConfirmations(requests)));
        session.Error += error => _dq.TryEnqueue(() => SafeInvoke(() => OnError(error)));
        session.RunCompleted += () => _dq.TryEnqueue(() => SafeInvoke(FinalizeStreaming));
        session.StepGroupCompleted += summary => _dq.TryEnqueue(() => SafeInvoke(() => OnStepGroupCompleted(summary)));
    }

    /// <summary>
    /// UI 线程事件回调的安全壳：任何异常都转为错误气泡，
    /// 防止 marshal 回调中的未处理异常导致 XAML 崩溃（0xc000027b）。
    /// </summary>
    private void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AgentDebugLog.Error("UI 回调异常", ex);
            try { AddErrorBubble($"界面处理出错：{ex.Message}"); } catch { }
        }
    }

    // ---------- 发送 / 停止 ----------

    private async void SendButton_Click(object sender, RoutedEventArgs e)
        => await SendAsync(InputBox.Text);

    private void InputBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && !e.KeyStatus.IsMenuKeyDown &&
            !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            _ = SendAsync(InputBox.Text);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
        => _session?.Cancel();

    private void FullAccessToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        AgentToolContext.IsFullAccess = FullAccessToggle.IsOn;
        if (FullAccessToggle.IsOn)
            AddSystemBubble("⚠️ 已开启完全访问模式：AI 可直接执行命令、修改注册表等操作，不再逐项确认。");
    }

    private async Task SendAsync(string text)
    {
        if (_isProcessing || _awaitingConfirmation) return;
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _session ??= CreateSession();
        _lastUserText = text;

        AddUserBubble(text);

        _streamingBubble = BeginAssistantBubble();

        _isProcessing = true;
        UpdateInputState();
        InputBox.Text = "";
        SmartScroll();

        try
        {
            await Task.Run(() => _session.SendAsync(text));
        }
        catch (OperationCanceledException)
        {
            AddSystemBubble("已取消");
        }
        catch (Exception ex)
        {
            AddErrorBubble(AgentErrorPolicy.FormatApiError(ex));
        }
        finally
        {
            _isProcessing = false;
            UpdateInputState();
            _session.Save();
            TitleText.Text = _session.Title;
            UpdateTokenUsage();
            SmartScroll();
        }
    }

    // ---------- 确认流 ----------

    private void Confirmation_Resolved(object? sender, ConfirmationResolvedEventArgs e)
        => _ = ResumeAfterConfirmationAsync(e.Decisions);

    private void Plan_Resolved(object? sender, PlanResolvedEventArgs e)
    {
        if (_session is null || !_awaitingConfirmation) return;
        if (_pendingPlanRequest is not { } request) return;

        _ = ResumeAfterConfirmationAsync(
            [new AgentConfirmationDecision { Request = request, Confirmed = e.Approved }]);
    }

    private AgentConfirmationRequest? _pendingPlanRequest;

    private async Task ResumeAfterConfirmationAsync(IReadOnlyList<AgentConfirmationDecision> decisions)
    {
        if (_session is null || !_awaitingConfirmation || decisions.Count == 0) return;
        _awaitingConfirmation = false;
        _isProcessing = true;
        UpdateInputState();

        try
        {
            await Task.Run(() => _session.ResumeConfirmationsAsync(decisions));
        }
        catch (OperationCanceledException)
        {
            AddSystemBubble("已取消");
        }
        catch (Exception ex)
        {
            AddErrorBubble(AgentErrorPolicy.FormatApiError(ex));
        }
        finally
        {
            _isProcessing = false;
            UpdateInputState();
            _session.Save();
            UpdateTokenUsage();
            SmartScroll();
        }
    }

    // ---------- 会话事件 ----------

    private void AppendChunk(string chunk)
    {
        // 步骤链之后的新文本块 → 新的助手气泡（文本与步骤按顺序交错展示）
        if (_streamingBubble is null)
        {
            _streamingBubble = BeginAssistantBubble();
        }
        _streamingBubble.StreamingText.Text += chunk;
        SmartScroll();
    }

    /// <summary>
    /// 首个工具步骤：定稿当前文本气泡，并在其后的消息流位置插入
    /// 独立的步骤链节点（执行中展开，整链完成后自动折叠为摘要）。
    /// </summary>
    private void OnStepStarted(AgentStep step)
    {
        if (_activeChain is null)
        {
            FinalizeStreaming();
            _activeChain = CreateStepChainNode();
        }
        _activeChain.RunVm.AddStep(new StepRowVm(step));
        SmartScroll();
    }

    private void OnStepCompleted(AgentStep step)
    {
        if (_activeChain is null) return;
        var row = _activeChain.RunVm.FindByCallId(step.CallId ?? "");
        row?.Update(step);
        SmartScroll();
    }

    private void OnStepGroupCompleted(AgentStepGroupSummary summary)
    {
        FinalizeStreaming();
        _activeChain?.RunVm.Complete(summary);
        _activeChain = null;
        UpdateTokenUsage();
        SmartScroll();
    }

    private void OnConfirmations(IReadOnlyList<AgentConfirmationRequest> requests)
    {
        FinalizeStreaming();
        _awaitingConfirmation = true;
        UpdateInputState();

        if (requests.Count == 1 && requests[0].Kind == "plan")
        {
            _pendingPlanRequest = requests[0];
            var planCard = new PlanCardControl
            {
                Request = requests[0],
                Margin = new Thickness(38, 0, 0, 10)
            };
            planCard.Resolved += Plan_Resolved;
            MsgPanel.Children.Add(planCard);
        }
        else
        {
            var card = new ConfirmationCardControl
            {
                Requests = requests,
                Margin = new Thickness(38, 0, 0, 10)
            };
            card.Resolved += Confirmation_Resolved;
            MsgPanel.Children.Add(card);
        }
        SmartScroll();
    }

    private void OnError(string error)
    {
        FinalizeStreaming();
        AddErrorBubble(error);
        SmartScroll();
    }

    /// <summary>流式气泡定稿：文本渲染为 markdown 内容；无文本的气泡（纯工具轮次）移除。</summary>
    private void FinalizeStreaming()
    {
        if (_streamingBubble is null) return;
        var bubble = _streamingBubble;
        _streamingBubble = null;

        var content = bubble.StreamingText.Text;
        if (!string.IsNullOrWhiteSpace(content))
        {
            var rendered = AiMarkdownRenderer.Render(content);
            rendered.MaxWidth = 860;
            bubble.ContentHost.Content = rendered;
        }
        else if (MsgPanel.Children.Contains(bubble.Root))
        {
            MsgPanel.Children.Remove(bubble.Root);
        }
        bubble.StreamingRow.Visibility = Visibility.Collapsed;
    }

    /// <summary>创建独立的步骤链节点（与文本气泡按事件顺序交错排列）。</summary>
    private StepChainControl CreateStepChainNode()
    {
        var chain = new StepChainControl
        {
            Margin = new Thickness(38, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        chain.RunVm = new RunVm();
        MsgPanel.Children.Add(chain);
        return chain;
    }

    // ---------- 历史 / 新对话 ----------

    private void LoadLatestConversation()
    {
        var conversations = AiAssistantService.ListConversations();
        if (conversations.Count > 0)
            LoadConversation(conversations[0]);
        else
            Welcome();
    }

    private void Welcome()
    {
        if (AiService.IsUsingDefaultModel)
        {
            AddSystemBubble("⚠️ 自带模型可能出现排队/限额满速，质量低下等问题。推荐使用 DeepSeek V4 Pro。\n\n你可以在 设置 → AI 服务 中配置自己的 API Key 和模型。");
        }
        AddSystemBubble("你好！我是图吧助手（智能代理版），可以帮你诊断系统问题、优化配置、执行操作、搜索最新资讯。\n\n我可以：\n- 诊断问题并**自动执行**修复操作（危险操作会先请你确认）\n- 读写文件、执行命令、下载文件\n- 联网搜索最新硬件评测、驱动、价格\n- 制定多步任务计划并逐项执行\n- 记住你的偏好与任务进度（会话记忆）\n\n试试下面的快捷问题，或直接输入你的需求！");
    }

    private void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        _session?.Dispose();
        _session = null;
        _streamingBubble = null;
        _activeChain = null;
        _awaitingConfirmation = false;
        _pendingPlanRequest = null;
        MsgPanel.Children.Clear();
        TitleText.Text = "新对话";
        Welcome();
        UpdateInputState();
        SmartScroll();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        _session?.Save();

        var flyout = new MenuFlyout();
        var conversations = AiAssistantService.ListConversations();

        if (conversations.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "暂无历史记录", IsEnabled = false });
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

    private void LoadConversation(ConversationMeta meta)
    {
        if (_isProcessing) return;

        _session?.Dispose();
        _session = AgentSession.Load(meta);
        HookSession(_session);
        _streamingBubble = null;
        _activeChain = null;
        _awaitingConfirmation = false;
        _pendingPlanRequest = null;
        MsgPanel.Children.Clear();

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
                    AddUserBubble(item.Content);
                }
                else if (item.Role == "assistant" && !string.IsNullOrWhiteSpace(item.Content))
                {
                    var bubble = BeginAssistantBubble(streaming: false);
                    var rendered = AiMarkdownRenderer.Render(item.Content);
                    rendered.MaxWidth = 860;
                    bubble.ContentHost.Content = rendered;
                    bubble.StreamingRow.Visibility = Visibility.Collapsed;
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
                    AddUserBubble(msg.Content);
                }
                else if (msg.Role == "assistant" && !string.IsNullOrWhiteSpace(msg.Content))
                {
                    var bubble = BeginAssistantBubble(streaming: false);
                    var rendered = AiMarkdownRenderer.Render(msg.Content);
                    rendered.MaxWidth = 860;
                    bubble.ContentHost.Content = rendered;
                    bubble.StreamingRow.Visibility = Visibility.Collapsed;
                }
            }
        }

        TitleText.Text = meta.Title;
        UpdateTokenUsage();
        UpdateInputState();
        SmartScroll();
    }

    /// <summary>从展示记录恢复步骤链节点（折叠态，可点击展开查看每步详情）。</summary>
    private void AddPersistedStepChain(ConversationDisplayItem item)
    {
        var chain = new StepChainControl
        {
            Margin = new Thickness(38, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
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
        MsgPanel.Children.Add(chain);
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastUserText is { Length: > 0 } text)
            await SendAsync(text);
    }

    // ---------- 气泡构建 ----------

    private void AddUserBubble(string text)
    {
        var bubble = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(12, 12, 4, 12),
            Padding = new Thickness(14, 9, 14, 9),
            MaxWidth = 520,
            Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"],
                IsTextSelectionEnabled = true
            }
        };
        MsgPanel.Children.Add(bubble);
    }

    private AssistantBubble BeginAssistantBubble(bool streaming = true)
    {
        // 头像
        var avatar = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Top,
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Child = new FontIcon
            {
                Glyph = "\uE946",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
            }
        };

        // 名称行
        var nameRow = new TextBlock
        {
            Text = "图吧助手",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        // 流式文本 + 思考指示
        var streamingText = new TextBlock
        {
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        var thinkingRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new ProgressRing { Width = 14, Height = 14, IsActive = true },
                new TextBlock
                {
                    Text = streaming ? "正在思考…" : "",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                }
            }
        };
        var streamingRow = new StackPanel { Spacing = 4, Children = { streamingText, thinkingRow } };

        // 最终内容宿主
        var contentHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        var bubble = new AssistantBubble
        {
            Root = null!,
            StreamingText = streamingText,
            StreamingRow = streamingRow,
            ContentHost = contentHost
        };

        var body = new StackPanel
        {
            Spacing = 6,
            Children = { nameRow, streamingRow, contentHost }
        };

        var grid = new Grid
        {
            ColumnSpacing = 10,
            Margin = new Thickness(0, 0, 0, 12)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(body, 1);
        grid.Children.Add(avatar);
        grid.Children.Add(body);

        var root = new Border
        {
            Child = grid,
            Margin = new Thickness(0, 0, 0, 2)
        };
        bubble.Root = root;

        MsgPanel.Children.Add(root);
        return bubble;
    }

    private void AddSystemBubble(string text)
    {
        var bubble = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10),
            MaxWidth = 700,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            }
        };
        MsgPanel.Children.Add(bubble);
    }

    private void AddErrorBubble(string text)
    {
        var retryBtn = new Button
        {
            Content = "重试",
            FontSize = 12,
            Padding = new Thickness(12, 4, 12, 4),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        retryBtn.Click += RetryButton_Click;

        var bubble = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10),
            MaxWidth = 700,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new FontIcon
                            {
                                Glyph = "\uE783",
                                FontSize = 13,
                                Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                            },
                            new TextBlock
                            {
                                Text = "出错了",
                                FontSize = 12,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                            }
                        }
                    },
                    // 详细错误（异常链）可滚动查看，不占大块版面
                    new ScrollViewer
                    {
                        MaxHeight = 240,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new TextBlock
                        {
                            Text = text,
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap,
                            IsTextSelectionEnabled = true,
                            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                        }
                    },
                    retryBtn
                }
            }
        };
        MsgPanel.Children.Add(bubble);
    }

    // ---------- UI 辅助 ----------

    private void BuildQuickPills()
    {
        var index = 0;
        foreach (var (text, glyph) in QuickQuestions)
        {
            var btn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    Children =
                    {
                        new FontIcon { Glyph = glyph, FontSize = 11 },
                        new TextBlock { Text = text, FontSize = 12 }
                    }
                },
                Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 5, 12, 5),
                Tag = text
            };
            btn.Click += (_, _) => _ = SendAsync(text);
            QuickPillPanel.Children.Add(btn);

            // 错落入场动画（间隔 50ms）
            btn.RenderTransform = new TranslateTransform();
            var sb = new Storyboard { BeginTime = TimeSpan.FromMilliseconds(index++ * 50) };
            var opacity = new DoubleAnimation
            {
                From = 0, To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(opacity, btn);
            Storyboard.SetTargetProperty(opacity, "Opacity");
            var translate = new DoubleAnimation
            {
                From = 10, To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(translate, btn);
            Storyboard.SetTargetProperty(translate, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(opacity);
            sb.Children.Add(translate);
            sb.Begin();
        }
    }

    private void UpdateServiceStatus()
    {
        if (AiService.IsUsingDefaultModel)
        {
            ModelStatusText.Text = "默认模型（建议配置自定义 AI 服务）";
            ModelStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        }
        else
        {
            var (_, model, _) = AiService.GetConfig();
            ModelStatusText.Text = $"已连接 AI 服务 · {model}";
            ModelStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }
    }

    private void UpdateInputState()
    {
        var busy = _isProcessing || _awaitingConfirmation;
        InputBox.IsEnabled = !busy;
        SendButton.IsEnabled = !busy;
        SendButton.Visibility = _isProcessing ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = _isProcessing ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>发送按钮旁的气泡：实时展示本会话累计 token 消耗（多轮累加）。</summary>
    private void UpdateTokenUsage()
    {
        var tokens = _session?.TotalTokens ?? 0;
        if (tokens <= 0)
        {
            TokenUsageBubble.Visibility = Visibility.Collapsed;
            return;
        }
        TokenUsageBubble.Visibility = Visibility.Visible;
        TokenUsageText.Text = $"已消耗 {FormatTokens(tokens)} tokens";
    }

    private static string FormatTokens(int tokens)
        => tokens >= 1000 ? $"{tokens / 1000.0:F1}k" : tokens.ToString();

    private void SmartScroll()
    {
        var sv = MsgScroll;
        if (sv.ScrollableHeight <= 0) return;
        var distFromBottom = sv.ScrollableHeight - sv.VerticalOffset;
        if (distFromBottom < 140)
            sv.ChangeView(null, sv.ScrollableHeight, null);
    }
}
