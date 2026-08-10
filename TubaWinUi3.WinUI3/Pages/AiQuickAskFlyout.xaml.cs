using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class AiQuickAskFlyout : UserControl
{
    private readonly DispatcherQueue _dq;
    private readonly StackPanel _chatList;
    private readonly List<AiChatMessage> _history = [];
    private CancellationTokenSource _cts = new();
    private bool _isProcessing;
    private Border? _streamingBubble;
    private TextBlock? _streamingTb;
    private StringBuilder? _streamingContent;
    private string _conversationId = Guid.NewGuid().ToString("N")[..12];
    private string _conversationTitle = "新对话";

    public AiQuickAskFlyout()
    {
        InitializeComponent();
        _dq = DispatcherQueue.GetForCurrentThread();
        _chatList = ChatList;

        UpdateServiceStatus();
        LoadLatestConversation();
    }

    private void UpdateServiceStatus()
    {
        if (AiService.IsUsingDefaultModel)
        {
            ServiceStatusText.Text = "自带模型（可在设置中配置自定义 AI 服务）";
        }
        else
        {
            var (endpoint, model, _) = AiService.GetConfig();
            ServiceStatusText.Text = $"已连接 AI 服务 · {model}";
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

        _history.Clear();
        _conversationId = Guid.NewGuid().ToString("N")[..12];
        _conversationTitle = "新对话";
        AddSystemMessage("你好！我是图吧助手，可以快速提问。\n\n例如：新电脑怎么验机、电脑卡顿怎么办、最新处理器性能对比…");
    }

    private void LoadConversation(ConversationMeta meta, bool scrollToBottom = true)
    {
        var messages = AiAssistantService.LoadConversation(meta.Id);

        _conversationId = meta.Id;
        _conversationTitle = meta.Title;
        _history.Clear();
        _history.AddRange(messages);

        _chatList.Children.Clear();

        foreach (var msg in messages)
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

        if (_chatList.Children.Count == 0)
        {
            AddSystemMessage("新对话已开始。请输入你的问题。", scrollToBottom: false);
        }

        if (scrollToBottom)
            ScrollToBottom();
    }

    private void NewConversationButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentConversation();
        _history.Clear();
        _conversationId = Guid.NewGuid().ToString("N")[..12];
        _conversationTitle = "新对话";
        _chatList.Children.Clear();
        AddSystemMessage("新对话已开始。请输入你的问题。");
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentConversation();

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

    private void SaveCurrentConversation()
    {
        if (_history.Count == 0) return;
        try
        {
            AiAssistantService.SaveConversation(
                _conversationId,
                _conversationTitle,
                _history);
        }
        catch { }
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
        if (_isProcessing) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        InputBox.Text = "";
        _isProcessing = true;
        SetInputEnabled(false);

        AddUserMessage(text);

        if (_history.Count <= 1)
        {
            _conversationTitle = text.Length > 30 ? text.Substring(0, 30) + "..." : text;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        BeginStreaming();

        try
        {
            await Task.Run(async () =>
            {
                await AiAssistantService.ProcessUserMessageStreamAsync(
                    text,
                    _history,
                    onTextChunk: chunk => _dq.TryEnqueue(() => AppendChunk(chunk)),
                    onToolCall: toolInfo => _dq.TryEnqueue(() => AddToolCallIndicator(toolInfo)),
                    onToolResult: result => _dq.TryEnqueue(() => AddToolResultIndicator(result)),
                    onActions: actions =>
                    {
                        _dq.TryEnqueue(() =>
                        {
                            FinalizeStreaming();
                            var actionContent = "[ACTION]\n" + System.Text.Json.JsonSerializer.Serialize(actions.Select(a => new
                            {
                                kind = a.Kind switch
                                {
                                    AiActionKind.RunCommand => "run_command",
                                    AiActionKind.ModifyConfig => "write_reg",
                                    AiActionKind.LaunchTool => "launch_tool",
                                    AiActionKind.ReadConfig => "read_reg",
                                    _ => "info"
                                },
                                description = a.Description,
                                detail = a.Detail,
                                reason = a.Reason,
                                timeout = a.TimeoutSeconds
                            }));
                            var card = AiMarkdownRenderer.CreateActionCard(actionContent, onAllResolved: ContinueAfterActions);
                            card.MaxWidth = 368;
                            _chatList.Children.Add(card);
                            ScrollToBottom();
                        });
                    },
                    onToolRecommendations: _ => { },
                    onError: error => _dq.TryEnqueue(() =>
                    {
                        FinalizeStreaming();
                        AddErrorMessage(error);
                    }),
                    ct: ct);
            }, ct);

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
            AddErrorMessage($"发生错误：{ex.Message}");
        }
        finally
        {
            _isProcessing = false;
            SetInputEnabled(true);
            SaveCurrentConversation();
        }
    }

    private async void ContinueAfterActions(List<(AiActionStep action, bool confirmed, string result)> results)
    {
        if (_isProcessing) return;
        _isProcessing = true;
        SetInputEnabled(false);

        var sb = new StringBuilder();
        foreach (var (action, confirmed, result) in results)
        {
            if (confirmed)
            {
                sb.AppendLine($"✓ 已确认执行：{action.Description}");
                sb.AppendLine($"执行结果：\n{result}");
            }
            else
            {
                sb.AppendLine($"✗ 用户拒绝执行：{action.Description}");
            }
            sb.AppendLine();
        }

        _history.Add(AiChatMessage.User($"[ACTION_CONFIRMED]\n{sb}"));

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        BeginStreaming();

        try
        {
            await Task.Run(async () =>
            {
                await AiAssistantService.ContinueConversationStreamAsync(
                    _history,
                    onTextChunk: chunk => _dq.TryEnqueue(() => AppendChunk(chunk)),
                    onToolCall: toolInfo => _dq.TryEnqueue(() => AddToolCallIndicator(toolInfo)),
                    onToolResult: result => _dq.TryEnqueue(() => AddToolResultIndicator(result)),
                    onActions: actions =>
                    {
                        _dq.TryEnqueue(() =>
                        {
                            FinalizeStreaming();
                            var actionContent = "[ACTION]\n" + System.Text.Json.JsonSerializer.Serialize(actions.Select(a => new
                            {
                                kind = a.Kind switch
                                {
                                    AiActionKind.RunCommand => "run_command",
                                    AiActionKind.ModifyConfig => "write_reg",
                                    AiActionKind.LaunchTool => "launch_tool",
                                    AiActionKind.ReadConfig => "read_reg",
                                    _ => "info"
                                },
                                description = a.Description,
                                detail = a.Detail,
                                reason = a.Reason,
                                timeout = a.TimeoutSeconds
                            }));
                            var card = AiMarkdownRenderer.CreateActionCard(actionContent, onAllResolved: ContinueAfterActions);
                            card.MaxWidth = 368;
                            _chatList.Children.Add(card);
                            ScrollToBottom();
                        });
                    },
                    onToolRecommendations: _ => { },
                    onError: error => _dq.TryEnqueue(() =>
                    {
                        FinalizeStreaming();
                        AddErrorMessage(error);
                    }),
                    ct: ct);
            }, ct);

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
            AddErrorMessage($"发生错误：{ex.Message}");
        }
        finally
        {
            _isProcessing = false;
            SetInputEnabled(true);
            SaveCurrentConversation();
        }
    }

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
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28))
            }
        };

        _chatList.Children.Add(border);
        ScrollToBottom();
    }

    private void AddToolCallIndicator(string toolInfo)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        stack.Children.Add(new FontIcon
        {
            Glyph = "\uE74C",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        });

        var isSearch = toolInfo.Contains("web_search", StringComparison.OrdinalIgnoreCase);
        var displayText = isSearch ? $"搜索：{ExtractQueryFromToolInfo(toolInfo)}" : $"调用工具：{toolInfo}";

        stack.Children.Add(new TextBlock
        {
            Text = displayText,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
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

    private void AddToolResultIndicator(string result)
    {
        var truncated = result.Length > 300 ? result.Substring(0, 300) + "..." : result;

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

    private static string ExtractQueryFromToolInfo(string toolInfo)
    {
        var idx = toolInfo.IndexOf("query=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return toolInfo;
        var start = idx + "query=".Length;
        var end = toolInfo.IndexOf('|', start);
        if (end < 0) end = toolInfo.Length;
        return toolInfo.Substring(start, end - start).Trim();
    }

    private void ScrollToBottom()
    {
        if (ChatScrollViewer.ScrollableHeight <= 0) return;
        ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
    }

    private void FullAssistantButton_Click(object sender, RoutedEventArgs e)
    {
        var tool = new AiAssistantTool();
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
