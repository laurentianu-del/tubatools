using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Diagnostics;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class HomePage : Page
{
    private readonly ObservableCollection<ToolItem> _tools = [];
    private string? _category;

    public HomePage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
        ToolsRootText.Text = ToolCatalog.ToolsRoot;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _category = e.Parameter as string;
        SearchBox.Text = string.Empty;
        LoadTools();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason is AutoSuggestionBoxTextChangeReason.UserInput or AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
        {
            LoadTools();
        }
    }

    private void ToolsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ToolItem tool)
        {
            ShowToolDetail(tool);
        }
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ToolItem tool })
        {
            LaunchTool(tool, runAsAdmin: false);
        }
    }

    private void RunAsAdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ToolItem tool })
        {
            LaunchTool(tool, runAsAdmin: true);
        }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToolItem tool })
        {
            FavoritesService.ToggleFavorite(tool.Path);
            tool.IsFavorite = !tool.IsFavorite;

            var idx = _tools.IndexOf(tool);
            if (idx >= 0)
            {
                _tools[idx] = tool;
            }
        }
    }

    private void LoadTools()
    {
        var query = SearchBox.Text.Trim();
        var tools = query.Length > 0
            ? ToolCatalog.Search(query)
            : LoadSelectedCategory();

        _tools.Clear();
        foreach (var tool in tools)
        {
            _tools.Add(tool);
        }

        CategoryTitle.Text = query.Length > 0 ? $"搜索：{query}" : (_category ?? "全部工具");
        CategorySubtitle.Text = query.Length > 0
            ? "显示所有分类中匹配的工具。"
            : _category is null
                ? "从左侧选择分类，点击卡片看详情，点击打开运行工具。"
                : $"正在浏览\u201C{_category}\u201D分类。";
        ToolCountText.Text = $"{_tools.Count} 个工具";
    }

    private IReadOnlyList<ToolItem> LoadSelectedCategory()
    {
        if (_category is not null)
        {
            return ToolCatalog.GetTools(_category);
        }

        return ToolCatalog.GetCategories()
            .SelectMany(ToolCatalog.GetTools)
            .Take(120)
            .ToList();
    }

    private void ShowToolDetail(ToolItem tool)
    {
        ToolDetailTip.Title = tool.Name;
        ToolDetailTip.Subtitle = tool.Category;
        DetailDescriptionText.Text = string.IsNullOrWhiteSpace(tool.Description)
            ? "暂无介绍。"
            : tool.Description;
        DetailPublisherText.Text = $"发布者：{ValueOrUnknown(tool.Publisher)}";
        DetailVersionText.Text = $"版本：{ValueOrUnknown(tool.Version)}";
        DetailPathText.Text = tool.Path;
        ToolDetailTip.IsOpen = true;
    }

    private void LaunchTool(ToolItem tool, bool runAsAdmin)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = tool.Path,
                WorkingDirectory = Path.GetDirectoryName(tool.Path) ?? ToolCatalog.ToolsRoot,
                UseShellExecute = true,
                Verb = runAsAdmin ? "runAs" : null
            });

            ShowStatus(runAsAdmin ? "已以管理员身份启动" : "已启动", tool.Name, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static string ValueOrUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未知" : value;
    }
}
