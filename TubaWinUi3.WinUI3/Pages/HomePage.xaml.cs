using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class HomePage : Page
{
    private readonly BulkObservableCollection<ToolItem> _tools = new();
    private string? _category;
    private string? _selectedTag;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _highlightCts;
    private bool _compactMode;
    private string? _highlightToolPath;
    private string _searchQuery = string.Empty;
    private string? _lastLoadedCategory;
    private string? _lastLoadedTag;
    private string _lastLoadedQuery = string.Empty;
    private int _lastCacheVersion = -1;
    private int _tagBarCacheVersion = -1;

    public HomePage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
        CompactGrid.ItemsSource = _tools;

        _compactMode = CompactModeService.IsCompactModeEnabled();
        ApplyCompactMode();
        CompactModeService.CompactModeChanged += OnCompactModeChanged;
        ToolCatalog.ToolsChanged += OnToolsChanged;
    }

    private void OnCompactModeChanged(bool enabled)
    {
        _compactMode = enabled;
        ApplyCompactMode();
    }

    private void OnToolsChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ToolCatalog.InvalidateTagsCache();
            _ = LoadToolsAsync();
        });
    }

    private void ApplyCompactMode()
    {
        if (_compactMode)
        {
            ToolsGrid.Visibility = Visibility.Collapsed;
            CompactGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CompactGrid.Visibility = Visibility.Collapsed;
        }
        UpdateItemWidth();
    }

    private void UpdateItemWidth()
    {
        var grid = _compactMode ? CompactGrid : ToolsGrid;
        var panel = grid.ItemsPanelRoot as ItemsWrapGrid;
        if (panel is null) return;

        double minItemWidth = _compactMode ? 100 : 280;
        double spacing = _compactMode ? 10 : 12;
        double availableWidth = grid.ActualWidth - grid.Padding.Left - grid.Padding.Right;

        if (availableWidth <= 0) return;

        int columns = Math.Max(1, (int)((availableWidth + spacing) / (minItemWidth + spacing)));
        double itemWidth = (availableWidth - (columns - 1) * spacing) / columns;
        panel.ItemWidth = Math.Max(minItemWidth, itemWidth);
    }

    private void ToolsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_compactMode) UpdateItemWidth();
    }

    private void CompactGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_compactMode) UpdateItemWidth();
    }

    private void UpdateGridVisibility(bool hasTools)
    {
        if (_compactMode)
        {
            ToolsGrid.Visibility = Visibility.Collapsed;
            var wasVisible = CompactGrid.Visibility == Visibility.Visible;
            CompactGrid.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
            if (hasTools && !wasVisible)
                PlayGridEntrance(CompactGrid);
        }
        else
        {
            var wasVisible = ToolsGrid.Visibility == Visibility.Visible;
            ToolsGrid.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
            CompactGrid.Visibility = Visibility.Collapsed;
            if (hasTools && !wasVisible)
                PlayGridEntrance(ToolsGrid);
        }
    }

    private void PlayGridEntrance(GridView grid)
    {
        var storyboard = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(320);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation { From = 0.0, To = 1.0, Duration = duration, EasingFunction = easing };
        Storyboard.SetTarget(fade, grid);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);

        var slide = new DoubleAnimation { From = 24.0, To = 0.0, Duration = duration, EasingFunction = easing };
        Storyboard.SetTarget(slide, grid);
        Storyboard.SetTargetProperty(slide, "(UIElement.RenderTransform).(TranslateTransform.Y)");
        storyboard.Children.Add(slide);

        storyboard.Begin();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SearchNavigationTarget target && target.HighlightToolPath is not null)
        {
            _highlightToolPath = target.HighlightToolPath;
            try
            {
                var tool = ToolCatalog.GetAllToolsCached()
                    .FirstOrDefault(t => t.Path.Equals(_highlightToolPath, StringComparison.OrdinalIgnoreCase));
                _category = tool?.Category;
            }
            catch { }
        }
        else if (e.Parameter is string category)
        {
            _highlightToolPath = null;
            _category = category;
        }
        else
        {
            _highlightToolPath = null;
            _category = null;
        }

        _searchQuery = string.Empty;
        _selectedTag = null;
        UpdateTitle();

        var needsReload = _category != _lastLoadedCategory ||
                          _selectedTag != _lastLoadedTag ||
                          _searchQuery != _lastLoadedQuery ||
                          ToolCatalog.CacheVersion != _lastCacheVersion;

        if (needsReload)
        {
            _ = LoadToolsAsync();
        }
        else if (_highlightToolPath is not null)
        {
            StartHighlight(_highlightToolPath);
            _highlightToolPath = null;
        }

        if (_category is null && ToolCatalog.CacheVersion != _tagBarCacheVersion)
            _ = PopulateTagBarAsync();
        else if (_category is not null)
            TagBarScrollViewer.Visibility = Visibility.Collapsed;
    }

    private async Task PopulateTagBarAsync()
    {
        IReadOnlyList<string> tags;
        try
        {
            tags = await Task.Run(() => ToolCatalog.GetAllTags());
        }
        catch
        {
            TagBarScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            TagBarPanel.Children.Clear();

            var allBtn = new RadioButton
            {
                Content = "全部",
                Tag = null as string,
                IsChecked = _selectedTag is null,
                Padding = new Thickness(10, 4, 10, 4),
                Style = (Style)Resources["TagRadioButtonStyle"]
            };
            allBtn.Click += TagRadioButton_Click;
            TagBarPanel.Children.Add(allBtn);

            TagBarScrollViewer.Visibility = tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var tag in tags)
            {
                var btn = new RadioButton
                {
                    Content = tag,
                    Tag = tag,
                    IsChecked = tag == _selectedTag,
                    Padding = new Thickness(10, 4, 10, 4),
                    Style = (Style)Resources["TagRadioButtonStyle"]
                };
                btn.Click += TagRadioButton_Click;
                TagBarPanel.Children.Add(btn);
            }

            _tagBarCacheVersion = ToolCatalog.CacheVersion;
        });
    }

    private void TagRadioButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            _selectedTag = rb.Tag as string;
            foreach (var child in TagBarPanel.Children)
            {
                if (child is RadioButton other && other != rb)
                    other.IsChecked = false;
            }
            UpdateTitle();
            _ = LoadToolsAsync();
        }
    }

    private async void DownloadToolsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ToolsBundleDownloadDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await dialog.ShowDownloadAsync();

            if (dialog.DownloadSucceeded)
            {
                ToolCatalog.RefreshToolsRoot();
                ToolCatalog.InvalidateTagsCache();
                _ = LoadToolsAsync();
            }
        }
        catch { }
    }

    private void UpdateTitle()
    {
        var query = _searchQuery;
        var title = _category?.Replace("工具", "") ?? "全部";
        if (query.Length > 0)
            title = $"搜索：{query}";
        else if (_selectedTag is not null)
            title = $"标签：{_selectedTag}";
        CategoryTitle.Text = title;
        CategorySubtitle.Text = query.Length > 0
            ? "显示所有分类中匹配的工具。"
            : _selectedTag is not null
                ? $"显示带有「{_selectedTag}」标签的工具。"
                : _category is null
                    ? "从左侧选择分类，点击卡片看详情，点击打开运行工具。"
                    : $"正在浏览\u201C{_category.Replace("工具", "")}\u201D分类。";
    }

    private async Task LoadToolsAsync()
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        _tools.Clear();

        var query = _searchQuery;

        try
        {
            IReadOnlyList<ToolItem> tools = await Task.Run(() =>
            {
                if (query.Length > 0 || _selectedTag is not null)
                    return ToolCatalog.Search(query, _selectedTag);
                if (_category is not null)
                    return ToolCatalog.GetTools(_category);
                return ToolCatalog.GetAllToolsCached();
            }, cts.Token);

            cts.Token.ThrowIfCancellationRequested();

            _tools.AddRange(tools);

            _lastLoadedCategory = _category;
            _lastLoadedTag = _selectedTag;
            _lastLoadedQuery = query;
            _lastCacheVersion = ToolCatalog.CacheVersion;

            ToolCountText.Text = _tools.Count > 0 ? $"{_tools.Count} 个工具" : "";
            ToolCountText.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateGridVisibility(_tools.Count > 0);
            EmptyStateText.Text = query.Length > 0
                ? $"未找到与\u201C{query}\u201D相关的工具。"
                : _selectedTag is not null
                    ? $"未找到带有「{_selectedTag}」标签的工具。"
                    : _category is not null
                        ? "此分类下没有可用工具。"
                        : "没有找到任何工具，请检查 Tools 目录。";

            DownloadToolsButton.Visibility = _tools.Count == 0
                && string.IsNullOrEmpty(query)
                && _selectedTag is null
                && _category is null
                && RuntimeHelper.IsMsixPackaged
                && !ToolsBundleService.IsToolsBundleReady()
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            StartIconLoading(tools);

            if (_highlightToolPath is not null)
            {
                StartHighlight(_highlightToolPath);
                _highlightToolPath = null;
            }
        }
        catch (OperationCanceledException) { }
    }

    private void StartHighlight(string toolPath)
    {
        _highlightCts?.Cancel();
        _highlightCts = new CancellationTokenSource();
        _ = HighlightToolAsync(toolPath, _highlightCts.Token);
    }

    private async Task HighlightToolAsync(string toolPath, CancellationToken ct)
    {
        var grid = _compactMode ? CompactGrid : ToolsGrid;
        var index = -1;
        for (var i = 0; i < _tools.Count; i++)
        {
            if (_tools[i].Path.Equals(toolPath, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0 || ct.IsCancellationRequested) return;

        grid.ScrollIntoView(_tools[index]);
        try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return; }

        var container = grid.ContainerFromItem(_tools[index]) as GridViewItem;
        if (container is null || ct.IsCancellationRequested) return;

        container.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.5
        });

        try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return; }

        if (ct.IsCancellationRequested) return;

        var border = FindChildBorder(container);
        if (border is not null)
            SearchHighlightService.HighlightBorder(border);
    }

    private static Border? FindChildBorder(DependencyObject parent)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Border border) return border;
            var result = FindChildBorder(child);
            if (result is not null) return result;
        }
        return null;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
    }

    private void CompactGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ToolItem tool)
            LaunchTool(tool, runAsAdmin: false);
    }

    private void CompactGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var tool = FindAncestorDataContext<ToolItem>(e.OriginalSource as FrameworkElement);
        if (tool is not null)
            LaunchTool(tool, runAsAdmin: false);
    }

    private void CompactItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolItem tool)
        {
            var flyout = (MenuFlyout)CompactGrid.Resources["CompactItemFlyout"];
            PopulateArchSubmenu(flyout, tool);
            UpdateBuiltinLinkFlyoutItems(flyout, tool, "CompactMenu");
            UpdateFavoriteMenuItem(flyout, tool, "CompactMenuToggleFavorite");
            flyout.ShowAt(fe, e.GetPosition(fe));
        }
    }

    private void CompactMenu_SendToDesktop(object sender, RoutedEventArgs e)
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

    private void CompactMenu_RunAsAdmin(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            LaunchTool(tool, runAsAdmin: true);
    }

    private void CompactMenu_OpenDirectory(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            OpenToolDirectory(tool);
    }

    private void CompactMenu_OpenTutorial(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool } && tool.HasTutorial)
            BrowserWindow.Open(tool.TutorialUrl!, $"{tool.Name} - 使用教程");
    }

    private void NormalItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolItem tool)
        {
            var flyout = (MenuFlyout)ToolsGrid.Resources["NormalItemFlyout"];
            PopulateArchSubmenu(flyout, tool);
            UpdateBuiltinLinkFlyoutItems(flyout, tool, "NormalMenu");
            UpdateFavoriteMenuItem(flyout, tool, "NormalMenuToggleFavorite");
            flyout.ShowAt(fe, e.GetPosition(fe));
        }
    }

    private void NormalMenu_SendToDesktop(object sender, RoutedEventArgs e)
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

    private void NormalMenu_RunAsAdmin(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            LaunchTool(tool, runAsAdmin: true);
    }

    private void NormalMenu_OpenDirectory(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            OpenToolDirectory(tool);
    }

    private void NormalMenu_OpenTutorial(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool } && tool.HasTutorial)
            BrowserWindow.Open(tool.TutorialUrl!, $"{tool.Name} - 使用教程");
    }

    private void NormalMenu_DeleteTool(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            _ = DeleteToolAsync(tool);
    }

    private void CompactMenu_DeleteTool(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            _ = DeleteToolAsync(tool);
    }

    private void PopulateArchSubmenu(MenuFlyout flyout, ToolItem tool)
    {
        var isCompact = ReferenceEquals(flyout, CompactGrid.Resources["CompactItemFlyout"]);
        var submenuName = isCompact ? "CompactArchSubmenu" : "NormalArchSubmenu";

        var submenu = flyout.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(i => i.Name == submenuName);
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

    private static void UpdateBuiltinLinkFlyoutItems(MenuFlyout flyout, ToolItem tool, string prefix)
    {
        var isBuiltin = tool.IsBuiltinLink;
        var sendToDesktop = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("桌面快捷方式"));
        if (sendToDesktop is not null)
            sendToDesktop.Visibility = isBuiltin ? Visibility.Collapsed : Visibility.Visible;

        var runAsAdmin = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("管理员"));
        if (runAsAdmin is not null)
            runAsAdmin.Visibility = isBuiltin ? Visibility.Collapsed : Visibility.Visible;

        var openDir = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("所在目录"));
        if (openDir is not null)
            openDir.Visibility = isBuiltin ? Visibility.Collapsed : Visibility.Visible;

        var tutorialItem = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("教程"));
        if (tutorialItem is not null)
            tutorialItem.Visibility = tool.HasTutorial ? Visibility.Visible : Visibility.Collapsed;

        var deleteItem = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("删除工具"));
        if (deleteItem is not null)
            deleteItem.Visibility = isBuiltin ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task DeleteToolAsync(ToolItem tool)
    {
        var dialog = new ContentDialog
        {
            Title = $"删除「{tool.Name}」",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "确定要删除此工具吗？此操作不可撤销！",
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "将会删除工具所在目录及所有相关文件：",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.72
                    },
                    new TextBlock
                    {
                        Text = tool.Path,
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.52,
                        FontSize = 12,
                        IsTextSelectionEnabled = true
                    }
                }
            },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        try
        {
            var toolDir = System.IO.Path.GetDirectoryName(tool.Path);
            if (!string.IsNullOrWhiteSpace(toolDir) && System.IO.Directory.Exists(toolDir))
            {
                var categoryDir = System.IO.Path.GetDirectoryName(toolDir);
                await Task.Run(() => System.IO.Directory.Delete(toolDir, true));

                if (!string.IsNullOrWhiteSpace(categoryDir) &&
                    System.IO.Directory.Exists(categoryDir) &&
                    !System.IO.Directory.EnumerateFileSystemEntries(categoryDir).Any())
                {
                    await Task.Run(() => System.IO.Directory.Delete(categoryDir, false));
                }
            }
            else if (System.IO.File.Exists(tool.Path))
            {
                await Task.Run(() => System.IO.File.Delete(tool.Path));
            }

            FavoritesService.RemoveFavorite(tool.Path);
            await ToolMetadataService.RemoveMetadataAsync(tool.Path);
            ToolCatalog.InvalidateTagsCache();

            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.RefreshToolCategories();

            _tools.Remove(tool);
            ToolCountText.Text = _tools.Count > 0 ? $"{_tools.Count} 个工具" : "";
            ToolCountText.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateGridVisibility(_tools.Count > 0);

            ShowStatus("已删除", $"「{tool.Name}」已删除", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("删除失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private static void OpenToolDirectory(ToolItem tool)
    {
        var dir = tool.EffectiveWorkingDir;
        if (System.IO.Directory.Exists(dir))
            _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    private void ToolsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ToolItem tool)
        {
            ShowToolDetail(tool);
        }
    }

    private void ToolsGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var tool = FindAncestorDataContext<ToolItem>(e.OriginalSource as FrameworkElement);
        if (tool is not null)
            LaunchTool(tool, runAsAdmin: false);
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

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ToolItem btnTool })
        {
            LaunchTool(btnTool, runAsAdmin: false);
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
        if (sender is FrameworkElement fe && fe.DataContext is ToolItem tool)
        {
            FavoritesService.ToggleFavorite(tool.Path);
            tool.IsFavorite = !tool.IsFavorite;
            AnimateFavoriteButton(fe);
        }
    }

    private static void UpdateFavoriteMenuItem(MenuFlyout flyout, ToolItem tool, string menuItemName)
    {
        var item = flyout.Items.OfType<MenuFlyoutItem>().FirstOrDefault(i => i.Name == menuItemName);
        if (item is null) return;
        item.Text = tool.IsFavorite ? "取消收藏" : "收藏";
        if (item.Icon is FontIcon icon)
            icon.Glyph = tool.IsFavorite ? "\uE735" : "\uE734";
    }

    private void NormalMenu_ToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
        {
            FavoritesService.ToggleFavorite(tool.Path);
            tool.IsFavorite = !tool.IsFavorite;
        }
    }

    private void CompactMenu_ToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
        {
            FavoritesService.ToggleFavorite(tool.Path);
            tool.IsFavorite = !tool.IsFavorite;
        }
    }

    private static void AnimateFavoriteButton(FrameworkElement target)
    {
        if (FastModeService.IsFastModeEnabled()) return;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(target);
        if (visual is null) return;
        var compositor = visual.Compositor;

        var scaleUp = compositor.CreateVector3KeyFrameAnimation();
        scaleUp.InsertKeyFrame(0f, new System.Numerics.Vector3(1f, 1f, 1f));
        scaleUp.InsertKeyFrame(0.4f, new System.Numerics.Vector3(1.35f, 1.35f, 1f));
        scaleUp.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f));
        scaleUp.Duration = TimeSpan.FromMilliseconds(350);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.InsertKeyFrame(0f, 1f);
        opacityAnim.InsertKeyFrame(0.3f, 0.4f);
        opacityAnim.InsertKeyFrame(1f, 1f);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(350);

        visual.CenterPoint = new System.Numerics.Vector3((float)target.ActualSize.X / 2, (float)target.ActualSize.Y / 2, 0f);
        visual.StartAnimation("Scale", scaleUp);
        visual.StartAnimation("Opacity", opacityAnim);
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

    private void StartIconLoading(IReadOnlyList<ToolItem> tools)
    {
        if (tools.Count == 0) return;
        _loadCts?.Cancel();
        var ct = _loadCts?.Token ?? CancellationToken.None;
        _ = ToolIconService.LoadIconsAsync(tools, DispatcherQueue);
        _ = CheckWingetInstallStatusAsync(tools, ct);
    }

    private async Task CheckWingetInstallStatusAsync(IReadOnlyList<ToolItem> tools, CancellationToken ct)
    {
        var wingetTools = tools.Where(t => !string.IsNullOrWhiteSpace(t.WingetId) && !t.IsWingetInstalled).ToList();
        if (wingetTools.Count == 0) return;

        foreach (var tool in wingetTools)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var installed = await WingetService.IsInstalledAsync(tool.WingetId!, ct);
                if (installed)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        tool.IsWingetInstalled = true;
                        tool.IsWingetInstalling = false;
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private void ShowToolDetail(ToolItem tool)
    {
        if (tool.IsBuiltinLink)
        {
            ToolDetailTip.Title = tool.Name;
            ToolDetailTip.Subtitle = tool.Category;
            DetailDescriptionText.Text = string.IsNullOrWhiteSpace(tool.Description)
                ? "暂无介绍。"
                : tool.Description;
            DetailPublisherText.Text = $"类型：{tool.BuiltinKindText ?? "内置"}";
            DetailVersionText.Text = "";
            DetailPathText.Text = "";
            ToolDetailTip.IsOpen = true;
            return;
        }

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
        if (tool.IsBuiltinLink)
        {
            _ = LaunchBuiltinToolAsync(tool);
            return;
        }

        if (!string.IsNullOrWhiteSpace(tool.RemoteUrl))
        {
            Pages.BrowserWindow.Open(tool.RemoteUrl, tool.Name);
            LaunchHistoryService.RecordLaunch(tool.Path);
            ShowStatus("已打开", tool.Name, InfoBarSeverity.Success);
            return;
        }

        if (!string.IsNullOrWhiteSpace(tool.DownloadUrl) && !File.Exists(tool.EffectivePath))
        {
            _ = ShowDownloadDialogAsync(tool);
            return;
        }

        if (!string.IsNullOrWhiteSpace(tool.WingetId) && !File.Exists(tool.EffectivePath))
        {
            _ = HandleWingetToolAsync(tool);
            return;
        }

        var exePath = tool.EffectivePath;
        if (!File.Exists(exePath))
        {
            ShowStatus("启动失败", $"找不到文件：{exePath}", InfoBarSeverity.Error);
            return;
        }

        var ext = Path.GetExtension(exePath);
        if (ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var command = ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
                ? $"powershell -ExecutionPolicy Bypass -Command \"& '{exePath}'\""
                : $"cmd.exe /c \"{exePath}\"";
            ScriptRunnerWindow.ShowAndRun(command, tool.EffectiveWorkingDir, $"安装 {tool.Name}");
            LaunchHistoryService.RecordLaunch(tool.Path);
            ShowStatus("已启动", tool.Name, InfoBarSeverity.Success);
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

    private async Task LaunchBuiltinToolAsync(ToolItem tool)
    {
        var builtinTool = BuiltinToolRegistry.GetById(tool.BuiltinToolId!);
        if (builtinTool is null)
        {
            ShowStatus("启动失败", "找不到对应的内置工具", InfoBarSeverity.Error);
            return;
        }

        try
        {
            var context = new BuiltinToolContext
            {
                XamlRoot = XamlRoot,
                OnProgress = msg => DispatcherQueue.TryEnqueue(() =>
                    ShowStatus(builtinTool.Name, msg, InfoBarSeverity.Informational))
            };
            await builtinTool.ExecuteAsync(context);
            LaunchHistoryService.RecordLaunch(tool.Path);
            ShowStatus("已启动", tool.Name, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task HandleWingetToolAsync(ToolItem tool)
    {
        if (tool.IsWingetInstalling) return;

        if (!tool.IsWingetInstalled)
        {
            var installed = await WingetService.IsInstalledAsync(tool.WingetId!);
            if (installed)
            {
                tool.IsWingetInstalled = true;
                LaunchInstalledWingetTool(tool);
                return;
            }

            tool.IsWingetInstalling = true;
            ShowStatus("正在安装", $"正在通过 winget 安装「{tool.Name}」...", InfoBarSeverity.Informational);

            var progress = new Progress<WingetInstallProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    tool.WingetInstallStatus = p.StatusLine;
                    if (p.Percent > 0) tool.WingetInstallProgress = p.Percent;
                });
            });

            var result = await WingetService.InstallAsync(tool.WingetId!, progress);
            tool.IsWingetInstalling = false;

            if (result.Success)
            {
                tool.IsWingetInstalled = true;
                ShowStatus("安装完成", $"「{tool.Name}」安装成功，点击打开即可使用。", InfoBarSeverity.Success);
            }
            else
            {
                ShowStatus("安装失败", result.Message, InfoBarSeverity.Error);
            }
            return;
        }

        LaunchInstalledWingetTool(tool);
    }

    private void LaunchInstalledWingetTool(ToolItem tool)
    {
        var exePath = WingetService.FindInstalledExePath(tool.WingetId!);
        if (exePath is not null && File.Exists(exePath))
        {
            try
            {
                ToolProcessLauncher.Launch(exePath, Path.GetDirectoryName(exePath));
                ShowStatus("已启动", tool.Name, InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
            }
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" winget run --id {tool.WingetId} --accept-source-agreements",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            ShowStatus("已启动", tool.Name, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task ShowDownloadDialogAsync(ToolItem tool)
    {
        var toolDir = Path.GetDirectoryName(tool.Path) ?? Path.Combine(ToolCatalog.ToolsRoot, tool.Category, tool.Folder);
        Directory.CreateDirectory(toolDir);

        var dialog = new ToolDownloadDialog(
            tool.Name,
            tool.Description ?? "",
            tool.DownloadUrl!,
            tool.DownloadFilter,
            toolDir);

        await dialog.ShowAsync();
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

public sealed class ZeroToIndeterminateConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int progress)
            return progress <= 0;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
            return b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}