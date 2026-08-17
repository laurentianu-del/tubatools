using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class BuiltinToolsPage : Page
{
    private CancellationTokenSource? _activeCts;
    private CancellationTokenSource? _highlightCts;
    private string? _pendingHighlightId;
    private bool _builtinToolOpenModeInitializing;

    public BuiltinToolsPage()
    {
        InitializeComponent();
        InitBuiltinToolOpenModeComboBox();
        LoadTools();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SearchNavigationTarget target && target.HighlightBuiltinId is not null)
        {
            _pendingHighlightId = target.HighlightBuiltinId;
        }

        // 与设置页共用同一配置，导航回来时同步选择框状态
        SyncBuiltinToolOpenMode();

        if (_pendingHighlightId is not null)
        {
            StartHighlight(_pendingHighlightId);
            _pendingHighlightId = null;
        }
    }

    private void InitBuiltinToolOpenModeComboBox()
    {
        _builtinToolOpenModeInitializing = true;
        BuiltinToolOpenModeComboBox.Items.Clear();
        BuiltinToolOpenModeComboBox.Items.Add("嵌入页面");
        BuiltinToolOpenModeComboBox.Items.Add("独立窗口");
        BuiltinToolOpenModeComboBox.SelectedIndex = AppSettings.GetBool("BuiltinToolsOpenInWindow", false) ? 1 : 0;
        _builtinToolOpenModeInitializing = false;
    }

    private void SyncBuiltinToolOpenMode()
    {
        var expected = AppSettings.GetBool("BuiltinToolsOpenInWindow", false) ? 1 : 0;
        if (BuiltinToolOpenModeComboBox.SelectedIndex == expected) return;
        _builtinToolOpenModeInitializing = true;
        BuiltinToolOpenModeComboBox.SelectedIndex = expected;
        _builtinToolOpenModeInitializing = false;
    }

    private void BuiltinToolOpenModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_builtinToolOpenModeInitializing) return;
        AppSettings.Set("BuiltinToolsOpenInWindow", BuiltinToolOpenModeComboBox.SelectedIndex == 1);
    }

    private void StartHighlight(string builtinId)
    {
        _highlightCts?.Cancel();
        _highlightCts = new CancellationTokenSource();
        _ = HighlightBuiltinToolAsync(builtinId, _highlightCts.Token);
    }

    private async Task HighlightBuiltinToolAsync(string builtinId, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested) return;

        var card = FindCard(builtinId);
        if (card is null) return;

        var scrollViewer = FindParent<ScrollViewer>(card);
        if (scrollViewer is not null)
        {
            var transform = card.TransformToVisual(scrollViewer);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            var targetOffset = scrollViewer.VerticalOffset + point.Y - scrollViewer.ViewportHeight / 2 + card.ActualHeight / 2;
            targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.ScrollableHeight));
            scrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
        }

        try { await Task.Delay(600, ct); } catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested) return;

        SearchHighlightService.HighlightBorder(card);
    }

    private Border? FindCard(string builtinId)
    {
        foreach (var child in GroupsPanel.Children)
        {
            if (child is not StackPanel section) continue;
            foreach (var sectionChild in section.Children)
            {
                if (sectionChild is not GridView grid) continue;
                foreach (var item in grid.Items)
                {
                    if (item is BuiltinToolViewModel vm && vm.Id == builtinId)
                    {
                        var container = grid.ContainerFromItem(item) as GridViewItem;
                        if (container?.ContentTemplateRoot is Border border)
                            return border;
                    }
                }
            }
        }
        return null;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T result) return result;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private void LoadTools()
    {
        GroupsPanel.Children.Clear();

        var grouped = BuiltinToolRegistry.Tools
            .GroupBy(t => t.Category)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in grouped)
        {
            GroupsPanel.Children.Add(CreateGroupSection(group.Key, group.ToList()));
        }

        ToolCountText.Text = $"{BuiltinToolRegistry.Tools.Count} 个内置工具";
    }

    private UIElement CreateGroupSection(string category, List<IBuiltinTool> tools)
    {
        var section = new StackPanel { Spacing = 10 };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = category,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var countBadge = new Border
        {
            Padding = new Thickness(8, 1, 8, 2),
            CornerRadius = new CornerRadius(4),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            Child = new TextBlock
            {
                Text = tools.Count.ToString(),
                FontSize = 12,
                Opacity = 0.7
            }
        };

        Grid.SetColumn(title, 0);
        Grid.SetColumn(countBadge, 1);
        headerGrid.Children.Add(title);
        headerGrid.Children.Add(countBadge);
        section.Children.Add(headerGrid);

        var viewModels = tools.Select(t => new BuiltinToolViewModel(t)).ToList();

        var grid = new GridView
        {
            ItemsSource = viewModels,
            ItemContainerStyle = (Style)Resources["BuiltinCardStyle"],
            IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.None,
            Padding = new Thickness(0),
        };

        grid.ItemClick += BuiltinGrid_ItemClick;
        grid.SizeChanged += BuiltinGrid_SizeChanged;

        grid.ItemTemplate = CreateCompactItemTemplate();

        section.Children.Add(grid);

        return section;
    }

    private DataTemplate CreateCompactItemTemplate()
    {
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <Border
                    Padding="8,10,8,6"
                    Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                    BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                    BorderThickness="1"
                    CornerRadius="8"
                    HorizontalAlignment="Stretch"
                    VerticalAlignment="Stretch"
                    PointerEntered="BuiltinCard_PointerEntered"
                    PointerExited="BuiltinCard_PointerExited">
                    <StackPanel HorizontalAlignment="Center" Spacing="6">
                        <Border
                            Width="52"
                            Height="52"
                            HorizontalAlignment="Center"
                            Background="{ThemeResource SubtleFillColorSecondaryBrush}"
                            CornerRadius="10">
                            <FontIcon
                                FontSize="26"
                                HorizontalAlignment="Center"
                                VerticalAlignment="Center"
                                Glyph="{Binding Glyph}" />
                        </Border>
                        <TextBlock
                            HorizontalAlignment="Center"
                            FontSize="13"
                            MaxLines="2"
                            Text="{Binding Name}"
                            TextAlignment="Center"
                            TextTrimming="CharacterEllipsis"
                            TextWrapping="Wrap"
                            Width="84" />
                    </StackPanel>
                </Border>
            </DataTemplate>
            """);
    }

    private void BuiltinCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
    }

    private void BuiltinCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    }

    private void BuiltinGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BuiltinToolViewModel vm)
            _ = ExecuteToolAsync(vm);
    }

    private void BuiltinGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not GridView grid) return;
        var panel = grid.ItemsPanelRoot as ItemsWrapGrid;
        if (panel is null) return;

        double minItemWidth = 100;
        double spacing = 10;
        double availableWidth = grid.ActualWidth - grid.Padding.Left - grid.Padding.Right;
        if (availableWidth <= 0) return;

        int columns = Math.Max(1, (int)((availableWidth + spacing) / (minItemWidth + spacing)));
        double itemWidth = (availableWidth - (columns - 1) * spacing) / columns;
        panel.ItemWidth = Math.Max(minItemWidth, itemWidth);
    }

    private async Task ExecuteToolAsync(BuiltinToolViewModel vm)
    {
        _activeCts?.Cancel();
        _activeCts = new CancellationTokenSource();

        var context = new BuiltinToolContext
        {
            XamlRoot = XamlRoot,
            OnProgress = msg => DispatcherQueue.TryEnqueue(() =>
            {
                StatusBar.Title = vm.Name;
                StatusBar.Message = msg;
                StatusBar.Severity = InfoBarSeverity.Informational;
                StatusBar.IsOpen = true;
            }),
            ConfirmDownload = (toolName, description, size) => ConfirmDownloadAsync(toolName, description, size),
            CancellationToken = _activeCts.Token
        };

        try
        {
            MainWindow.ActiveToolName = vm.Name;
            await vm.Tool.ExecuteAsync(context);
            StatusBar.IsOpen = false;
        }
        catch (OperationCanceledException)
        {
            ShowStatus("已取消", vm.Name, InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowStatus("执行失败", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            MainWindow.ActiveToolName = null;
        }
    }

    private async Task<bool> ConfirmDownloadAsync(string toolName, string description, string size)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = $"即将下载「{toolName}」，是否继续？",
            TextWrapping = TextWrapping.Wrap
        });

        var secondaryBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = secondaryBrush,
                FontSize = 13
            });
        }

        if (!string.IsNullOrWhiteSpace(size))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"文件大小：{size}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = secondaryBrush,
                FontSize = 13
            });
        }

        var dialog = new ContentDialog
        {
            Title = "下载确认",
            Content = panel,
            PrimaryButtonText = "下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private DispatcherTimer? _statusBarTimer;

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;

        _statusBarTimer?.Stop();
        _statusBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusBarTimer.Tick += (s, e) =>
        {
            StatusBar.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _statusBarTimer.Start();
    }
}

public sealed class BuiltinToolViewModel
{
    public IBuiltinTool Tool { get; }

    public BuiltinToolViewModel(IBuiltinTool tool)
    {
        Tool = tool;
    }

    public string Id => Tool.Id;
    public string Name => Tool.Name;
    public string Description => Tool.Description;
    public string Glyph => Tool.Glyph;
    public string Category => Tool.Category;
    public string KindText => Tool.Kind switch
    {
        BuiltinToolKind.Dialog => "页面",
        BuiltinToolKind.BackgroundTask => "后台任务",
        BuiltinToolKind.ProgressTask => "进度任务",
        BuiltinToolKind.InstantAction => "即时操作",
        _ => "未知"
    };
}
