using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using Windows.UI;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3;

public sealed partial class MainWindow : Window
{
    private bool _syncingNavSelection;
    private bool _navFromSidebar;
    private bool _suppressSearch;
    private bool _searchDismissed;
    private bool _syncingTabSelection;
    private readonly ObservableCollection<SearchResult> _searchResults = [];
    private readonly DispatcherQueueTimer _searchDebounceTimer;
    private Flyout? _downloadFlyout;
    private int _lastBadgeCount;
    private bool _isTabMode;
    private bool _refreshCategoriesInFlight;
    private bool _refreshCategoriesPending;

    public bool IsTabMode => _isTabMode;

    /// <summary>当前正在执行的内置工具名称（入口页在 ExecuteAsync 前设置），供独立窗口标题使用。</summary>
    public static string? ActiveToolName { get; set; }

    public Image? GetBackgroundImage() => BackgroundImg;

    public void ShowUpdateBanner(Models.UpdateInfo update, bool autoDownload)
    {
        if (autoDownload)
        {
            UpdateBanner.ShowDownloading();
            var item = UpdateService.AutoDownloadUpdate(update);
            if (item is not null)
            {
                item.PropertyChanged += (s, e) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (s is not DownloadItem di) return;
                        switch (e.PropertyName)
                        {
                            case nameof(DownloadItem.State):
                                if (di.State == DownloadItemState.Completed)
                                    UpdateBanner.ShowDownloadComplete();
                                else if (di.State == DownloadItemState.Failed)
                                    UpdateBanner.ShowDownloadFailed(di.ErrorMessage ?? "未知错误");
                                break;
                            case nameof(DownloadItem.Progress):
                                if (di.Progress is not null && di.Progress.TotalBytes > 0)
                                    UpdateBanner.ShowDownloadProgress(di.Progress.Percentage);
                                break;
                        }
                    });
                };
            }
            else
            {
                UpdateBanner.ShowUpdateAvailable(update);
            }
        }
        else
        {
            UpdateBanner.ShowUpdateAvailable(update);
        }
    }

    public void ShowUpdateAlreadyDownloaded(Models.UpdateInfo update)
    {
        UpdateBanner.ShowUpdateAvailable(update);
        UpdateBanner.ShowDownloadComplete();
    }

    private DispatcherTimer? _toolUpdateToastTimer;

    public void ShowToolUpdateToast(string toolName)
    {
        ToolUpdateToast.Title = "工具更新完成";
        ToolUpdateToast.Message = $"「{toolName}」已更新到最新版本";
        ToolUpdateToast.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
        ToolUpdateToast.IsOpen = true;
        StartToastAutoClose();
    }

    public void ShowToolUpdateProgressToast(string toolName)
    {
        ToolUpdateToast.Title = "正在更新工具";
        ToolUpdateToast.Message = $"「{toolName}」正在同步更新...";
        ToolUpdateToast.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
        ToolUpdateToast.IsOpen = true;
    }

    public void ShowToolUpdateFailedToast(string toolName, string error)
    {
        ToolUpdateToast.Title = "工具更新失败";
        ToolUpdateToast.Message = $"「{toolName}」更新失败：{error}";
        ToolUpdateToast.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
        ToolUpdateToast.IsOpen = true;
        StartToastAutoClose();
    }

    private void StartToastAutoClose()
    {
        _toolUpdateToastTimer?.Stop();
        _toolUpdateToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toolUpdateToastTimer.Tick += (s, e) =>
        {
            ToolUpdateToast.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _toolUpdateToastTimer.Start();
    }

#pragma warning disable CS0414
    private bool _initialized;
#pragma warning restore CS0414

    public MainWindow()
    {
        InitializeComponent();

        SearchListView.ItemsSource = _searchResults;

        _searchDebounceTimer = DispatcherQueue.CreateTimer();
        _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(100);
        _searchDebounceTimer.Tick += OnSearchDebounceTick;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        AppWindow.SetIcon(iconPath);

        ApplyTitleBarTheme(ElementTheme.Default);

        BackdropService.ApplyBackdrop(this);
        BackdropService.BackdropChanged += OnBackdropChanged;

        WindowSizeService.ApplySavedWindowSize(this);

        Closed += MainWindow_Closed;
        AppWindow.Changed += AppWindow_Changed;
        NavFrame.Navigated += NavFrame_Navigated;
        TabNavFrame.Navigated += TabNavFrame_Navigated;

        if (RuntimeHelper.IsMsixPackaged)
        {
            NavView.MenuItems.Remove(CommunityNavItem);
        }

        SplashVersionText.Text = UpdateService.CurrentVersion.ToString();
        _ = InitializeAfterSplashAsync();
    }

    private async Task InitializeAfterSplashAsync()
    {
        _initialized = true;

        NavigateToDefaultPage();

        // 本地背景立即应用；品牌检测（WMI 查询 + 壁纸下载）延迟到空闲期，不抢启动窗口
        ApplyBackground();
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            await BackgroundService.EnsureBrandBackgroundInitializedAsync();
        });

        _ = Task.Run(async () =>
        {
            try
            {
                _ = ToolCatalog.ToolsRoot;
                var categories = ToolCatalog.GetCategories().ToList();

                if (!ToolCatalog.IsCacheReady)
                {
                    await ToolCatalog.GetAllToolsAsync();
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    PopulateCategories(categories);
                    ApplyNavLayoutMode();
                    NavLayoutModeService.NavLayoutModeChanged += OnNavLayoutModeChanged;
                });
            }
            catch { }
        });

        DownloadQueueService.Initialize(DispatcherQueue);
        DownloadQueueService.QueueChanged += OnDownloadQueueChanged;
        ToolUpdateService.Initialize(DispatcherQueue);
        UpdateDownloadBadge();

        BrandEasterEggService.BrandBackgroundLoaded += OnBrandBackgroundLoaded;
        AppSettings.SettingChanged += OnBackgroundSettingChanged;

        await FadeOutSplashAsync();
    }

    private async Task FadeOutSplashAsync()
    {
        var storyboard = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(350);
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };

        storyboard.Children.Add(CreateSplashAnimation("Opacity", 1.0, 0.0, duration, easing));
        storyboard.Children.Add(CreateSplashAnimation("(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 1.0, 0.95, duration, easing));
        storyboard.Children.Add(CreateSplashAnimation("(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 1.0, 0.95, duration, easing));

        var tcs = new TaskCompletionSource();
        storyboard.Completed += (_, _) => tcs.TrySetResult();
        storyboard.Begin();
        await tcs.Task;

        SplashOverlay.Visibility = Visibility.Collapsed;
    }

    private DoubleAnimation CreateSplashAnimation(string targetProperty, double from, double to, TimeSpan duration, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(animation, SplashOverlay);
        Storyboard.SetTargetProperty(animation, targetProperty);
        return animation;
    }

    private void OnBackgroundSettingChanged(string key)
    {
        if (key == "BackgroundImagePath" || key == "BackgroundOpacity")
            DispatcherQueue.TryEnqueue(ApplyBackground);
    }

    private void ApplyBackground()
    {
        var bmp = BackgroundService.LoadBackgroundImage();
        if (bmp is not null)
        {
            BackgroundImg.Source = bmp;
            BackgroundImg.Opacity = BackgroundService.GetBackgroundOpacity();
            BackgroundImg.Visibility = Visibility.Visible;
        }
        else
        {
            BackgroundImg.Source = null;
            BackgroundImg.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSplashStatus(string text)
    {
        DispatcherQueue.TryEnqueue(() => SplashStatusText.Text = text);
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        if (!_isTabMode)
            AppTitleBar.IsBackButtonVisible = NavFrame.CanGoBack;

        if (_navFromSidebar)
        {
            _navFromSidebar = false;
            return;
        }

        _syncingNavSelection = true;

        if (e.SourcePageType == typeof(SettingsPage))
        {
            NavView.SelectedItem = NavView.SettingsItem;
        }
        else
        {
            var targetTag = ResolvePageTag(e.SourcePageType, e.Parameter);
            if (targetTag is not null)
            {
                foreach (var item in NavView.MenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag is string t && t == targetTag)
                    {
                        NavView.SelectedItem = navItem;
                        break;
                    }
                }
            }
        }

        _syncingNavSelection = false;
    }

    private void TabNavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        if (_isTabMode)
            AppTitleBar.IsBackButtonVisible = TabNavFrame.CanGoBack;

        if (_navFromSidebar)
        {
            _navFromSidebar = false;
            return;
        }

        _syncingTabSelection = true;

        if (e.SourcePageType == typeof(SettingsPage))
        {
            foreach (var item in MainTabView.TabItems)
            {
                if (item is TabViewItem tab && tab.Tag?.ToString() == "settings")
                {
                    MainTabView.SelectedItem = tab;
                    break;
                }
            }
        }
        else
        {
            var targetTag = ResolvePageTag(e.SourcePageType, e.Parameter);
            if (targetTag is not null)
            {
                foreach (var item in MainTabView.TabItems)
                {
                    if (item is TabViewItem tab && tab.Tag?.ToString() == targetTag)
                    {
                        MainTabView.SelectedItem = tab;
                        break;
                    }
                }
            }
        }

        _syncingTabSelection = false;
    }

    private static string? ResolvePageTag(Type pageType, object? parameter)
    {
        if (pageType == typeof(SettingsPage)) return "settings";
        if (pageType == typeof(FavoritesPage)) return "favorites";
        if (pageType == typeof(HardwarePage)) return "hardware";
        if (pageType == typeof(BuiltinToolsPage)) return "builtin";
        if (pageType == typeof(CommunityToolsPage)) return "community";

        if (pageType == typeof(HomePage))
        {
            if (parameter is string category) return category;
            return "all";
        }
        return null;
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPointProperties props = e.GetCurrentPoint(null).Properties;
        var frame = _isTabMode ? TabNavFrame : NavFrame;

        if (props.IsXButton1Pressed)
        {
            if (frame.CanGoBack)
            {
                frame.GoBack();
                e.Handled = true;
            }
        }
        else if (props.IsXButton2Pressed)
        {
            if (frame.CanGoForward)
            {
                frame.GoForward();
                e.Handled = true;
            }
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // Gracefully stop EnergyStar throttling so any throttled processes recover
        // their normal scheduling priority before the app exits. If the user has
        // enabled the scheduled-task auto-start, the next logon will re-enable it.
        try { EnergyStarService.Shutdown(); } catch { }

        if (App.IsLiteMode)
        {
            args.Handled = true;
            AppWindow.Hide();
            return;
        }
        BackdropService.BackdropChanged -= OnBackdropChanged;
        AppWindow.Changed -= AppWindow_Changed;
        WindowSizeService.SaveWindowSize(this);
        DownloadQueueService.QueueChanged -= OnDownloadQueueChanged;
        BrandEasterEggService.BrandBackgroundLoaded -= OnBrandBackgroundLoaded;
        AppSettings.SettingChanged -= OnBackgroundSettingChanged;
        NavLayoutModeService.NavLayoutModeChanged -= OnNavLayoutModeChanged;
    }

    private void OnDownloadQueueChanged()
    {
        DispatcherQueue.TryEnqueue(UpdateDownloadBadge);
    }

    private void OnBrandBackgroundLoaded(object? sender, BrandEasterEggLoadedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyBackground();
            BrandBgBanner.Show(e.BrandName);
        });
    }

    private void UpdateDownloadBadge()
    {
        var count = DownloadQueueService.PendingCount;
        if (count > 0)
        {
            DownloadQueueBadge.Value = count > 99 ? 99 : count;
            DownloadQueueBadge.Visibility = Visibility.Visible;
        }
        else
        {
            DownloadQueueBadge.Visibility = Visibility.Collapsed;
        }

        if (count > _lastBadgeCount)
        {
            PlayDownloadPulseAnimation();
        }
        _lastBadgeCount = count;
    }

    private void PlayDownloadPulseAnimation()
    {
        var btn = DownloadQueueButton;
        btn.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        btn.RenderTransform = new ScaleTransform();

        var scaleX = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTargetProperty(scaleX, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        Storyboard.SetTarget(scaleX, btn);
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(150), Value = 1.3, EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(400), Value = 1.0, EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseInOut } });

        var scaleY = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTargetProperty(scaleY, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        Storyboard.SetTarget(scaleY, btn);
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(150), Value = 1.3, EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } });
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(400), Value = 1.0, EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseInOut } });

        var sb = new Storyboard();
        sb.Children.Add(scaleX);
        sb.Children.Add(scaleY);
        sb.Begin();
    }

    private void DownloadQueueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadFlyout is null)
        {
            _downloadFlyout = new Flyout
            {
                Content = new DownloadQueueFlyout(),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
            };
        }
        _downloadFlyout.ShowAt(DownloadQueueButton);
    }

    private void AiQuickButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new Flyout
        {
            Content = new AiQuickAskFlyout(),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        };
        flyout.ShowAt(AiQuickButton);
    }

    private void OnBackdropChanged()
    {
        DispatcherQueue.TryEnqueue(() => BackdropService.ApplyBackdrop(this));
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange) return;
        var size = sender.Size;
        var minWidth = 800;
        var minHeight = 600;
        var needsResize = false;
        var newW = size.Width;
        var newH = size.Height;

        if (size.Width < minWidth)
        {
            newW = minWidth;
            needsResize = true;
        }
        if (size.Height < minHeight)
        {
            newH = minHeight;
            needsResize = true;
        }

        if (needsResize)
        {
            sender.Resize(new Windows.Graphics.SizeInt32(newW, newH));
        }
    }

    public void ApplyTitleBarTheme(ElementTheme theme)
    {
        var tb = AppWindow.TitleBar;
        var isDark = theme == ElementTheme.Dark ||
                     (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        if (isDark)
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);

            tb.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
            tb.InactiveBackgroundColor = Color.FromArgb(255, 32, 32, 32);
        }
        else
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);

            tb.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.InactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
        }

        tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        if (!_isTabMode)
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        var frame = _isTabMode ? TabNavFrame : NavFrame;
        if (frame.CanGoBack)
            frame.GoBack();
    }

    private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingNavSelection || _isTabMode) return;

        _navFromSidebar = true;

        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "all":
                    NavFrame.Navigate(typeof(HomePage), null);
                    break;
                case "favorites":
                    NavFrame.Navigate(typeof(FavoritesPage));
                    break;
                case "hardware":
                    NavFrame.Navigate(typeof(HardwarePage));
                    break;
                case "builtin":
                    NavFrame.Navigate(typeof(BuiltinToolsPage));
                    break;
                case "community":
                    if (RuntimeHelper.IsMsixPackaged) break;
                    NavFrame.Navigate(typeof(CommunityToolsPage));
                    break;

                case "benchmark":
                    _navFromSidebar = false;
                    _ = ExecuteBenchmarkToolAsync();
                    break;
                case string category:
                    NavFrame.Navigate(typeof(HomePage), category);
                    break;
            }
        }
    }

    private void NavigateToDefaultPage()
    {
        var defaultPage = AppSettings.Get("DefaultPage") ?? "all";

        if (_isTabMode)
        {
            TabViewItem? targetTab = null;
            foreach (var item in MainTabView.TabItems)
            {
                if (item is TabViewItem tab && tab.Tag is string tag && tag == defaultPage)
                {
                    targetTab = tab;
                    break;
                }
            }

            if (targetTab is not null)
            {
                MainTabView.SelectedItem = targetTab;
            }
            else
            {
                TabNavFrame.Navigate(typeof(HomePage), null);
            }
        }
        else
        {
            NavigationViewItem? targetItem = null;

            foreach (var item in NavView.MenuItems)
            {
                if (item is NavigationViewItem navItem && navItem.Tag is string tag && tag == defaultPage)
                {
                    targetItem = navItem;
                    break;
                }
            }

            if (targetItem is not null)
            {
                NavView.SelectedItem = targetItem;
            }
            else
            {
                NavFrame.Navigate(typeof(HomePage), null);
            }
        }
    }

    private int _dynamicMenuItemCount;

    private void PopulateCategories(IReadOnlyList<string> categories)
    {
        while (_dynamicMenuItemCount > 0)
        {
            NavView.MenuItems.RemoveAt(NavView.MenuItems.Count - 1);
            _dynamicMenuItemCount--;
        }

        var otherCategory = categories.FirstOrDefault(c => c.Contains("其他"));
        var restCategories = categories.Where(c => !c.Contains("其他"));

        foreach (var category in restCategories)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = category.Replace("工具", ""),
                Tag = category,
                Icon = new FontIcon { Glyph = GetCategoryGlyphStatic(category) }
            });
            _dynamicMenuItemCount++;
        }

        if (otherCategory != null)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = otherCategory.Replace("工具", ""),
                Tag = otherCategory,
                Icon = new FontIcon { Glyph = GetCategoryGlyphStatic(otherCategory) }
            });
            _dynamicMenuItemCount++;
        }
    }

    public static string GetCategoryGlyphStatic(string category)
    {
        var customGlyph = AppSettings.Get($"CategoryGlyph_{category}");
        if (!string.IsNullOrWhiteSpace(customGlyph))
            return customGlyph;

        if (category.Contains("处理器", StringComparison.CurrentCultureIgnoreCase))
            return "\uEEA1";
        if (category.Contains("显卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uF211";
        if (category.Contains("显示器", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7F4";
        if (category.Contains("硬盘", StringComparison.CurrentCultureIgnoreCase))
            return "\uEDA2";
        if (category.Contains("内存", StringComparison.CurrentCultureIgnoreCase))
            return "\uEEA0";
        if (category.Contains("外设", StringComparison.CurrentCultureIgnoreCase))
            return "\uE962";
        if (category.Contains("游戏", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7FC";
        if (category.Contains("声卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7F5";
        if (category.Contains("网卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uEDA3";
        if (category.Contains("烤鸡", StringComparison.CurrentCultureIgnoreCase))
            return "\uECAD";
        if (category.Contains("综合", StringComparison.CurrentCultureIgnoreCase))
            return "\uEC4E";
        if (category.Contains("其他", StringComparison.CurrentCultureIgnoreCase))
            return "\uE712";

        return "\uE8B7";
    }

    public void RefreshToolCategories()
    {
        if (_refreshCategoriesInFlight)
        {
            _refreshCategoriesPending = true;
            return;
        }
        _refreshCategoriesInFlight = true;
        _ = RefreshToolCategoriesCoreAsync();
    }

    private async Task RefreshToolCategoriesCoreAsync()
    {
        try
        {
            var categories = await Task.Run(() => ToolCatalog.GetCategories().ToList());

            if (!DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    PopulateCategories(categories);
                    if (_isTabMode)
                        PopulateTabItems(categories);
                }
                finally
                {
                    _refreshCategoriesInFlight = false;
                    if (_refreshCategoriesPending)
                    {
                        _refreshCategoriesPending = false;
                        RefreshToolCategories();
                    }
                }
            }))
            {
                _refreshCategoriesInFlight = false;
                _refreshCategoriesPending = false;
            }
        }
        catch
        {
            _refreshCategoriesInFlight = false;
            _refreshCategoriesPending = false;
        }
    }

    private void OnNavLayoutModeChanged(string mode)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyNavLayoutMode();
            NavigateToDefaultPage();
        });
    }

    private void ApplyNavLayoutMode()
    {
        _isTabMode = NavLayoutModeService.IsTabMode();

        if (_isTabMode)
        {
            NavView.Visibility = Visibility.Collapsed;
            TabModeGrid.Visibility = Visibility.Visible;
            AppTitleBar.IsPaneToggleButtonVisible = false;
            _ = PopulateTabItemsCoreAsync();
        }
        else
        {
            NavView.Visibility = Visibility.Visible;
            TabModeGrid.Visibility = Visibility.Collapsed;
            AppTitleBar.IsPaneToggleButtonVisible = true;
        }
    }

    private async Task PopulateTabItemsCoreAsync()
    {
        try
        {
            var categories = await Task.Run(() => ToolCatalog.GetCategories().ToList());
            DispatcherQueue.TryEnqueue(() =>
            {
                try { PopulateTabItems(categories); } catch { }
            });
        }
        catch { }
    }

    private void PopulateTabItems(IReadOnlyList<string> categories)
    {
        MainTabView.TabItems.Clear();

        MainTabView.TabItems.Add(new TabViewItem
        {
            Header = "全部",
            IconSource = new FontIconSource { Glyph = "\uE80F" },
            Tag = "all",
            IsClosable = false
        });
        MainTabView.TabItems.Add(new TabViewItem
        {
            Header = "常用",
            IconSource = new FontIconSource { Glyph = "\uE735" },
            Tag = "favorites",
            IsClosable = false
        });
        MainTabView.TabItems.Add(new TabViewItem
        {
            Header = "硬件",
            IconSource = new FontIconSource { Glyph = "\uE977" },
            Tag = "hardware",
            IsClosable = false
        });
        MainTabView.TabItems.Add(new TabViewItem
        {
            Header = "内置",
            IconSource = new FontIconSource { Glyph = "\uE90F" },
            Tag = "builtin",
            IsClosable = false
        });
        MainTabView.TabItems.Add(new TabViewItem
        {
            Header = "性能",
            IconSource = new FontIconSource { Glyph = "\uE9D9" },
            Tag = "benchmark",
            IsClosable = false
        });

        if (!RuntimeHelper.IsMsixPackaged)
        {
            MainTabView.TabItems.Add(new TabViewItem
            {
                Header = "社区",
                IconSource = new FontIconSource { Glyph = "\uE779" },
                Tag = "community",
                IsClosable = false
            });
        }

        var otherCategory = categories.FirstOrDefault(c => c.Contains("其他"));
        var restCategories = categories.Where(c => !c.Contains("其他"));

        foreach (var category in restCategories)
        {
            MainTabView.TabItems.Add(new TabViewItem
            {
                Header = category.Replace("工具", ""),
                IconSource = new FontIconSource { Glyph = GetCategoryGlyphStatic(category) },
                Tag = category,
                IsClosable = false
            });
        }

        if (otherCategory != null)
        {
            MainTabView.TabItems.Add(new TabViewItem
            {
                Header = otherCategory.Replace("工具", ""),
                IconSource = new FontIconSource { Glyph = GetCategoryGlyphStatic(otherCategory) },
                Tag = otherCategory,
                IsClosable = false
            });
        }

        MainTabView.TabItems.Add(new TabViewItem
        {
            Header = "设置",
            IconSource = new FontIconSource { Glyph = "\uE713" },
            Tag = "settings",
            IsClosable = false
        });
    }

    private void MainTabView_SelectionChanged(object sender, object args)
    {
        if (_syncingTabSelection) return;
        if (MainTabView.SelectedItem is not TabViewItem tab) return;

        var tag = tab.Tag?.ToString();
        if (tag is null) return;

        _navFromSidebar = true;

        switch (tag)
        {
            case "all":
                TabNavFrame.Navigate(typeof(HomePage), null);
                break;
            case "favorites":
                TabNavFrame.Navigate(typeof(FavoritesPage));
                break;
            case "hardware":
                TabNavFrame.Navigate(typeof(HardwarePage));
                break;
            case "builtin":
                TabNavFrame.Navigate(typeof(BuiltinToolsPage));
                break;
            case "community":
                if (RuntimeHelper.IsMsixPackaged) break;
                TabNavFrame.Navigate(typeof(CommunityToolsPage));
                break;
            case "benchmark":
                _navFromSidebar = false;
                _ = ExecuteBenchmarkToolAsync();
                break;
            case "settings":
                TabNavFrame.Navigate(typeof(SettingsPage));
                break;
            case string category:
                TabNavFrame.Navigate(typeof(HomePage), category);
                break;
        }
    }

    private async Task ExecuteBenchmarkToolAsync()
    {
        NavigateToBenchmark();
    }

    public void NavigateToBenchmark()
    {
        var frame = _isTabMode ? TabNavFrame : NavFrame;
        frame.Navigate(typeof(PerformanceBenchmarkPage));
    }

    public void NavigateToToolPage(Type pageType, object? parameter = null)
    {
        // 设置"独立窗口"模式下，内置工具在新窗口的 Frame 中打开（切换即时生效）
        if (AppSettings.GetBool("BuiltinToolsOpenInWindow", false))
        {
            var title = parameter is ToolContentPageParam p
                ? p.Title
                : (ActiveToolName ?? "内置工具");
            BuiltinToolWindow.Show(pageType, parameter, title);
            return;
        }
        var frame = _isTabMode ? TabNavFrame : NavFrame;
        frame.Navigate(pageType, parameter);
    }

    public void NavigateToSettings(string? highlightSettingKey = null)
    {
        var frame = _isTabMode ? TabNavFrame : NavFrame;
        frame.Navigate(typeof(SettingsPage),
            highlightSettingKey is null
                ? null
                : new SearchNavigationTarget { HighlightSettingKey = highlightSettingKey });
        SyncNavSelection("settings");
    }

    public void NavigateBack()
    {
        // 若前台是独立工具窗口，返回/关闭操作作用于该窗口，避免误操作主窗口导航
        if (BuiltinToolWindow.ActiveWindow is { } toolWindow)
        {
            toolWindow.GoBackOrClose();
            return;
        }
        var frame = _isTabMode ? TabNavFrame : NavFrame;
        if (frame.CanGoBack)
            frame.GoBack();
    }

    public bool CanNavigateBack()
    {
        var frame = _isTabMode ? TabNavFrame : NavFrame;
        return frame.CanGoBack;
    }

    private void PopulateSearchSuggestions()
    {
        var items = UnifiedSearchService.GetQuickPanelItems();
        _searchResults.Clear();
        foreach (var item in items)
            _searchResults.Add(item);
    }

    private void ShowSearchPopup()
    {
        if (_searchDismissed) return;
        SearchPopup.IsOpen = _searchResults.Count > 0;
    }

    private void HideSearchPopup()
    {
        SearchPopup.IsOpen = false;
    }

    private void SearchPopup_GettingFocus(object sender, GettingFocusEventArgs e)
    {
        e.TryCancel();
    }

    private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressSearch || _searchDismissed) return;
        var query = SearchTextBox.Text.Trim();
        if (query.Length == 0)
            PopulateSearchSuggestions();
        ShowSearchPopup();
    }

    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!SearchTextBox.FocusState.HasFlag(FocusState.Programmatic))
                HideSearchPopup();
        });
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearch) return;

        _searchDismissed = false;
        var query = SearchTextBox.Text.Trim();

        if (query.Length == 0)
        {
            _searchDebounceTimer.Stop();
            PopulateSearchSuggestions();
            ShowSearchPopup();
            return;
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounceTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        var query = SearchTextBox.Text.Trim();
        if (query.Length == 0) return;

        _ = SearchInBackgroundAsync(query);
    }

    private async Task SearchInBackgroundAsync(string query)
    {
        try
        {
            var results = await Task.Run(() => UnifiedSearchService.Search(query));
            _searchResults.Clear();
            foreach (var r in results)
                _searchResults.Add(r);
            SearchPopup.IsOpen = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Search] {ex}");
        }
    }

    private void SearchListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchResult result)
        {
            HideSearchPopup();
            HandleSearchResult(result);
        }
    }

    private void SearchSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var first = _searchResults.FirstOrDefault();
        if (first is not null)
        {
            HideSearchPopup();
            HandleSearchResult(first);
        }
    }

    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            SearchListView.SelectedIndex = -1;
            HideSearchPopup();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var idx = SearchListView.SelectedIndex;
            SearchResult? result = idx >= 0 && idx < _searchResults.Count
                ? _searchResults[idx]
                : _searchResults.Count > 0 ? _searchResults[0] : null;

            if (result is not null)
            {
                HideSearchPopup();
                HandleSearchResult(result);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Down)
        {
            if (SearchListView.Items.Count > 0)
            {
                var next = SearchListView.SelectedIndex < 0
                    ? 0
                    : Math.Min(SearchListView.SelectedIndex + 1, SearchListView.Items.Count - 1);
                SearchListView.SelectedIndex = next;
                SearchListView.ScrollIntoView(SearchListView.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Up)
        {
            if (SearchListView.Items.Count > 0)
            {
                var prev = SearchListView.SelectedIndex <= 0
                    ? 0
                    : SearchListView.SelectedIndex - 1;
                SearchListView.SelectedIndex = prev;
                SearchListView.ScrollIntoView(SearchListView.SelectedItem);
            }
            e.Handled = true;
        }
    }

    private void SearchListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (SearchListView.SelectedItem is SearchResult result)
            {
                _suppressSearch = true;
                SearchTextBox.Text = string.Empty;
                _suppressSearch = false;
                HideSearchPopup();
                HandleSearchResult(result);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideSearchPopup();
            e.Handled = true;
        }
    }

    private void HandleSearchResult(SearchResult result)
    {
        var frame = _isTabMode ? TabNavFrame : NavFrame;

        switch (result.Kind)
        {
            case SearchItemKind.ExternalTool:
            case SearchItemKind.CustomTool:
                NavigateToTool(result.MatchKey);
                break;
            case SearchItemKind.BuiltinTool:
                frame.Navigate(typeof(BuiltinToolsPage),
                    new SearchNavigationTarget { HighlightBuiltinId = result.MatchKey });
                SyncNavSelection("builtin");
                break;

            case SearchItemKind.Setting:
                frame.Navigate(typeof(SettingsPage),
                    new SearchNavigationTarget { HighlightSettingKey = result.MatchKey });
                SyncNavSelection("settings");
                break;
            case SearchItemKind.QuickAction:
                HandleQuickAction(result.MatchKey);
                break;
        }
    }

    private void SyncNavSelection(string tag)
    {
        if (_isTabMode)
        {
            _syncingTabSelection = true;
            foreach (var item in MainTabView.TabItems)
            {
                if (item is TabViewItem tab && tab.Tag?.ToString() == tag)
                {
                    MainTabView.SelectedItem = tab;
                    break;
                }
            }
            _syncingTabSelection = false;
        }
        else
        {
            _syncingNavSelection = true;
            foreach (var item in NavView.MenuItems)
            {
                if (item is NavigationViewItem navItem && navItem.Tag is string t && t == tag)
                {
                    NavView.SelectedItem = navItem;
                    break;
                }
            }
            _syncingNavSelection = false;
        }
    }

    private void NavigateToTool(string toolPath)
    {
        try
        {
            var tools = ToolCatalog.GetAllToolsCached();
            var tool = tools.FirstOrDefault(t => t.Path.Equals(toolPath, StringComparison.OrdinalIgnoreCase));
            if (tool is not null)
            {
                var frame = _isTabMode ? TabNavFrame : NavFrame;
                frame.Navigate(typeof(HomePage),
                    new SearchNavigationTarget { HighlightToolPath = toolPath });

                if (!string.IsNullOrEmpty(tool.Category))
                    SyncNavSelection(tool.Category);
            }
        }
        catch { }
    }

    private void HandleQuickAction(string action)
    {
        if (!action.StartsWith("navigate:")) return;
        var target = action["navigate:".Length..];
        var frame = _isTabMode ? TabNavFrame : NavFrame;

        switch (target)
        {
            case "hardware":
                frame.Navigate(typeof(HardwarePage));
                SyncNavSelection("hardware");
                break;
            case "favorites":
                frame.Navigate(typeof(FavoritesPage));
                SyncNavSelection("favorites");
                break;
            case "builtin":
                frame.Navigate(typeof(BuiltinToolsPage));
                SyncNavSelection("builtin");
                break;
            case "benchmark":
                _navFromSidebar = false;
                _ = ExecuteBenchmarkToolAsync();
                break;

            case "settings":
                frame.Navigate(typeof(SettingsPage));
                break;
        }
    }
}
