using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Diagnostics;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class FavoritesPage : Page
{
    private readonly ObservableCollection<ToolItem> _tools = [];
    private readonly List<ToolItem> _frequentTools = [];
    private CancellationTokenSource? _iconLoadCts;

    public FavoritesPage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
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

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = LoadFrequentToolsAsync();
        _ = LoadToolsAsync();
    }

    private async Task LoadFrequentToolsAsync()
    {
        _frequentTools.Clear();

        var frequentRecords = LaunchHistoryService.GetFrequentTools(12);
        if (frequentRecords.Count == 0)
        {
            FrequentSection.Visibility = Visibility.Collapsed;
            return;
        }

        List<ToolItem> allTools;
        try
        {
            allTools = await Task.Run(() => ToolCatalog.GetAllToolsCached().ToList());
        }
        catch
        {
            FrequentSection.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var record in frequentRecords)
        {
            var tool = allTools.FirstOrDefault(t =>
                t.Path.Equals(record.Path, StringComparison.OrdinalIgnoreCase));
            if (tool is not null)
                _frequentTools.Add(tool);
        }

        if (_frequentTools.Count == 0)
        {
            FrequentSection.Visibility = Visibility.Collapsed;
            return;
        }

        FrequentPanel.Children.Clear();
        foreach (var tool in _frequentTools)
        {
            var card = CreateFrequentCard(tool);
            FrequentPanel.Children.Add(card);
        }

        FrequentSection.Visibility = Visibility.Visible;
        FrequentSubtitle.Text = _frequentTools.Count >= 12
            ? $"基于使用频率智能排序 · 前 {_frequentTools.Count} 个"
            : $"基于使用频率智能排序 · {_frequentTools.Count} 个";

        _ = ToolIconService.LoadIconsAsync(_frequentTools.ToList(), DispatcherQueue);
    }

    private Border CreateFrequentCard(ToolItem tool)
    {
        var card = new Border
        {
            Padding = new Thickness(10, 10, 10, 8),
            Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Width = 100,
            Tag = tool
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 6
        };

        var iconBorder = new Border
        {
            Width = 48,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(10)
        };

        var iconGrid = new Grid();
        var image = new Image
        {
            Width = 36,
            Height = 36,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
        };
        image.SetBinding(Image.SourceProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Path = new PropertyPath(nameof(ToolItem.IconPath)),
            Source = tool,
            Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay
        });
        image.SetBinding(Image.VisibilityProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Path = new PropertyPath(nameof(ToolItem.IconPath)),
            Source = tool,
            Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay,
            Converter = (Microsoft.UI.Xaml.Data.IValueConverter)Resources["NullToCollapse"]
        });
        var fontIcon = new FontIcon
        {
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Glyph = tool.IconGlyph ?? "",
            Visibility = tool.IconGlyph is not null ? Visibility.Visible : Visibility.Collapsed
        };
        iconGrid.Children.Add(image);
        iconGrid.Children.Add(fontIcon);
        iconBorder.Child = iconGrid;
        stack.Children.Add(iconBorder);

        var nameBlock = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12,
            MaxLines = 2,
            Text = tool.Name,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
            Width = 80
        };
        stack.Children.Add(nameBlock);

        card.Child = stack;

        var toolTip = new ToolTip
        {
            Content = string.IsNullOrWhiteSpace(tool.Description) ? tool.Name : $"{tool.Name}\n{tool.Description}"
        };
        ToolTipService.SetToolTip(card, toolTip);

        card.PointerPressed += (s, e) =>
        {
            LaunchTool(tool, runAsAdmin: false);
        };

        return card;
    }

    private async Task LoadToolsAsync()
    {
        _iconLoadCts?.Cancel();
        _tools.Clear();

        var favPaths = FavoritesService.GetFavorites();
        if (favPaths.Count == 0)
        {
            ToolCountText.Text = "暂无收藏";
            ClearAllButton.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            ToolsGrid.Visibility = Visibility.Collapsed;
            return;
        }

        List<ToolItem> favTools;
        try
        {
            favTools = await Task.Run(() => ToolCatalog.GetCategories()
                .SelectMany(ToolCatalog.GetTools)
                .Where(t => favPaths.Contains(t.Path, StringComparer.OrdinalIgnoreCase))
                .ToList());
        }
        catch
        {
            ToolCountText.Text = "加载失败";
            return;
        }

        foreach (var tool in favTools)
        {
            _tools.Add(tool);
        }

        ToolCountText.Text = $"已收藏 {_tools.Count} 个工具";
        ClearAllButton.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (favTools.Count > 0)
        {
            _iconLoadCts = new CancellationTokenSource();
            _ = ToolIconService.LoadIconsAsync(favTools, DispatcherQueue);
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

    private void RemoveFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToolItem tool })
        {
            FavoritesService.RemoveFavorite(tool.Path);
            _tools.Remove(tool);
            ToolCountText.Text = _tools.Count > 0 ? $"已收藏 {_tools.Count} 个工具" : "暂无收藏";
            ClearAllButton.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SendToDesktopButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToolItem tool })
        {
            try
            {
                CreateDesktopShortcut(tool);
                ShowStatus("已创建", $"已将「{tool.Name}」快捷方式发送到桌面", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("创建失败", ex.Message, InfoBarSeverity.Error);
            }
        }
    }

    private void FavItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolItem tool)
        {
            var flyout = (MenuFlyout)Resources["FavItemFlyout"];
            PopulateArchSubmenu(flyout, tool);
            UpdateTutorialVisibility(flyout, tool);
            UpdateFavoriteMenuItem(flyout, tool);
            flyout.ShowAt(fe, e.GetPosition(fe));
        }
    }

    private static void UpdateFavoriteMenuItem(MenuFlyout flyout, ToolItem tool)
    {
        var item = flyout.Items.OfType<MenuFlyoutItem>().FirstOrDefault(i => i.Name == "FavMenuToggleFavorite");
        if (item is null) return;
        item.Text = tool.IsFavorite ? "取消收藏" : "收藏";
        if (item.Icon is FontIcon icon)
            icon.Glyph = tool.IsFavorite ? "\uE735" : "\uE734";
    }

    private void FavMenu_ToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
        {
            FavoritesService.ToggleFavorite(tool.Path);
            tool.IsFavorite = !tool.IsFavorite;
            if (!tool.IsFavorite)
            {
                _tools.Remove(tool);
                ToolCountText.Text = _tools.Count > 0 ? $"已收藏 {_tools.Count} 个工具" : "暂无收藏";
                ClearAllButton.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private static void UpdateTutorialVisibility(MenuFlyout flyout, ToolItem tool)
    {
        var tutorialItem = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("教程"));
        if (tutorialItem is not null)
            tutorialItem.Visibility = tool.HasTutorial ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FavMenu_SendToDesktop(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
        {
            try
            {
                CreateDesktopShortcut(tool);
                ShowStatus("已创建", $"已将「{tool.Name}」快捷方式发送到桌面", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("创建失败", ex.Message, InfoBarSeverity.Error);
            }
        }
    }

    private void FavMenu_RunAsAdmin(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            LaunchTool(tool, runAsAdmin: true);
    }

    private void FavMenu_OpenDirectory(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            OpenToolDirectory(tool);
    }

    private void FavMenu_OpenTutorial(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool } && tool.HasTutorial)
            BrowserPage.Open(tool.TutorialUrl!, $"{tool.Name} - 使用教程");
    }

    private static void OpenToolDirectory(ToolItem tool)
    {
        try
        {
            var dir = tool.EffectiveWorkingDir;
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch { }
    }

    private void PopulateArchSubmenu(MenuFlyout flyout, ToolItem tool)
    {
        var submenu = flyout.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(i => i.Name == "FavArchSubmenu");
        if (submenu is null) return;

        submenu.Items.Clear();

        if (tool.ArchOptions.Count <= 1)
        {
            submenu.Visibility = Visibility.Collapsed;
            return;
        }

        submenu.Visibility = Visibility.Visible;
        foreach (var opt in tool.ArchOptions)
        {
            var label = string.IsNullOrEmpty(opt.Arch) ? "默认" : opt.Arch;
            var item = new ToggleMenuFlyoutItem
            {
                Text = label,
                IsChecked = opt == tool.SelectedArch,
                DataContext = opt
            };
            item.Click += (s, e) =>
            {
                if (s is ToggleMenuFlyoutItem { DataContext: ArchOption selected })
                    tool.SelectedArch = selected;
            };
            submenu.Items.Add(item);
        }
    }

    private static void CreateDesktopShortcut(ToolItem tool)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var archSuffix = tool.SelectedArch is not null && !string.IsNullOrEmpty(tool.SelectedArch.Arch)
            ? $" ({tool.SelectedArch.Arch})" : "";
        var shortcutPath = Path.Combine(desktop, $"{tool.Name}{archSuffix}.lnk");

        var psScript = $"""
            $ws = New-Object -ComObject WScript.Shell
            $s = $ws.CreateShortcut('{shortcutPath}')
            $s.TargetPath = '{tool.EffectivePath}'
            $s.WorkingDirectory = '{tool.EffectiveWorkingDir}'
            $s.Description = '{tool.Name}{archSuffix}'
            $s.Save()
            """;

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{psScript.Replace("\"", "\\\"")}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(5000);

        if (process is not null && process.ExitCode != 0)
        {
            var err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(err);
        }
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清空全部收藏",
            Content = "确定要取消所有工具的收藏吗？此操作不可撤销。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        if (dialog.ShowAsync() is not null)
        {
            dialog.PrimaryButtonClick += (_, _) =>
            {
                FavoritesService.RemoveAll();
                _ = LoadToolsAsync();
            };
        }
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
        if (!string.IsNullOrWhiteSpace(tool.RemoteUrl))
        {
            Pages.BrowserPage.Open(tool.RemoteUrl, tool.Name);
            LaunchHistoryService.RecordLaunch(tool.Path);
            ShowStatus("已打开", tool.Name, InfoBarSeverity.Success);
            return;
        }

        var exePath = tool.EffectivePath;
        if (!File.Exists(exePath))
        {
            ShowStatus("启动失败", $"找不到文件：{exePath}", InfoBarSeverity.Error);
            return;
        }

        try
        {
            ToolProcessLauncher.Launch(exePath, tool.EffectiveWorkingDir, runAsAdmin);

            LaunchHistoryService.RecordLaunch(tool.Path);
            ShowStatus(runAsAdmin ? "已以管理员身份启动" : "已启动", tool.Name, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
        }
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

    private static string ValueOrUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未知" : value;
    }
}
