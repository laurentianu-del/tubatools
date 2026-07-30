using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class FirPeTool : IBuiltinTool
{
    public string Id => "firpe";
    public string Name => "FirPE";
    public string Description => "下载 FirPE 微型 Windows PE 系统，支持安装程序和 ISO 镜像。";
    public string Glyph => "\uE896";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    private const string ExeUrl = "https://gitcode.com/luolangaga/firpe/releases/download/2.1.1/FirPE-V2.1.1.exe";
    private const string IsoUrl = "https://gitcode.com/luolangaga/firpe/releases/download/2.1.1/FirPE-V2.1.1.iso";
    private const string ExeFileName = "FirPE-V2.1.1.exe";
    private const string IsoFileName = "FirPE-V2.1.1.iso";
    private const string ExeTag = "firpe-exe";
    private const string IsoTag = "firpe-iso";

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            var window = new Window();
            var state = new WindowState();

            var content = BuildDialogContent(window, state);

            var page = new Page { Content = content };
            page.RequestedTheme = ThemeService.CurrentElementTheme;

            window.Content = page;
            window.AppWindow.Title = "FirPE 下载";
            window.AppWindow.Resize(new SizeInt32(680, 520));

            try
            {
                var mainPos = App.MainWindow?.AppWindow.Position;
                if (mainPos is not null)
                    window.AppWindow.Move(new PointInt32(mainPos.Value.X + 60, mainPos.Value.Y + 60));
            }
            catch { }

            window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

            ApplyTitleBarTheme(window);
            BackdropService.ApplyBackdrop(window);

            DownloadQueueService.Queue.CollectionChanged += state.OnQueueChanged;
            foreach (var qi in DownloadQueueService.Queue)
                qi.PropertyChanged += state.OnQueueItemPropertyChanged;

            SyncExistingQueueItems(state);

            window.Closed += (_, _) =>
            {
                DownloadQueueService.Queue.CollectionChanged -= state.OnQueueChanged;
                state.UnsubscribeQueueItems();
            };

            window.Activate();
        });

        return Task.CompletedTask;
    }

    private static void SyncExistingQueueItems(WindowState state)
    {
        foreach (var qi in DownloadQueueService.Queue)
        {
            if (qi.Tag is string tag && (tag == ExeTag || tag == IsoTag))
                UpdateCardForItem(state, qi);
        }
    }

    private static void UpdateCardForItem(WindowState state, DownloadItem qi)
    {
        if (qi.Tag is not string tag) return;

        var cardState = tag == ExeTag ? state.ExeCard : state.IsoCard;
        if (cardState is null) return;

        var isDownloading = qi.State is DownloadItemState.Queued or DownloadItemState.Resolving or DownloadItemState.Downloading;
        var isProcessing = qi.State == DownloadItemState.Processing;
        var isCompleted = qi.State == DownloadItemState.Completed;
        var isFailed = qi.State is DownloadItemState.Failed or DownloadItemState.Cancelled;

        cardState.ProgressBar.IsIndeterminate = qi.State is DownloadItemState.Queued or DownloadItemState.Resolving or DownloadItemState.Processing;
        cardState.ProgressBar.Visibility = isDownloading || isProcessing || isCompleted
            ? Visibility.Visible : Visibility.Collapsed;

        if (qi.Progress is not null && qi.State == DownloadItemState.Downloading)
        {
            cardState.ProgressBar.IsIndeterminate = false;
            cardState.ProgressBar.Value = qi.Progress.Percentage;
        }
        else if (isCompleted)
        {
            cardState.ProgressBar.IsIndeterminate = false;
            cardState.ProgressBar.Value = 100;
        }

        cardState.StatusText.Visibility = Visibility.Visible;
        cardState.StatusText.Text = qi.State switch
        {
            DownloadItemState.Queued => "等待下载...",
            DownloadItemState.Resolving => "正在解析下载地址...",
            DownloadItemState.Downloading => qi.Progress is not null
                ? $"{qi.Progress.Percentage:F1}%  ·  {FormatSpeed(qi.Progress.SpeedMbps)}  ·  {FormatRemaining(qi.Progress.EstimatedRemaining)}"
                : "正在下载...",
            DownloadItemState.Processing => qi.ProcessingStatus ?? "正在处理...",
            DownloadItemState.Completed => "下载完成",
            DownloadItemState.Failed => $"下载失败：{qi.ErrorMessage ?? "未知错误"}",
            DownloadItemState.Cancelled => "已取消",
            DownloadItemState.Paused => "已暂停",
            _ => ""
        };

        cardState.StatusText.Foreground = isFailed
            ? new SolidColorBrush(ThemeColors.AccentRed)
            : isCompleted
                ? new SolidColorBrush(ThemeColors.AccentGreen)
                : new SolidColorBrush(ThemeColors.DimText);

        cardState.DownloadBtn.IsEnabled = true;
        cardState.DownloadBtn.Tag = qi;
        cardState.DownloadBtn.Content = isCompleted
            ? new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE8E5", FontSize = 13 },
                    new TextBlock { Text = "打开", FontSize = 13 }
                }
            }
            : isFailed
                ? new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE72C", FontSize = 13 },
                        new TextBlock { Text = "重试", FontSize = 13 }
                    }
                }
                : new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE896", FontSize = 13 },
                        new TextBlock { Text = "下载", FontSize = 13 }
                    }
                };
    }

    private static string FormatSpeed(double speedMbps)
    {
        if (speedMbps <= 0) return "";
        var bytesPerSec = speedMbps * 1024 * 1024 / 8;
        if (bytesPerSec > 1024 * 1024)
            return $"{bytesPerSec / 1024 / 1024:F1} MB/s";
        if (bytesPerSec > 1024)
            return $"{bytesPerSec / 1024:F0} KB/s";
        return $"{bytesPerSec:F0} B/s";
    }

    private static string FormatRemaining(TimeSpan? remaining)
    {
        if (remaining is null) return "";
        if (remaining.Value.TotalSeconds < 1) return "";
        if (remaining.Value.TotalMinutes < 1)
            return $"{remaining.Value.Seconds}秒";
        if (remaining.Value.TotalHours < 1)
            return $"{remaining.Value.Minutes}分{remaining.Value.Seconds}秒";
        return $"{(int)remaining.Value.TotalHours}时{remaining.Value.Minutes}分";
    }

    private ScrollViewer BuildDialogContent(Window window, WindowState state)
    {
        var headerIcon = new Border
        {
            Width = 40,
            Height = 40,
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
            CornerRadius = new CornerRadius(8),
            Child = new FontIcon
            {
                FontSize = 20,
                Foreground = new SolidColorBrush(Colors.White),
                Glyph = "\uE896"
            }
        };

        var titleText = new TextBlock
        {
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = "FirPE 下载"
        };
        var descText = new TextBlock
        {
            FontSize = 12,
            Opacity = 1.0,
            Text = "微型 Windows PE 系统，可用于系统维护、数据恢复等场景"
        };

        var headerPanel = new StackPanel { Spacing = 2 };
        headerPanel.Children.Add(titleText);
        headerPanel.Children.Add(descText);

        var headerGrid = new Grid { ColumnSpacing = 12 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.Children.Add(headerIcon);
        headerGrid.Children.Add(headerPanel);
        Grid.SetColumn(headerPanel, 1);

        var infoBar = new InfoBar
        {
            Title = "FirPE 说明",
            Message = "FirPE 是一款基于 Windows PE 的微型系统，适用于系统安装、维护和数据恢复。安装程序可直接在 Windows 下运行制作启动盘；ISO 镜像可用于刻录光盘或写入 U 盘。",
            Severity = InfoBarSeverity.Informational,
            IsOpen = true,
            IsClosable = false
        };

        var exeCard = CreateDownloadCard(
            "安装程序",
            ExeFileName,
            "在 Windows 下运行，可直接制作启动 U 盘",
            "\uE896",
            ThemeColors.AccentBlue,
            ExeUrl,
            ExeTag,
            "安装程序",
            true,
            state,
            out var exeCardState);
        state.ExeCard = exeCardState;

        var isoCard = CreateDownloadCard(
            "ISO 镜像",
            IsoFileName,
            "可刻录光盘或使用 Rufus/Ventoy 等工具写入 U 盘",
            "\uE8F2",
            ThemeColors.AccentGreen,
            IsoUrl,
            IsoTag,
            "ISO 镜像",
            false,
            state,
            out var isoCardState);
        state.IsoCard = isoCardState;

        var cardsPanel = new StackPanel { Spacing = 10 };
        cardsPanel.Children.Add(exeCard);
        cardsPanel.Children.Add(isoCard);

        var root = new StackPanel { Spacing = 14, Padding = new Thickness(24, 48, 24, 24) };
        root.Children.Add(headerGrid);
        root.Children.Add(infoBar);
        root.Children.Add(cardsPanel);

        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private Border CreateDownloadCard(
        string label,
        string fileName,
        string description,
        string glyph,
        Color accentColor,
        string url,
        string tag,
        string displayName,
        bool isInstaller,
        WindowState windowState,
        out CardState cardState)
    {
        var iconBorder = new Border
        {
            Width = 44,
            Height = 44,
            Background = new SolidColorBrush(Color.FromArgb(30, accentColor.R, accentColor.G, accentColor.B)),
            CornerRadius = new CornerRadius(8),
            Child = new FontIcon
            {
                FontSize = 20,
                Foreground = new SolidColorBrush(accentColor),
                Glyph = glyph
            }
        };

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var fileText = new TextBlock
        {
            Text = fileName,
            FontSize = 12,
            Opacity = 0.68
        };
        var descText = new TextBlock
        {
            Text = description,
            FontSize = 12,
            Opacity = 0.68,
            TextWrapping = TextWrapping.Wrap
        };

        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(labelText);
        infoPanel.Children.Add(fileText);
        infoPanel.Children.Add(descText);

        var statusText = new TextBlock
        {
            FontSize = 12,
            Opacity = 1.0,
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };
        infoPanel.Children.Add(statusText);

        var progressBar = new ProgressBar
        {
            Visibility = Visibility.Collapsed,
            IsIndeterminate = true,
            Margin = new Thickness(0, 4, 0, 0)
        };
        infoPanel.Children.Add(progressBar);

        var downloadBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE896", FontSize = 13 },
                    new TextBlock { Text = "下载", FontSize = 13 }
                }
            },
            VerticalAlignment = VerticalAlignment.Center
        };
        downloadBtn.Click += (s, _) =>
        {
            if (s is Button btn && btn.Tag is DownloadItem item && item.State == DownloadItemState.Completed)
            {
                try
                {
                    var dir = item.DestinationPath;
                    var file = Directory.GetFiles(dir).FirstOrDefault(f =>
                        Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    if (file is not null)
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
                    else
                        System.Diagnostics.Process.Start("explorer.exe", dir);
                }
                catch { }
                return;
            }

            var destDir = Path.Combine(Path.GetTempPath(), "TubaWinUi3_FirPE");
            DownloadQueueService.Enqueue(
                $"FirPE {displayName}",
                url,
                destDir,
                postProcessor: isInstaller ? new InstallerLaunchProcessor() : null,
                description: fileName,
                glyph: "\uE896",
                tag: tag);
        };

        var browserBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE774", FontSize = 13 },
                    new TextBlock { Text = "浏览器下载", FontSize = 13 }
                }
            },
            VerticalAlignment = VerticalAlignment.Center
        };
        browserBtn.Click += async (_, _) =>
        {
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); } catch { }
        };

        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        actionsPanel.Children.Add(downloadBtn);
        actionsPanel.Children.Add(browserBtn);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconBorder);
        grid.Children.Add(infoPanel);
        Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(actionsPanel);
        Grid.SetColumn(actionsPanel, 2);

        cardState = new CardState
        {
            ProgressBar = progressBar,
            StatusText = statusText,
            DownloadBtn = downloadBtn,
            Tag = tag
        };

        return new Border
        {
            Padding = new Thickness(16),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = grid
        };
    }

    private static void ApplyTitleBarTheme(Window window)
    {
        var tb = window.AppWindow.TitleBar;
        var isDark = ThemeService.CurrentTheme == AppTheme.Dark ||
                     (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

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

    private sealed class WindowState
    {
        public CardState? ExeCard;
        public CardState? IsoCard;
        private bool _syncing;

        public void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
                foreach (DownloadItem old in e.OldItems)
                    old.PropertyChanged -= OnQueueItemPropertyChanged;
            if (e.NewItems is not null)
                foreach (DownloadItem ni in e.NewItems)
                    ni.PropertyChanged += OnQueueItemPropertyChanged;
            SyncCards();
        }

        public void OnQueueItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DownloadItem.State) or nameof(DownloadItem.Progress) or nameof(DownloadItem.ProcessingStatus) or nameof(DownloadItem.ErrorMessage))
                SyncCards();
        }

        public void UnsubscribeQueueItems()
        {
            foreach (var qi in DownloadQueueService.Queue)
                qi.PropertyChanged -= OnQueueItemPropertyChanged;
        }

        private void SyncCards()
        {
            if (_syncing) return;
            _syncing = true;
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    foreach (var qi in DownloadQueueService.Queue)
                    {
                        if (qi.Tag is string tag && (tag == ExeTag || tag == IsoTag))
                            UpdateCardForItem(this, qi);
                    }
                }
                finally
                {
                    _syncing = false;
                }
            });
        }
    }

    private sealed class CardState
    {
        public ProgressBar ProgressBar = null!;
        public TextBlock StatusText = null!;
        public Button DownloadBtn = null!;
        public string Tag = "";
    }
}
