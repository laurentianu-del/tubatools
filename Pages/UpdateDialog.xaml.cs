using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class UpdateDialog : ContentDialog
{
    private UpdateInfo? _updateInfo;
    private UpdateAsset? _selectedAsset;
    private CancellationTokenSource? _cts;
    private bool _isDownloading;

    public bool SkipThisVersion { get; private set; }

    public UpdateDialog()
    {
        InitializeComponent();
        XamlRoot = App.MainWindow?.Content?.XamlRoot;
    }

    public async Task ShowUpdateAsync(UpdateInfo updateInfo)
    {
        _updateInfo = updateInfo;

        NewVersionText.Text = updateInfo.Version;
        PublishDateText.Text = updateInfo.PublishedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

        var body = updateInfo.Body ?? "暂无更新说明";
        MarkdownTextService.RenderToRichTextBlock(ChangelogText, body);

        _selectedAsset = UpdateService.FindBestAsset(updateInfo.Assets);

        if (_selectedAsset is not null && !string.IsNullOrEmpty(_selectedAsset.GitCodeDownloadUrl))
        {
            GitCodeDownloadButton.Visibility = Visibility.Visible;
        }

        if (_selectedAsset is null)
        {
            ErrorInfoBar.Message = $"未找到适用于 {UpdateService.CurrentArchitecture} 架构的更新文件";
            ErrorInfoBar.IsOpen = true;
            IsPrimaryButtonEnabled = false;
        }

        await ShowAsync();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_isDownloading) return;

        var deferral = args.GetDeferral();
        args.Cancel = true;

        try
        {
            await StartUpdateProcess();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _cts?.Cancel();
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _cts?.Cancel();
        SkipThisVersion = true;
    }

    private async void OnGitCodeDownloadClick(object sender, RoutedEventArgs e)
    {
        if (_isDownloading || _selectedAsset is null) return;

        _cts = new CancellationTokenSource();
        _isDownloading = true;
        GitCodeDownloadButton.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;

        try
        {
            DownloadSection.Visibility = Visibility.Visible;

            var downloadProgress = new Progress<DownloadProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateDownloadProgress(p));
            });

            var filePath = await UpdateService.DownloadFromGitCodeAsync(
                _selectedAsset, downloadProgress, _cts.Token);

            Hide();
            await ShowDownloadCompleteDialog(filePath);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorInfoBar.Message = $"GitCode 下载失败: {ex.Message}";
            ErrorInfoBar.IsOpen = true;
            IsPrimaryButtonEnabled = true;
            IsSecondaryButtonEnabled = true;
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private async Task StartUpdateProcess()
    {
        if (_updateInfo is null || _selectedAsset is null) return;

        _cts = new CancellationTokenSource();
        _isDownloading = true;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;

        try
        {
            ProxyStatusText.Text = "正在从 GitCode/GitHub 下载更新...";
            DownloadSection.Visibility = Visibility.Visible;
            ProxyTestRing.Visibility = Visibility.Collapsed;

            var downloadProgress = new Progress<DownloadProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateDownloadProgress(p));
            });

            var filePath = await UpdateService.DownloadUpdateAsync(
                _selectedAsset, downloadProgress, _cts.Token);

            Hide();
            await ShowDownloadCompleteDialog(filePath);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorInfoBar.Message = ex.Message;
            ErrorInfoBar.IsOpen = true;
            IsPrimaryButtonEnabled = true;
            IsSecondaryButtonEnabled = true;
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private void UpdateDownloadProgress(DownloadProgress p)
    {
        DownloadProgressBar.Value = p.Percentage;
        DownloadPercentText.Text = $"{p.Percentage:F1}%";
        DownloadSpeedText.Text = UpdateService.FormatSpeed(p.SpeedMbps);
        DownloadSizeText.Text = $"{UpdateService.FormatSize(p.BytesReceived)} / {UpdateService.FormatSize(p.TotalBytes)}";
        DownloadTimeText.Text = UpdateService.FormatTime(p.EstimatedRemaining);
    }

    private async Task ShowDownloadCompleteDialog(string filePath)
    {
        var isExe = filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        var dialog = new ContentDialog
        {
            Title = "下载完成",
            XamlRoot = XamlRoot,
            PrimaryButtonText = isExe ? "立即安装" : "打开文件夹",
            SecondaryButtonText = "稍后手动安装",
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var stack = new StackPanel { Spacing = 12 };

        var successBorder = new Border
        {
            Padding = new Thickness(20, 16, 20, 16),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
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
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Green),
            CornerRadius = new CornerRadius(12)
        };
        var checkIcon = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 24,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.White)
        };
        iconBorder.Child = checkIcon;
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        var infoStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };
        infoStack.Children.Add(new TextBlock
        {
            Text = "更新已下载完成",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = $"文件: {Path.GetFileName(filePath)}",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = $"架构: {UpdateService.CurrentArchitecture}",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = isExe ? "点击「立即安装」将关闭本程序并启动安装程序" : "请关闭本程序后解压/安装更新",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        });
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        successBorder.Child = grid;
        stack.Children.Add(successBorder);
        dialog.Content = stack;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                if (isExe)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                    Application.Current.Exit();
                }
                else
                {
                    var folder = Path.GetDirectoryName(filePath)!;
                    System.Diagnostics.Process.Start("explorer.exe", folder);
                }
            }
            catch
            {
            }
        }
    }
}
