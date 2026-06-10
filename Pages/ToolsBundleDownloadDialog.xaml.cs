using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class ToolsBundleDownloadDialog : ContentDialog
{
    private ToolsBundleUpdateInfo? _updateInfo;
    private CancellationTokenSource? _cts;
    private bool _isBusy;

    public bool DownloadSucceeded { get; private set; }

    public void SetDescription(string text)
    {
        DescText.Text = text;
    }

    public ToolsBundleDownloadDialog()
    {
        InitializeComponent();
        XamlRoot = App.MainWindow?.Content?.XamlRoot;
    }

    public async Task ShowDownloadAsync(ToolsBundleUpdateInfo? info = null)
    {
        if (info is not null && info.HasUpdate)
        {
            _updateInfo = info;
            var sizeStr = info.Size > 0 ? ToolsBundleService.FormatSize(info.Size) : "";
            DescText.Text = string.IsNullOrEmpty(sizeStr)
                ? $"发现新版本工具包 v{info.Version}，下载完成后即可使用全部功能。"
                : $"发现新版本工具包 v{info.Version}（{sizeStr}），下载完成后即可使用全部功能。";
        }
        else
        {
            ResolvingSection.Visibility = Visibility.Visible;
        }

        await ShowAsync();

        if (_updateInfo is null && !_isBusy)
        {
            _ = ResolveAndShowAsync();
        }
    }

    private async Task ResolveAndShowAsync()
    {
        try
        {
            var info = await ToolsBundleService.CheckForToolsUpdateAsync();
            ResolvingSection.Visibility = Visibility.Collapsed;

            if (info is null)
            {
                DescText.Text = "无法获取工具包信息，请检查网络连接后重试。";
                return;
            }

            _updateInfo = info;

            if (!info.HasUpdate)
            {
                DescText.Text = "当前工具包已是最新版本，无需下载。";
                IsPrimaryButtonEnabled = false;
                return;
            }

            var sizeStr = info.Size > 0 ? ToolsBundleService.FormatSize(info.Size) : "";
            DescText.Text = string.IsNullOrEmpty(sizeStr)
                ? $"发现工具包 v{info.Version}，下载完成后即可使用全部功能。"
                : $"发现工具包 v{info.Version}（{sizeStr}），下载完成后即可使用全部功能。";
        }
        catch (Exception ex)
        {
            ResolvingSection.Visibility = Visibility.Collapsed;
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_isBusy)
        {
            args.Cancel = true;
            return;
        }

        var deferral = args.GetDeferral();
        args.Cancel = true;

        try
        {
            await StartDownloadAsync();
        }
        finally
        {
            try { deferral.Complete(); } catch { }
        }
    }

    private async Task StartDownloadAsync()
    {
        if (_updateInfo is null || !_updateInfo.HasUpdate)
        {
            _updateInfo = await ToolsBundleService.CheckForToolsUpdateAsync();
            if (_updateInfo is null || !_updateInfo.HasUpdate)
            {
                ErrorBar.Message = "未找到可用的工具包更新。";
                ErrorBar.IsOpen = true;
                return;
            }
        }

        _cts = new CancellationTokenSource();
        _isBusy = true;
        IsPrimaryButtonEnabled = false;
        CloseButtonText = null;

        try
        {
            ProgressSection.Visibility = Visibility.Visible;
            ResolvingSection.Visibility = Visibility.Collapsed;
            PrimaryButtonText = "下载中...";
            DownloadProgressBar.Value = 0;

            var progress = new Progress<ToolsBundleProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateProgress(p));
            });

            var success = await ToolsBundleService.DownloadAndExtractAsync(_updateInfo, progress, _cts.Token);

            if (success)
            {
                DownloadSucceeded = true;
                Hide();
                await ShowSuccessDialog();
            }
            else
            {
                ErrorBar.Message = "工具包下载或解压失败，请重试。";
                ErrorBar.IsOpen = true;
                IsPrimaryButtonEnabled = true;
                PrimaryButtonText = "重试";
            }
        }
        catch (OperationCanceledException)
        {
            IsPrimaryButtonEnabled = true;
            PrimaryButtonText = "重试";
        }
        catch (Exception ex)
        {
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
            IsPrimaryButtonEnabled = true;
            PrimaryButtonText = "重试";
        }
        finally
        {
            _isBusy = false;
            CloseButtonText = "跳过";
        }
    }

    private void UpdateProgress(ToolsBundleProgress p)
    {
        if (p.Percentage >= 100 && p.BytesReceived == 0)
        {
            ProgressLabel.Text = "正在解压工具包...";
            DownloadProgressBar.IsIndeterminate = true;
            PercentText.Text = "解压中";
            SpeedText.Text = "--";
            SizeText.Text = "--";
            TimeText.Text = "--";
            return;
        }

        ProgressLabel.Text = "正在下载工具包...";
        DownloadProgressBar.IsIndeterminate = false;
        DownloadProgressBar.Value = p.Percentage;
        PercentText.Text = $"{p.Percentage:F1}%";
        SpeedText.Text = ToolsBundleService.FormatSpeed(p.SpeedMbps);
        SizeText.Text = $"{ToolsBundleService.FormatSize(p.BytesReceived)} / {ToolsBundleService.FormatSize(p.TotalBytes)}";
        TimeText.Text = ToolsBundleService.FormatTime(p.EstimatedRemaining);
    }

    private async Task ShowSuccessDialog()
    {
        var dialog = new ContentDialog
        {
            Title = "下载完成",
            XamlRoot = XamlRoot,
            PrimaryButtonText = "完成",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var stack = new StackPanel { Spacing = 12 };

        var border = new Border
        {
            Padding = new Thickness(20, 16, 20, 16),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBorder = new Border
        {
            Width = 48,
            Height = 48,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Green),
            CornerRadius = new CornerRadius(12)
        };
        iconBorder.Child = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 24,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        infoStack.Children.Add(new TextBlock
        {
            Text = "工具包下载完成！",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = "已解压到工具目录，刷新后可直接使用全部工具。",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        if (_updateInfo is not null)
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = $"版本：v{_updateInfo.Version}",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        border.Child = grid;
        stack.Children.Add(border);
        dialog.Content = stack;

        await dialog.ShowAsync();
    }
}
