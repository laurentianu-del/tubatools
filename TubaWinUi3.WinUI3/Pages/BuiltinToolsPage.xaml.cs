using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.ObjectModel;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class BuiltinToolsPage : Page
{
    private readonly ObservableCollection<BuiltinToolViewModel> _tools = [];
    private CancellationTokenSource? _activeCts;
    private CancellationTokenSource? _highlightCts;
    private string? _pendingHighlightId;

    public BuiltinToolsPage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
        PopulateCategoryFilter();
        LoadTools(null);

    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SearchNavigationTarget target && target.HighlightBuiltinId is not null)
        {
            _pendingHighlightId = target.HighlightBuiltinId;
        }

        if (_pendingHighlightId is not null)
        {
            StartHighlight(_pendingHighlightId);
            _pendingHighlightId = null;
        }
    }

    private void StartHighlight(string builtinId)
    {
        _highlightCts?.Cancel();
        _highlightCts = new CancellationTokenSource();
        _ = HighlightBuiltinToolAsync(builtinId, _highlightCts.Token);
    }

    private async Task HighlightBuiltinToolAsync(string builtinId, CancellationToken ct)
    {
        var vm = _tools.FirstOrDefault(t => t.Id.Equals(builtinId, StringComparison.OrdinalIgnoreCase));
        if (vm is null || ct.IsCancellationRequested) return;

        ToolsGrid.ScrollIntoView(vm);
        try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return; }

        var container = ToolsGrid.ContainerFromItem(vm) as GridViewItem;
        if (container is null || ct.IsCancellationRequested) return;

        var scrollViewer = FindChildScrollViewer(ToolsGrid);
        if (scrollViewer is not null)
        {
            var transform = container.TransformToVisual(scrollViewer.Content as UIElement ?? scrollViewer);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            var targetOffset = scrollViewer.VerticalOffset + point.Y - scrollViewer.ViewportHeight / 2 + container.ActualHeight / 2;
            targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.ExtentHeight - scrollViewer.ViewportHeight));
            scrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
            try { await Task.Delay(600, ct); } catch (OperationCanceledException) { return; }
        }

        if (ct.IsCancellationRequested) return;

        var border = FindChildBorder(container);
        if (border is not null)
            SearchHighlightService.HighlightBorder(border);
    }

    private static ScrollViewer? FindChildScrollViewer(DependencyObject parent)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            var result = FindChildScrollViewer(child);
            if (result is not null) return result;
        }
        return null;
    }

    private static Border? FindChildBorder(DependencyObject parent)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Border b) return b;
            var result = FindChildBorder(child);
            if (result is not null) return result;
        }
        return null;
    }

    private void ToolsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var panel = ToolsGrid.ItemsPanelRoot as ItemsWrapGrid;
        if (panel is null) return;

        double minItemWidth = 280;
        double spacing = 12;
        double availableWidth = ToolsGrid.ActualWidth - ToolsGrid.Padding.Left - ToolsGrid.Padding.Right;

        if (availableWidth <= 0) return;

        int columns = Math.Max(1, (int)((availableWidth + spacing) / (minItemWidth + spacing)));
        double itemWidth = (availableWidth - (columns - 1) * spacing) / columns;
        panel.ItemWidth = Math.Max(minItemWidth, itemWidth);
    }

    private void PopulateCategoryFilter()
    {
        CategoryFilter.Items.Add("全部分类");
        foreach (var category in BuiltinToolRegistry.GetCategories())
        {
            CategoryFilter.Items.Add(category);
        }
        CategoryFilter.SelectedIndex = 0;
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = CategoryFilter.SelectedItem as string;
        LoadTools(selected == "全部分类" ? null : selected);
    }

    private void LoadTools(string? category)
    {
        _tools.Clear();
        var tools = category is null
            ? BuiltinToolRegistry.Tools
            : BuiltinToolRegistry.GetByCategory(category);

        foreach (var tool in tools)
        {
            _tools.Add(new BuiltinToolViewModel(tool));
        }

        ToolCountText.Text = $"{_tools.Count} 个内置工具";
    }

    private void ToolsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BuiltinToolViewModel vm)
        {
            ShowToolDetail(vm);
        }
    }

    private void ToolsGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var vm = FindAncestorDataContext<BuiltinToolViewModel>(e.OriginalSource as FrameworkElement);
        if (vm is not null)
            _ = ExecuteToolAsync(vm);
    }

    private static T? FindAncestorDataContext<T>(FrameworkElement? element) where T : class
    {
        while (element is not null)
        {
            if (element.DataContext is T t) return t;
            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element) as FrameworkElement;
        }
        return null;
    }

    private void ShowToolDetail(BuiltinToolViewModel vm)
    {
        ToolDetailTip.Title = vm.Name;
        ToolDetailTip.Subtitle = vm.Category;
        DetailDescriptionText.Text = string.IsNullOrWhiteSpace(vm.Description)
            ? "暂无介绍。"
            : vm.Description;
        DetailCategoryText.Text = $"分类：{vm.Category}";
        DetailKindText.Text = $"类型：{vm.KindText}";
        ToolDetailTip.IsOpen = true;
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BuiltinToolViewModel vm })
        {
            _ = ExecuteToolAsync(vm);
        }
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
    }

    private async Task<bool> ConfirmDownloadAsync(string toolName, string description, string size)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = $"即将下载「{toolName}」，是否继续？",
            TextWrapping = TextWrapping.Wrap
        });

        var secondaryBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

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
            ((DispatcherTimer)s).Stop();
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
        BuiltinToolKind.Dialog => "弹窗",
        BuiltinToolKind.BackgroundTask => "后台任务",
        BuiltinToolKind.ProgressTask => "进度任务",
        BuiltinToolKind.InstantAction => "即时操作",
        _ => "未知"
    };
}