using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Net.NetworkInformation;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class NetworkAdapterProxyWindow : Window
{
    private sealed class AdapterCardRefs
    {
        public TextBlock? IpText;
        public TextBlock? GwText;
        public TextBlock? StatusText;
        public Border? StatusBadge;
        public Border? LeftBar;
        public FontIcon? Icon;
        public Border? IconBg;
    }

    private sealed class SpeedRefs
    {
        public TextBlock? DlText;
        public TextBlock? UlText;
        public ProgressBar? DlBar;
        public ProgressBar? UlBar;
    }

    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentOrange = Color.FromArgb(255, 251, 191, 36);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);

    private List<AdapterInfo> _adapters = [];
    private DispatcherTimer? _refreshTimer;
    private DispatcherTimer? _connTimer;
    private readonly Dictionary<int, SpeedRefs> _speedRefs = new();
    private readonly Dictionary<int, AdapterCardRefs> _cardRefs = new();
    private bool _isVisible = true;
    private bool _cardsBuilt;

    public NetworkAdapterProxyWindow()
    {
        InitializeComponent();

        AppWindow.Title = "网络调度器";
        AppWindow.Resize(new SizeInt32(740, 720));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        NetworkAdapterProxyService.StatsUpdated += OnStatsUpdated;
        AppWindow.Changed += OnAppWindowChanged;

        _ = InitializeAsync();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidVisibilityChange) return;
        var visible = sender.IsVisible;
        if (visible && !_isVisible)
        {
            _isVisible = true;
            _refreshTimer?.Start();
            _connTimer?.Start();
            NetworkAdapterProxyService.StartMonitoring(2000);
        }
        else if (!visible && _isVisible)
        {
            _isVisible = false;
            _refreshTimer?.Stop();
            _connTimer?.Stop();
            NetworkAdapterProxyService.StopMonitoring();
        }
    }

    private async Task InitializeAsync()
    {
        _adapters = await Task.Run(() => NetworkAdapterProxyService.GetAdapters());
        BuildAdapterCards();
        BuildSpeedPanel();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (_, _) =>
        {
            _adapters = await Task.Run(() => NetworkAdapterProxyService.GetAdapters());
            if (!_cardsBuilt || _cardRefs.Count != _adapters.Count)
            {
                BuildAdapterCards();
                BuildSpeedPanel();
            }
            else
            {
                UpdateAdapterCards();
            }
        };
        _refreshTimer.Start();

        NetworkAdapterProxyService.StartMonitoring(2000);

        _connTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _connTimer.Tick += async (_, _) =>
        {
            var conns = await Task.Run(() => NetworkAdapterProxyService.GetActiveConnections());
            RenderConnections(conns);
        };
        _connTimer.Start();

        _ = Task.Run(() => NetworkAdapterProxyService.GetActiveConnections())
            .ContinueWith(t => { if (t.Result != null) DispatcherQueue.TryEnqueue(() => RenderConnections(t.Result)); });
    }

    private void OnStatsUpdated(List<AdapterStats> stats)
    {
        DispatcherQueue.TryEnqueue(RefreshSpeed);
    }

    #region Adapter Cards

    private void BuildAdapterCards()
    {
        var grid = AdapterCardsGrid;
        grid.Children.Clear();
        _cardRefs.Clear();
        _cardsBuilt = true;

        if (_adapters.Count == 0)
        {
            var hint = new TextBlock { Text = "未检测到 Wi-Fi 或以太网适配器", Opacity = 0.5, FontSize = 13 };
            grid.Children.Add(hint);
            Grid.SetColumn(hint, 0);
            Grid.SetColumnSpan(hint, 2);
            return;
        }

        for (int i = 0; i < _adapters.Count && i < 2; i++)
        {
            var (card, refs) = CreateAdapterCard(_adapters[i]);
            _cardRefs[_adapters[i].Index] = refs;
            grid.Children.Add(card);
            Grid.SetColumn(card, i);
        }
    }

    private void UpdateAdapterCards()
    {
        foreach (var a in _adapters)
        {
            if (!_cardRefs.TryGetValue(a.Index, out var refs)) continue;
            var accent = a.AccentColor;
            var isUp = a.IsUp;
            var hasNet = a.HasInternet;

            if (refs.IpText != null)
                refs.IpText.Text = a.Addresses.Count > 0 ? string.Join(", ", a.Addresses.Select(x => x.ToString())) : "无 IP";
            if (refs.GwText != null)
                refs.GwText.Text = a.Gateways.Count > 0 ? $"网关 {a.Gateways[0]}" : "无网关";

            var statusColor = hasNet ? AccentGreen : (isUp ? AccentOrange : AccentRed);
            var statusText = hasNet ? "已联网" : (isUp ? "已连接" : "未连接");

            if (refs.StatusText != null)
            {
                refs.StatusText.Text = statusText;
                refs.StatusText.Foreground = new SolidColorBrush(statusColor);
            }
            if (refs.StatusBadge != null)
                refs.StatusBadge.Background = new SolidColorBrush(Color.FromArgb(30, statusColor.R, statusColor.G, statusColor.B));
            if (refs.LeftBar != null)
                refs.LeftBar.Background = new SolidColorBrush(isUp ? accent : Color.FromArgb(255, 120, 120, 120));
            if (refs.Icon != null)
                refs.Icon.Foreground = new SolidColorBrush(isUp ? accent : ThemeColors.DimText);
            if (refs.IconBg != null)
                refs.IconBg.Background = new SolidColorBrush(Color.FromArgb((byte)(isUp ? 30 : 15), accent.R, accent.G, accent.B));
        }
    }

    private (Border card, AdapterCardRefs refs) CreateAdapterCard(AdapterInfo a)
    {
        var accent = a.AccentColor;
        var isUp = a.IsUp;
        var hasNet = a.HasInternet;

        var icon = new FontIcon
        {
            Glyph = a.IsWifi ? "\uEC85" : "\uE8BD",
            FontSize = 24,
            Foreground = new SolidColorBrush(isUp ? accent : ThemeColors.DimText)
        };

        var iconBg = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb((byte)(isUp ? 30 : 15), accent.R, accent.G, accent.B)),
            Child = icon
        };

        var name = new TextBlock
        {
            Text = a.Name, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        var statusColor = hasNet ? AccentGreen : (isUp ? AccentOrange : AccentRed);
        var statusText = hasNet ? "已联网" : (isUp ? "已连接" : "未连接");

        var statusTextBlock = new TextBlock
        {
            Text = statusText, FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(statusColor)
        };

        var statusBadge = new Border
        {
            Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(30, statusColor.R, statusColor.G, statusColor.B)),
            Child = statusTextBlock
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(name);
        header.Children.Add(statusBadge);

        var ip = new TextBlock
        {
            Text = a.Addresses.Count > 0 ? string.Join(", ", a.Addresses.Select(x => x.ToString())) : "无 IP",
            FontSize = 12, FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var gw = new TextBlock
        {
            Text = a.Gateways.Count > 0 ? $"网关 {a.Gateways[0]}" : "无网关",
            FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var speed = new TextBlock
        {
            Text = a.Speed > 0 ? NetworkAdapterProxyService.FormatSpeed(a.Speed / 8) : "",
            FontSize = 11, Opacity = 0.6,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var leftBar = new Border
        {
            Width = 4, CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(isUp ? accent : Color.FromArgb(255, 120, 120, 120))
        };

        var info = new StackPanel { Spacing = 3 };
        info.Children.Add(header);
        info.Children.Add(ip);
        info.Children.Add(gw);
        if (a.Speed > 0) info.Children.Add(speed);

        var body = new Grid { ColumnSpacing = 12 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        body.Children.Add(leftBar); Grid.SetColumn(leftBar, 0);
        body.Children.Add(iconBg); Grid.SetColumn(iconBg, 1);
        body.Children.Add(info); Grid.SetColumn(info, 2);

        var card = new Border
        {
            Padding = new Thickness(16, 14, 16, 14), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            Child = body
        };

        var refs = new AdapterCardRefs
        {
            IpText = ip, GwText = gw, StatusText = statusTextBlock,
            StatusBadge = statusBadge, LeftBar = leftBar,
            Icon = icon, IconBg = iconBg
        };

        return (card, refs);
    }

    #endregion

    #region Speed Panel

    private void BuildSpeedPanel()
    {
        var grid = SpeedGrid;
        grid.Children.Clear();
        _speedRefs.Clear();

        for (int i = 0; i < _adapters.Count && i < 2; i++)
        {
            var a = _adapters[i];
            var accent = a.AccentColor;

            var dlText = new TextBlock { Text = "0 B/s", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(AccentBlue) };
            var ulText = new TextBlock { Text = "0 B/s", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(AccentOrange) };
            var dlBar = new ProgressBar { Height = 4, Foreground = new SolidColorBrush(AccentBlue), Background = new SolidColorBrush(Color.FromArgb(30, AccentBlue.R, AccentBlue.G, AccentBlue.B)) };
            var ulBar = new ProgressBar { Height = 4, Foreground = new SolidColorBrush(AccentOrange), Background = new SolidColorBrush(Color.FromArgb(30, AccentOrange.R, AccentOrange.G, AccentOrange.B)) };

            _speedRefs[a.Index] = new SpeedRefs { DlText = dlText, UlText = ulText, DlBar = dlBar, UlBar = ulBar };

            var panel = new StackPanel { Spacing = 6 };
            var header = new TextBlock { Text = a.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(accent) };

            var dlRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            dlRow.Children.Add(new TextBlock { Text = "↓", FontSize = 13, Opacity = 0.6 });
            dlRow.Children.Add(dlText);

            var ulRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            ulRow.Children.Add(new TextBlock { Text = "↑", FontSize = 13, Opacity = 0.6 });
            ulRow.Children.Add(ulText);

            panel.Children.Add(header);
            panel.Children.Add(dlRow);
            panel.Children.Add(dlBar);
            panel.Children.Add(ulRow);
            panel.Children.Add(ulBar);

            grid.Children.Add(panel);
            Grid.SetColumn(panel, i);
        }
    }

    private void RefreshSpeed()
    {
        foreach (var (ifIndex, refs) in _speedRefs)
        {
            var stats = NetworkAdapterProxyService.GetStatsForAdapter(ifIndex);
            if (stats == null) continue;

            if (refs.DlText != null) refs.DlText.Text = NetworkAdapterProxyService.FormatSpeedFriendly(stats.SpeedDownload);
            if (refs.UlText != null) refs.UlText.Text = NetworkAdapterProxyService.FormatSpeedFriendly(stats.SpeedUpload);

            var adapter = _adapters.FirstOrDefault(a => a.Index == ifIndex);
            if (adapter != null && adapter.Speed > 0)
            {
                if (refs.DlBar != null) refs.DlBar.Value = Math.Min(100, (double)stats.SpeedDownload * 8 / adapter.Speed * 100);
                if (refs.UlBar != null) refs.UlBar.Value = Math.Min(100, (double)stats.SpeedUpload * 8 / adapter.Speed * 100);
            }
        }
    }

    #endregion

    #region Active Connections

    private void RenderConnections(List<ConnectionEntry> connections)
    {
        var scroll = FindScrollViewer(ConnectionList);
        var scrollOffset = scroll?.VerticalOffset ?? 0;

        ConnectionList.Items.Clear();

        if (connections.Count == 0)
        {
            ConnectionList.Items.Add(new TextBlock { Text = "暂无已建立的 TCP 连接", Opacity = 0.5, FontSize = 12 });
            return;
        }

        foreach (var c in connections)
        {
            var row = new Grid { Padding = new Thickness(4, 2, 4, 2), ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            var typeColor = c.AdapterType == "Wi-Fi" ? AccentBlue : AccentGreen;

            var proc = new TextBlock { Text = c.ProcessName, FontSize = 12, FontFamily = new FontFamily("Consolas") };
            var typeTag = new Border
            {
                Padding = new Thickness(6, 1, 6, 1), CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(25, typeColor.R, typeColor.G, typeColor.B)),
                Child = new TextBlock { Text = c.AdapterType, FontSize = 11, Foreground = new SolidColorBrush(typeColor) }
            };
            var addr = new TextBlock { Text = c.RemoteAddress, FontSize = 12, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(ThemeColors.DimText) };
            var port = new TextBlock { Text = c.RemotePort.ToString(), FontSize = 12, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(ThemeColors.DimText) };
            var adapter = new TextBlock { Text = c.AdapterName, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText) };

            row.Children.Add(proc); Grid.SetColumn(proc, 0);
            row.Children.Add(typeTag); Grid.SetColumn(typeTag, 1);
            row.Children.Add(addr); Grid.SetColumn(addr, 2);
            row.Children.Add(port); Grid.SetColumn(port, 3);
            row.Children.Add(adapter); Grid.SetColumn(adapter, 4);

            ConnectionList.Items.Add(row);
        }

        if (scroll != null)
            scroll.ScrollToVerticalOffset(scrollOffset);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var result = FindScrollViewer(VisualTreeHelper.GetChild(parent, i));
            if (result != null) return result;
        }
        return null;
    }

    #endregion

    #region Smart Routing Buttons

    private async void OptimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        OptimizeBtn.IsEnabled = false;
        await Task.Run(() => NetworkAdapterProxyService.OptimizeRouting());
        OptimizeBtn.IsEnabled = true;
        ShowToast("已应用最优加速：有线优先，Wi-Fi 备用", InfoBarSeverity.Success);
    }

    private async void BalanceBtn_Click(object sender, RoutedEventArgs e)
    {
        BalanceBtn.IsEnabled = false;
        await Task.Run(() => NetworkAdapterProxyService.BalanceRouting());
        BalanceBtn.IsEnabled = true;
        ShowToast("已应用均衡分流：多网络共同分担流量", InfoBarSeverity.Success);
    }

    private async void PrioWifiBtn_Click(object sender, RoutedEventArgs e)
    {
        PrioWifiBtn.IsEnabled = false;
        await Task.Run(() => NetworkAdapterProxyService.PrioritizeWifi());
        PrioWifiBtn.IsEnabled = true;
        ShowToast("已设置 Wi-Fi 优先", InfoBarSeverity.Success);
    }

    private async void PrioWiredBtn_Click(object sender, RoutedEventArgs e)
    {
        PrioWiredBtn.IsEnabled = false;
        await Task.Run(() => NetworkAdapterProxyService.PrioritizeWired());
        PrioWiredBtn.IsEnabled = true;
        ShowToast("已设置有线优先", InfoBarSeverity.Success);
    }

    private async void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        ResetBtn.IsEnabled = false;
        await Task.Run(() => NetworkAdapterProxyService.ResetRouting());
        ResetBtn.IsEnabled = true;
        ShowToast("已恢复默认路由", InfoBarSeverity.Success);
    }

    #endregion

    private void ShowToast(string msg, InfoBarSeverity sev)
    {
        ToastBar.Title = msg;
        ToastBar.Severity = sev;
        ToastBar.IsOpen = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        NetworkAdapterProxyService.StopMonitoring();
        _refreshTimer?.Stop();
        _connTimer?.Stop();
        AppWindow.Changed -= OnAppWindowChanged;
        Close();
    }
}
