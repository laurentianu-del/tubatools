using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class WindowsOptimizePage : Page
{
    private List<PcSetupAction> _actions = [];
    private bool _suppressChecked;
    private bool _applying;

    public WindowsOptimizePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _actions = SystemOptimizer.GetAllOptimizeActions();
        BuildPresetCards();
        BuildOptimizeList();
        RefreshSummary();
    }

    #region Preset Cards

    private void BuildPresetCards()
    {
        PresetCardsPanel.ColumnDefinitions.Clear();
        PresetCardsPanel.Children.Clear();
        var presets = SystemOptimizer.GetVisualPresets();
        for (var i = 0; i < presets.Count; i++)
        {
            PresetCardsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var card = CreatePresetCard(presets[i], presets[i].Name == "平衡");
            Grid.SetColumn(card, i);
            PresetCardsPanel.Children.Add(card);
        }
    }

    private Border CreatePresetCard(VisualPreset preset, bool isSelected)
    {
        var card = new Border
        {
            Tag = preset.Name,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(30, 96, 165, 250))
                : new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = isSelected
                ? new SolidColorBrush(ThemeColors.AccentBlue)
                : new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1.5)
        };

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new FontIcon
        {
            Glyph = preset.Glyph,
            FontSize = 15,
            Foreground = isSelected ? new SolidColorBrush(ThemeColors.AccentBlue) : new SolidColorBrush(ThemeColors.DimText)
        });
        title.Children.Add(new TextBlock
        {
            Text = preset.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        });

        card.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                title,
                new TextBlock
                {
                    Text = preset.Description,
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(ThemeColors.DimText),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        card.PointerEntered += (_, _) =>
        {
            if (card.Tag as string != GetSelectedPreset())
                card.Background = new SolidColorBrush(ThemeColors.RowHover);
        };
        card.PointerExited += (_, _) =>
        {
            if (card.Tag as string != GetSelectedPreset())
                card.Background = new SolidColorBrush(ThemeColors.CardBg);
        };
        card.PointerPressed += (_, _) => SelectPreset(card.Tag as string ?? "");
        return card;
    }

    private string GetSelectedPreset()
    {
        if (PresetCardsPanel.Children.Count == 0) return "";
        for (var i = 0; i < PresetCardsPanel.Children.Count; i++)
        {
            if (PresetCardsPanel.Children[i] is Border b &&
                b.BorderBrush is SolidColorBrush brush &&
                brush.Color == ThemeColors.AccentBlue)
                return b.Tag as string ?? "";
        }
        return "平衡";
    }

    private void SelectPreset(string presetName)
    {
        foreach (var child in PresetCardsPanel.Children)
        {
            if (child is not Border card) continue;
            var selected = card.Tag as string == presetName;
            card.Background = selected
                ? new SolidColorBrush(Color.FromArgb(30, 96, 165, 250))
                : new SolidColorBrush(ThemeColors.CardBg);
            card.BorderBrush = selected
                ? new SolidColorBrush(ThemeColors.AccentBlue)
                : new SolidColorBrush(ThemeColors.BorderColor);
            if (card.Child is StackPanel panel && panel.Children[0] is StackPanel title && title.Children[0] is FontIcon icon)
                icon.Foreground = selected ? new SolidColorBrush(ThemeColors.AccentBlue) : new SolidColorBrush(ThemeColors.DimText);
        }
        SystemOptimizer.ApplyVisualPreset(_actions, presetName);
        BuildOptimizeList();
        RefreshSummary();
    }

    #endregion

    #region Optimize List

    private void BuildOptimizeList()
    {
        OptimizeList.Children.Clear();
        foreach (var group in _actions.GroupBy(a => a.Group))
        {
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 10, 0, 2)
            };
            header.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                VerticalAlignment = VerticalAlignment.Center
            });
            var dangerCount = group.Count(a => a.IsDangerous);
            if (dangerCount > 0)
            {
                header.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 2, 8, 2),
                    Background = new SolidColorBrush(Color.FromArgb(50, 248, 113, 113)),
                    Child = new TextBlock
                    {
                        Text = $"{dangerCount} 项高危",
                        FontSize = 10.5,
                        Foreground = new SolidColorBrush(ThemeColors.AccentRed),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }
            header.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(Color.FromArgb(30, 96, 165, 250)),
                Child = new TextBlock
                {
                    Text = group.Count().ToString(),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(ThemeColors.AccentBlue),
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            OptimizeList.Children.Add(header);

            foreach (var action in group)
                OptimizeList.Children.Add(CreateOptimizeRow(action));
        }
    }

    private Border CreateOptimizeRow(PcSetupAction action)
    {
        var cb = new CheckBox
        {
            IsChecked = action.IsSelected,
            Tag = action.Id,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        cb.Checked += async (_, _) =>
        {
            if (_suppressChecked) return;
            if (action.IsDangerous)
            {
                _suppressChecked = true;
                cb.IsChecked = false;
                _suppressChecked = false;
                var dialog = new ContentDialog
                {
                    Title = "⚠ 高危操作确认",
                    Content = $"「{action.Name}」属于高危操作：\n\n{action.Description}\n\n确定要启用此操作吗？",
                    PrimaryButtonText = "确定启用",
                    CloseButtonText = "取消",
                    XamlRoot = XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    action.IsSelected = true;
                    _suppressChecked = true;
                    cb.IsChecked = true;
                    _suppressChecked = false;
                    RefreshSummary();
                }
            }
            else
            {
                action.IsSelected = true;
                RefreshSummary();
            }
        };
        cb.Unchecked += (_, _) =>
        {
            if (_suppressChecked) return;
            action.IsSelected = false;
            RefreshSummary();
        };

        var iconBorder = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Background = action.IsDangerous
                ? new SolidColorBrush(Color.FromArgb(45, 248, 113, 113))
                : new SolidColorBrush(ThemeColors.SubtleBg),
            Child = new FontIcon
            {
                Glyph = action.Glyph,
                FontSize = 15,
                Foreground = action.IsDangerous
                    ? new SolidColorBrush(ThemeColors.AccentRed)
                    : new SolidColorBrush(ThemeColors.AccentBlue)
            }
        };

        var nameStack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        nameRow.Children.Add(new TextBlock
        {
            Text = action.Name,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (action.IsDangerous)
        {
            nameRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Background = new SolidColorBrush(Color.FromArgb(50, 248, 113, 113)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "高危",
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(ThemeColors.AccentRed)
                }
            });
        }
        nameStack.Children.Add(nameRow);
        nameStack.Children.Add(new TextBlock
        {
            Text = action.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(cb, 0);
        Grid.SetColumn(iconBorder, 1);
        Grid.SetColumn(nameStack, 2);
        grid.Children.Add(cb);
        grid.Children.Add(iconBorder);
        grid.Children.Add(nameStack);

        var row = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = action.IsDangerous
                ? new SolidColorBrush(Color.FromArgb(80, 248, 113, 113))
                : new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            Child = grid
        };
        row.PointerEntered += (_, _) => row.Background = new SolidColorBrush(ThemeColors.RowHover);
        row.PointerExited += (_, _) => row.Background = new SolidColorBrush(ThemeColors.CardBg);
        return row;
    }

    #endregion

    #region Summary & Selection

    private void RefreshSummary()
    {
        var selected = _actions.Count(a => a.IsSelected);
        var dangerous = _actions.Count(a => a.IsSelected && a.IsDangerous);
        SummaryText.Text = selected > 0
            ? $"已选择 {selected} 项优化" + (dangerous > 0 ? $"（其中高危 {dangerous} 项）" : "")
            : "勾选需要应用的优化项，或使用「推荐方案」快速选择";
        ApplyBtn.IsEnabled = selected > 0 && !_applying;
    }

    private void Recommended_Click(object sender, RoutedEventArgs e)
    {
        SystemOptimizer.ApplyRecommendedSelection(_actions);
        BuildOptimizeList();
        RefreshSummary();
    }

    private void SelectSafe_Click(object sender, RoutedEventArgs e)
    {
        SystemOptimizer.SelectAllSafe(_actions);
        BuildOptimizeList();
        RefreshSummary();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SystemOptimizer.DeselectAll(_actions);
        BuildOptimizeList();
        RefreshSummary();
    }

    #endregion

    #region Apply

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_applying) return;

        var actions = _actions.Where(a => a.IsSelected).ToList();
        if (actions.Count == 0) return;

        // 系统还原点必须最先执行，确保后续优化可回滚
        actions = [.. actions.Where(a => a.Id == "sec-restore-point"), .. actions.Where(a => a.Id != "sec-restore-point")];

        _applying = true;
        ApplyBtn.IsEnabled = false;

        var cts = new CancellationTokenSource();
        var (dialog, rowUpdaters) = BuildApplyDialog(actions, cts);
        var showTask = dialog.ShowAsync();

        var successCount = 0;
        var failCount = 0;
        var doneCount = 0;

        foreach (var action in actions)
        {
            if (cts.IsCancellationRequested) break;
            if (rowUpdaters.TryGetValue(action.Id, out var running))
                running.Text = "正在执行...";
            UpdateApplyProgress(dialog, doneCount, actions.Count, action.Name, running: true);
            var result = await action.ExecuteAsync(
                new Progress<string>(line =>
                {
                    if (rowUpdaters.TryGetValue(action.Id, out var row))
                        row.Text = line;
                }), cts.Token);
            doneCount++;
            if (result.Success) successCount++;
            else failCount++;
            if (rowUpdaters.TryGetValue(action.Id, out var status))
                status.Text = result.Success ? "✔ 完成" : $"✘ {result.Message}";
            UpdateApplyProgress(dialog, doneCount, actions.Count, action.Name, running: false);
        }

        dialog.Hide();
        await showTask;
        cts.Dispose();

        _applying = false;
        ApplyBtn.IsEnabled = true;

        var summary = $"优化完成：成功 {successCount} 项";
        if (failCount > 0) summary += $"，失败 {failCount} 项";
        var note = "";
        if (failCount == 0 && actions.Any(a => a.Id == "sec-restore-point"))
            note = "\n\n提示：部分优化需要重启电脑后生效。";
        var doneDialog = new ContentDialog
        {
            Title = successCount > 0 ? "优化完成" : "优化结果",
            Content = summary + note,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        await doneDialog.ShowAsync();
    }

    private (ContentDialog Dialog, Dictionary<string, TextBlock> StatusMap) BuildApplyDialog(
        List<PcSetupAction> actions, CancellationTokenSource cts)
    {
        var statusMap = new Dictionary<string, TextBlock>();
        var rows = new StackPanel { Spacing = 4 };
        foreach (var action in actions)
        {
            var status = new TextBlock
            {
                Text = "排队中",
                FontSize = 11,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            statusMap[action.Id] = status;

            var row = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(ThemeColors.CardBg),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                BorderThickness = new Thickness(1),
                Child = new Grid
                {
                    ColumnSpacing = 8,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }
                    },
                    Children =
                    {
                        new TextBlock
                        {
                            Text = action.Name,
                            FontSize = 12.5,
                            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        status
                    }
                }
            };
            Grid.SetColumn(status, 1);
            rows.Children.Add(row);
        }

        var scroll = new ScrollViewer
        {
            MaxHeight = 340,
            Content = rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var cancelBtn = new Button
        {
            Content = "取消剩余优化",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0)
        };
        cancelBtn.Click += (_, _) => cts.Cancel();

        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new ProgressBar { IsIndeterminate = false, Value = 0, Maximum = actions.Count },
                new TextBlock
                {
                    Text = $"共 {actions.Count} 项优化，正在应用...",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(ThemeColors.DimText)
                },
                scroll,
                cancelBtn
            }
        };

        var dialog = new ContentDialog
        {
            Title = "正在应用优化",
            Content = panel,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        return (dialog, statusMap);
    }

    private void UpdateApplyProgress(ContentDialog dialog, int done, int total, string name, bool running)
    {
        if (dialog.Content is StackPanel panel &&
            panel.Children[0] is ProgressBar bar &&
            panel.Children[1] is TextBlock info)
        {
            bar.Maximum = total;
            bar.Value = done;
            info.Text = running
                ? $"正在应用: {name} ({done}/{total})"
                : $"已完成: {name} ({done}/{total})";
        }
    }

    #endregion
}
