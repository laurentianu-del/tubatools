using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Services;

public static partial class AiMarkdownRenderer
{
    public static StackPanel Render(string markdown)
    {
        var container = new StackPanel { Spacing = 4 };

        var blocks = SplitBlocks(markdown);

        foreach (var block in blocks)
        {
            if (block.Type == BlockType.ToolRecommend)
            {
                container.Children.Add(CreateToolCard(block.Content));
            }
            else if (block.Type == BlockType.Website)
            {
                container.Children.Add(CreateWebsiteCard(block.Content));
            }
            else if (block.Type == BlockType.Setting)
            {
                container.Children.Add(CreateSettingCard(block.Content));
            }
            else if (block.Type == BlockType.Action)
            {
                container.Children.Add(CreateActionCard(block.Content));
            }
            else if (block.Type == BlockType.CodeBlock)
            {
                var rtb = new RichTextBlock { TextWrapping = TextWrapping.Wrap };
                MarkdownTextService.RenderToRichTextBlock(rtb, block.Content);
                container.Children.Add(rtb);
            }
            else
            {
                var rtb = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                MarkdownTextService.RenderToRichTextBlock(rtb, block.Content);
                container.Children.Add(rtb);
            }
        }

        return container;
    }

    private static List<MarkdownBlock> SplitBlocks(string markdown)
    {
        var blocks = new List<MarkdownBlock>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var textBuffer = new System.Text.StringBuilder();

        void FlushText()
        {
            if (textBuffer.Length > 0)
            {
                var text = textBuffer.ToString().TrimEnd('\n');
                if (!string.IsNullOrWhiteSpace(text))
                {
                    blocks.AddRange(SplitTextWithActionAndCode(text));
                }
                textBuffer.Clear();
            }
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var remaining = line;

            while (remaining.Length > 0)
            {
                var recIdx = remaining.IndexOf("[RECOMMEND_TOOL]", StringComparison.OrdinalIgnoreCase);
                var webIdx = remaining.IndexOf("[WEBSITE]", StringComparison.OrdinalIgnoreCase);
                var setIdx = remaining.IndexOf("[SETTING]", StringComparison.OrdinalIgnoreCase);

                var firstIdx = -1;
                if (recIdx >= 0 && (firstIdx < 0 || recIdx < firstIdx)) firstIdx = recIdx;
                if (webIdx >= 0 && (firstIdx < 0 || webIdx < firstIdx)) firstIdx = webIdx;
                if (setIdx >= 0 && (firstIdx < 0 || setIdx < firstIdx)) firstIdx = setIdx;

                if (firstIdx < 0)
                {
                    textBuffer.AppendLine(remaining);
                    break;
                }

                if (firstIdx > 0)
                {
                    textBuffer.AppendLine(remaining.Substring(0, firstIdx));
                }

                FlushText();

                var tag = remaining.Substring(firstIdx);
                if (tag.StartsWith("[RECOMMEND_TOOL]", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = tag.IndexOf('\n');
                    var tagContent = endIdx >= 0 ? tag.Substring(0, endIdx) : tag;
                    blocks.Add(new MarkdownBlock(BlockType.ToolRecommend, tagContent.Trim()));
                    remaining = endIdx >= 0 ? tag.Substring(endIdx + 1) : "";
                }
                else if (tag.StartsWith("[WEBSITE]", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = tag.IndexOf('\n');
                    var tagContent = endIdx >= 0 ? tag.Substring(0, endIdx) : tag;
                    blocks.Add(new MarkdownBlock(BlockType.Website, tagContent.Trim()));
                    remaining = endIdx >= 0 ? tag.Substring(endIdx + 1) : "";
                }
                else if (tag.StartsWith("[SETTING]", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = tag.IndexOf('\n');
                    var tagContent = endIdx >= 0 ? tag.Substring(0, endIdx) : tag;
                    blocks.Add(new MarkdownBlock(BlockType.Setting, tagContent.Trim()));
                    remaining = endIdx >= 0 ? tag.Substring(endIdx + 1) : "";
                }
                else
                {
                    textBuffer.AppendLine(remaining);
                    break;
                }
            }
        }

        FlushText();
        return blocks;
    }

    private static List<MarkdownBlock> SplitTextWithActionAndCode(string markdown)
    {
        var blocks = new List<MarkdownBlock>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        var currentLines = new List<string>();

        void FlushText()
        {
            if (currentLines.Count > 0)
            {
                blocks.Add(new MarkdownBlock(BlockType.Text, string.Join("\n", currentLines)));
                currentLines.Clear();
            }
        }

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("[ACTION]", StringComparison.OrdinalIgnoreCase))
            {
                FlushText();
                var actionSb = new System.Text.StringBuilder();
                actionSb.AppendLine(line);
                i++;
                while (i < lines.Length)
                {
                    if (lines[i].TrimStart().StartsWith("```"))
                    {
                        actionSb.AppendLine(lines[i]);
                        i++;
                        while (i < lines.Length)
                        {
                            actionSb.AppendLine(lines[i]);
                            if (lines[i].TrimStart().StartsWith("```")) { i++; break; }
                            i++;
                        }
                        continue;
                    }
                    if (!lines[i].TrimStart().StartsWith("[") &&
                        !lines[i].TrimStart().StartsWith("{") &&
                        !lines[i].TrimStart().StartsWith("}") &&
                        !lines[i].TrimStart().StartsWith("\"") &&
                        !lines[i].TrimStart().StartsWith(",") &&
                        !string.IsNullOrWhiteSpace(lines[i]))
                    {
                        break;
                    }
                    actionSb.AppendLine(lines[i]);
                    i++;
                }
                blocks.Add(new MarkdownBlock(BlockType.Action, actionSb.ToString().TrimEnd()));
            }
            else if (trimmed.StartsWith("```"))
            {
                FlushText();
                var codeSb = new System.Text.StringBuilder();
                codeSb.AppendLine(line);
                i++;
                while (i < lines.Length)
                {
                    codeSb.AppendLine(lines[i]);
                    if (lines[i].TrimStart().StartsWith("```")) { i++; break; }
                    i++;
                }
                blocks.Add(new MarkdownBlock(BlockType.CodeBlock, codeSb.ToString().TrimEnd()));
            }
            else
            {
                currentLines.Add(line);
                i++;
            }
        }

        FlushText();
        return blocks;
    }

    private static Border CreateToolCard(string line)
    {
        var after = line.Substring("[RECOMMEND_TOOL]".Length).Trim();
        var pipeIdx = after.IndexOf('|');
        string name, reason;
        if (pipeIdx >= 0)
        {
            name = after.Substring(0, pipeIdx).Trim();
            var rest = after.Substring(pipeIdx + 1).Trim();
            reason = ParseArg(rest, "reason");
            if (string.IsNullOrWhiteSpace(reason)) reason = rest;
        }
        else
        {
            name = after.Trim();
            reason = "";
        }

        var toolPath = "";
        var isBuiltin = false;
        var builtinId = "";

        try
        {
            var allTools = ToolCatalog.GetAllToolsCached();
            var tool = allTools.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (tool is not null)
            {
                toolPath = tool.EffectivePath;
            }
            else
            {
                var builtin = BuiltinToolRegistry.Tools.FirstOrDefault(t =>
                    t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                if (builtin is not null)
                {
                    isBuiltin = true;
                    builtinId = builtin.Id;
                }
            }
        }
        catch { }

        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(14, 10, 14, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = "\uE8F1",
            FontSize = 18,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(icon); Grid.SetColumn(icon, 0);

        var infoStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoStack.Children.Add(new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        });
        if (!string.IsNullOrWhiteSpace(reason))
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = reason,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }
        grid.Children.Add(infoStack); Grid.SetColumn(infoStack, 1);

        if (!string.IsNullOrWhiteSpace(toolPath))
        {
            var launchBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE72A", FontSize = 11 },
                        new TextBlock { Text = "打开", FontSize = 12 }
                    }
                },
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(6),
                Tag = toolPath
            };
            launchBtn.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo((string)launchBtn.Tag) { UseShellExecute = true });
                    launchBtn.Content = new TextBlock { Text = "已打开", FontSize = 12 };
                    launchBtn.IsEnabled = false;
                }
                catch { }
            };
            grid.Children.Add(launchBtn); Grid.SetColumn(launchBtn, 2);
        }
        else if (isBuiltin)
        {
            var tip = new TextBlock
            {
                Text = "内置工具",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(tip); Grid.SetColumn(tip, 2);
        }
        else
        {
            var tip = new TextBlock
            {
                Text = "未安装",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(tip); Grid.SetColumn(tip, 2);
        }

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(8),
            Child = grid
        };
    }

    private static Border CreateWebsiteCard(string line)
    {
        var after = line.Substring("[WEBSITE]".Length).Trim();
        var pipeIdx = after.IndexOf('|');
        string url, desc;
        if (pipeIdx >= 0)
        {
            url = after.Substring(0, pipeIdx).Trim();
            var rest = after.Substring(pipeIdx + 1).Trim();
            desc = ParseArg(rest, "desc");
            if (string.IsNullOrWhiteSpace(desc)) desc = rest;
        }
        else
        {
            url = after.Trim();
            desc = "";
        }

        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(14, 10, 14, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = "\uE774",
            FontSize = 18,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212)),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(icon); Grid.SetColumn(icon, 0);

        var infoStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrWhiteSpace(desc))
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = desc,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13
            });
        }
        infoStack.Children.Add(new TextBlock
        {
            Text = url,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        });
        grid.Children.Add(infoStack); Grid.SetColumn(infoStack, 1);

        var openBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE71B", FontSize = 11 },
                    new TextBlock { Text = "访问", FontSize = 12 }
                }
            },
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(6),
            Tag = url
        };
        openBtn.Click += (_, _) =>
        {
            try { Pages.BrowserWindow.Open((string)openBtn.Tag); } catch { }
        };
        grid.Children.Add(openBtn); Grid.SetColumn(openBtn, 2);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(8),
            Child = grid
        };
    }

    private static Border CreateSettingCard(string line)
    {
        var after = line.Substring("[SETTING]".Length).Trim();

        string path = ParseArg(after, "path");
        string name = ParseArg(after, "name");
        string current = ParseArg(after, "current");
        string recommend = ParseArg(after, "recommend");
        string reason = ParseArg(after, "reason");

        if (string.IsNullOrWhiteSpace(name)) name = path;

        var stack = new StackPanel { Spacing = 4, Padding = new Thickness(14, 10, 14, 10) };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(new FontIcon
        {
            Glyph = "\uE77B",
            FontSize = 14,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 218, 112, 0)),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(header);

        if (!string.IsNullOrWhiteSpace(current))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"当前值：{current}",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }

        if (!string.IsNullOrWhiteSpace(recommend))
        {
            var recStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            recStack.Children.Add(new TextBlock
            {
                Text = $"建议修改为：{recommend}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212))
            });
            stack.Children.Add(recStack);
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"理由：{reason}",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            stack.Children.Add(new TextBlock
            {
                Text = path,
                FontSize = 11,
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(8),
            Child = stack
        };
    }

    internal static Border CreateActionCard(string content, Action<AiActionStep, string>? onConfirmed = null)
    {
        var idx = content.IndexOf("[ACTION]", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            content = content.Substring(idx + "[ACTION]".Length).Trim();

        var stack = new StackPanel { Spacing = 4, Padding = new Thickness(14, 10, 14, 10) };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(new FontIcon
        {
            Glyph = "\uE7BA",
            FontSize = 14,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 218, 112, 0)),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = "需要确认的操作",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 218, 112, 0)),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(header);

        var actions = ParseActionJson(content);
        foreach (var action in actions)
        {
            var kindLabel = action.Kind switch
            {
                AiActionKind.RunCommand => "执行命令",
                AiActionKind.ModifyConfig => "修改配置",
                AiActionKind.ReadConfig => "读取配置",
                AiActionKind.LaunchTool => "启动工具",
                _ => "操作"
            };

            var actionStack = new StackPanel { Spacing = 2, Margin = new Thickness(24, 6, 0, 0) };
            actionStack.Children.Add(new TextBlock
            {
                Text = $"{kindLabel}：{action.Description}",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13
            });

            if (!string.IsNullOrWhiteSpace(action.Detail))
            {
                actionStack.Children.Add(new TextBlock
                {
                    Text = $"详情：{action.Detail}",
                    FontSize = 12,
                    FontFamily = new FontFamily("Cascadia Code, Consolas"),
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (!string.IsNullOrWhiteSpace(action.Reason))
            {
                actionStack.Children.Add(new TextBlock
                {
                    Text = $"理由：{action.Reason}",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    TextWrapping = TextWrapping.Wrap
                });
            }

            var confirmBtn = new Button
            {
                Content = "确认执行",
                FontSize = 12,
                Padding = new Thickness(12, 4, 12, 4),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 4, 0, 0),
                Tag = action
            };

            confirmBtn.Click += async (_, _) =>
            {
                confirmBtn.IsEnabled = false;
                confirmBtn.Content = "执行中...";
                try
                {
                    var result = await AiAssistantService.ExecuteActionAsync(action, CancellationToken.None);
                    action.Executed = true;
                    confirmBtn.Content = "已执行 ✓";
                    onConfirmed?.Invoke(action, result);
                }
                catch
                {
                    confirmBtn.Content = "执行失败";
                }
            };

            actionStack.Children.Add(confirmBtn);
            stack.Children.Add(actionStack);
        }

        if (actions.Count == 0)
        {
            var tb = new TextBlock
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            stack.Children.Add(tb);
        }

        return new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 218, 112, 0)),
            CornerRadius = new CornerRadius(8),
            Child = stack
        };
    }

    private static List<AiActionStep> ParseActionJson(string content)
    {
        var result = new List<AiActionStep>();
        var jsonStart = content.IndexOf('[');
        var jsonEnd = content.LastIndexOf(']');
        if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart) return result;

        var json = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                var kindStr = elem.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
                var kind = kindStr switch
                {
                    "run_command" => AiActionKind.RunCommand,
                    "write_reg" => AiActionKind.ModifyConfig,
                    "modify_config" => AiActionKind.ModifyConfig,
                    "launch_tool" => AiActionKind.LaunchTool,
                    "read_config" => AiActionKind.ReadConfig,
                    "read_reg" => AiActionKind.ReadConfig,
                    _ => AiActionKind.Info
                };

                result.Add(new AiActionStep
                {
                    Kind = kind,
                    Description = elem.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    Detail = elem.TryGetProperty("detail", out var dt) ? dt.GetString() ?? "" :
                            elem.TryGetProperty("cmd", out var cmd) ? cmd.GetString() ?? "" : "",
                    Reason = elem.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "",
                });
            }
        }
        catch { }
        return result;
    }

    private static string ParseArg(string args, string key)
    {
        var pattern = key + "=";
        var idx = args.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var start = idx + pattern.Length;
        var end = args.IndexOf('|', start);
        if (end < 0) end = args.Length;
        return args.Substring(start, end - start).Trim();
    }

    private enum BlockType
    {
        Text,
        CodeBlock,
        ToolRecommend,
        Website,
        Setting,
        Action
    }

    private sealed class MarkdownBlock(BlockType type, string content)
    {
        public BlockType Type { get; } = type;
        public string Content { get; } = content;
    }
}
