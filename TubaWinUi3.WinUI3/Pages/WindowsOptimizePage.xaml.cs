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
    private readonly Dictionary<PcSetupAction, ToggleSwitch> _toggleMap = [];
    private bool _loading;

    public WindowsOptimizePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _actions = SystemOptimizer.GetAllOptimizeActions();
        BuildOptimizeList();
        await LoadToggleStatesAsync();
    }

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
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
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
                OptimizeList.Children.Add(action.IsToggle
                    ? CreateToggleRow(action)
                    : CreateRunRow(action));
        }
    }

    private async Task LoadToggleStatesAsync()
    {
        _loading = true;
        try
        {
            foreach (var (action, toggle) in _toggleMap)
                toggle.IsOn = action.GetCurrentEnabled() ?? toggle.IsOn;
        }
        finally
        {
            _loading = false;
        }
    }

    private Border CreateRunRow(PcSetupAction action)
    {
        var status = new TextBlock
        {
            Text = "点击执行",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var row = BuildRow(action, status, withToggle: false);
        row.PointerPressed += async (_, _) => await ExecuteRunAsync(action, row, status);
        return row;
    }

    private Border CreateToggleRow(PcSetupAction action)
    {
        var status = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var toggle = new ToggleSwitch
        {
            OnContent = "已开启",
            OffContent = "已关闭",
            MinWidth = 120,
            IsOn = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        _toggleMap[action] = toggle;

        var row = BuildRow(action, status, withToggle: true, toggle: toggle);
        toggle.Toggled += async (_, _) => await ExecuteToggleAsync(action, row, status, toggle);
        return row;
    }

    private Border BuildRow(PcSetupAction action, TextBlock status, bool withToggle, ToggleSwitch? toggle = null)
    {
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
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
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
        if (action.IsToggle)
        {
            nameRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Background = new SolidColorBrush(Color.FromArgb(30, 96, 165, 250)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "开关",
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(ThemeColors.AccentBlue)
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        if (withToggle)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        Grid.SetColumn(iconBorder, 0);
        Grid.SetColumn(nameStack, 1);
        Grid.SetColumn(status, 2);
        grid.Children.Add(iconBorder);
        grid.Children.Add(nameStack);
        grid.Children.Add(status);
        if (withToggle && toggle is not null)
        {
            Grid.SetColumn(toggle, 3);
            grid.Children.Add(toggle);
        }

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

    #region Execute

    private async Task ExecuteRunAsync(PcSetupAction action, Border row, TextBlock status)
    {
        if (action.State == PcSetupActionState.Running) return;

        if (action.IsDangerous && !await ConfirmDangerousAsync(action)) return;

        SetRunning(row, status, "正在执行...");
        try
        {
            var result = await action.ExecuteAsync(
                new Progress<string>(line =>
                {
                    if (status.Text == "正在执行...") status.Text = line;
                }), CancellationToken.None);
            SetResult(status, result.Success, result.Message);
        }
        catch (Exception ex)
        {
            SetResult(status, false, ex.Message);
        }
        finally
        {
            row.Background = new SolidColorBrush(ThemeColors.CardBg);
        }
    }

    private async Task ExecuteToggleAsync(PcSetupAction action, Border row, TextBlock status, ToggleSwitch toggle)
    {
        if (_loading || action.State == PcSetupActionState.Running) return;

        var enabled = toggle.IsOn;
        if (enabled && action.IsDangerous && !await ConfirmDangerousAsync(action))
        {
            toggle.IsOn = false;
            return;
        }

        toggle.IsEnabled = false;
        SetRunning(row, status, enabled ? "正在启用..." : "正在还原...");
        try
        {
            var result = await action.ExecuteAsync(enabled,
                new Progress<string>(line =>
                {
                    if (status.Text is "正在启用..." or "正在还原...") status.Text = line;
                }), CancellationToken.None);
            if (result.Success)
            {
                SetResult(status, true, enabled ? "已开启" : "已还原");
            }
            else
            {
                SetResult(status, false, result.Message);
                toggle.IsOn = !enabled;
            }
        }
        catch (Exception ex)
        {
            SetResult(status, false, ex.Message);
            toggle.IsOn = !enabled;
        }
        finally
        {
            toggle.IsEnabled = true;
            row.Background = new SolidColorBrush(ThemeColors.CardBg);
        }
    }

    private async Task<bool> ConfirmDangerousAsync(PcSetupAction action)
    {
        var dialog = new ContentDialog
        {
            Title = "⚠ 高危操作确认",
            Content = $"「{action.Name}」属于高危操作：\n\n{action.Description}\n\n确定要执行此操作吗？",
            PrimaryButtonText = "确定执行",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void SetRunning(Border row, TextBlock status, string text)
    {
        row.Background = new SolidColorBrush(ThemeColors.SubtleBg);
        status.Text = text;
        status.Foreground = new SolidColorBrush(ThemeColors.AccentBlue);
    }

    private void SetResult(TextBlock status, bool success, string message)
    {
        status.Text = success ? "✔ 完成" : $"✘ {message}";
        status.Foreground = new SolidColorBrush(success
            ? ThemeColors.AccentGreen
            : ThemeColors.AccentRed);
    }

    #endregion
}
