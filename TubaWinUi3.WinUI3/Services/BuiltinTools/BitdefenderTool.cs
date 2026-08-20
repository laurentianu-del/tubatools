using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class BitdefenderTool : IBuiltinTool
{
    public string Id => "bitdefender";
    public string Name => "Bitdefender 杀毒";
    public string Description => "目前最有效的银狐病毒查杀工具，360和卡巴斯基面对银狐病毒比较无力。";
    public string Glyph => "\uE72E";  // Shield icon
    public string Category => "安全工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    private const string DownloadUrl = "https://download.bitdefender.com/windows/installer/en-us/bitdefender_avfree.exe";
    private const string FileName = "bitdefender_avfree.exe";
    private const string DownloadTag = "bitdefender-installer";

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            var state = new WindowState();

            var content = BuildDialogContent(state);

            DownloadQueueService.Queue.CollectionChanged += state.OnQueueChanged;
            foreach (var qi in DownloadQueueService.Queue)
                qi.PropertyChanged += state.OnQueueItemPropertyChanged;

            SyncExistingQueueItems(state);

            App.MainWindow?.NavigateToToolPage(typeof(ToolContentPage), new ToolContentPageParam
            {
                Title = "Bitdefender 银狐病毒克星",
                Description = "目前最有效的银狐病毒查杀工具，360和卡巴斯基面对银狐病毒比较无力",
                Content = content,
                OnClose = () =>
                {
                    DownloadQueueService.Queue.CollectionChanged -= state.OnQueueChanged;
                    state.UnsubscribeQueueItems();
                }
            });
        });

        return Task.CompletedTask;
    }

    private static void SyncExistingQueueItems(WindowState state)
    {
        foreach (var qi in DownloadQueueService.Queue)
        {
            if (qi.Tag is string tag && tag == DownloadTag)
                UpdateCardForItem(state, qi);
        }
    }

    private static void UpdateCardForItem(WindowState state, DownloadItem qi)
    {
        if (qi.Tag is not string tag || tag != DownloadTag) return;
        if (state.Card is null) return;

        var isDownloading = qi.State is DownloadItemState.Queued or DownloadItemState.Resolving or DownloadItemState.Downloading;
        var isProcessing = qi.State == DownloadItemState.Processing;
        var isCompleted = qi.State == DownloadItemState.Completed;
        var isFailed = qi.State is DownloadItemState.Failed or DownloadItemState.Cancelled;

        state.Card.ProgressBar.IsIndeterminate = qi.State is DownloadItemState.Queued or DownloadItemState.Resolving or DownloadItemState.Processing;
        state.Card.ProgressBar.Visibility = isDownloading || isProcessing || isCompleted
            ? Visibility.Visible : Visibility.Collapsed;

        if (qi.Progress is not null && qi.State == DownloadItemState.Downloading)
        {
            state.Card.ProgressBar.IsIndeterminate = false;
            state.Card.ProgressBar.Value = qi.Progress.Percentage;
        }
        else if (isCompleted)
        {
            state.Card.ProgressBar.IsIndeterminate = false;
            state.Card.ProgressBar.Value = 100;
        }

        state.Card.StatusText.Visibility = Visibility.Visible;
        state.Card.StatusText.Text = qi.State switch
        {
            DownloadItemState.Queued => "等待下载...",
            DownloadItemState.Resolving => "正在解析下载地址...",
            DownloadItemState.Downloading => qi.Progress is not null
                ? $"{qi.Progress.Percentage:F1}%  ·  {FormatSpeed(qi.Progress.SpeedMbps)}  ·  {FormatRemaining(qi.Progress.EstimatedRemaining)}"
                : "正在下载...",
            DownloadItemState.Processing => qi.ProcessingStatus ?? "正在处理...",
            DownloadItemState.Completed => "下载完成，正在启动安装程序...",
            DownloadItemState.Failed => $"下载失败：{qi.ErrorMessage ?? "未知错误"}",
            DownloadItemState.Cancelled => "已取消",
            DownloadItemState.Paused => "已暂停",
            _ => ""
        };

        state.Card.StatusText.Foreground = isFailed
            ? new SolidColorBrush(ThemeColors.AccentRed)
            : isCompleted
                ? new SolidColorBrush(ThemeColors.AccentGreen)
                : new SolidColorBrush(ThemeColors.DimText);

        state.Card.DownloadBtn.IsEnabled = true;
        state.Card.DownloadBtn.Tag = qi;
        state.Card.DownloadBtn.Content = isCompleted
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
                        new TextBlock { Text = "下载安装", FontSize = 13 }
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

    private ScrollViewer BuildDialogContent(WindowState state)
    {
        // 推荐原因说明
        var infoBar = new InfoBar
        {
            Title = "银狐病毒克星",
            Message = "Bitdefender 是目前比较有效的消灭银狐病毒的杀毒软件。360和卡巴斯基在面对银狐病毒有点无力！" +
                      "银狐病毒是一种高度隐蔽的远控木马，Bitdefender 采用先进的行为检测技术，能有效查杀。",
            Severity = InfoBarSeverity.Warning,
            IsOpen = true,
            IsClosable = false
        };

        // 下载卡片
        var downloadCard = CreateDownloadCard(state);

        var root = new StackPanel { Spacing = 14, Padding = new Thickness(24, 0, 24, 24) };
        root.Children.Add(infoBar);
        root.Children.Add(downloadCard);

        return new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 1080
        };
    }

    private Border CreateDownloadCard(WindowState windowState)
    {
        var iconBorder = new Border
        {
            Width = 44,
            Height = 44,
            Background = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215)),
            CornerRadius = new CornerRadius(8),
            Child = new FontIcon
            {
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                Glyph = "\uE72E"
            }
        };

        var labelText = new TextBlock
        {
            Text = "Bitdefender 免费版",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        };
        var fileText = new TextBlock
        {
            Text = FileName,
            FontSize = 12,
            Opacity = 0.68
        };
        var descText = new TextBlock
        {
            Text = "官方直连下载，支持 Windows 10/11，下载后自动启动安装程序",
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
                    new TextBlock { Text = "下载安装", FontSize = 13 }
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
                        Path.GetFileName(f).Equals(FileName, StringComparison.OrdinalIgnoreCase));
                    if (file is not null)
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
                    else
                        System.Diagnostics.Process.Start("explorer.exe", dir);
                }
                catch { }
                return;
            }

            var destDir = Path.Combine(Path.GetTempPath(), "TubaWinUi3_Bitdefender");
            DownloadQueueService.Enqueue(
                "Bitdefender 免费版",
                DownloadUrl,
                destDir,
                postProcessor: new InstallerLaunchProcessor(),
                description: FileName,
                glyph: "\uE72E",
                tag: DownloadTag);
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
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri(DownloadUrl)); } catch { }
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

        windowState.Card = new CardState
        {
            ProgressBar = progressBar,
            StatusText = statusText,
            DownloadBtn = downloadBtn
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

    private sealed class WindowState
    {
        public CardState? Card;
        private bool _syncing;

        public void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
                foreach (DownloadItem old in e.OldItems)
                    old.PropertyChanged -= OnQueueItemPropertyChanged;
            if (e.NewItems is not null)
                foreach (DownloadItem ni in e.NewItems)
                    ni.PropertyChanged += OnQueueItemPropertyChanged;
            SyncCard();
        }

        public void OnQueueItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DownloadItem.State) or nameof(DownloadItem.Progress) or nameof(DownloadItem.ProcessingStatus) or nameof(DownloadItem.ErrorMessage))
                SyncCard();
        }

        public void UnsubscribeQueueItems()
        {
            foreach (var qi in DownloadQueueService.Queue)
                qi.PropertyChanged -= OnQueueItemPropertyChanged;
        }

        private void SyncCard()
        {
            if (_syncing) return;
            _syncing = true;
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    foreach (var qi in DownloadQueueService.Queue)
                    {
                        if (qi.Tag is string tag && tag == DownloadTag)
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
    }
}
