using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

public sealed partial class UpdateBanner : UserControl
{
    private UpdateInfo? _updateInfo;
    private bool _isDownloaded;
    private bool _isDownloading;

    public UpdateBanner()
    {
        InitializeComponent();
    }

    public void ShowUpdateAvailable(UpdateInfo update)
    {
        _updateInfo = update;
        _isDownloaded = false;
        _isDownloading = false;

        BannerText.Text = $"发现新版本 v{update.Version}";
        ActionButton.Content = "开始更新";
        ActionButton.Visibility = Visibility.Visible;
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    public void ShowDownloading()
    {
        _isDownloading = true;
        _isDownloaded = false;

        BannerText.Text = "正在下载更新...";
        BannerText.Visibility = Visibility.Collapsed;
        DownloadProgressBar.Visibility = Visibility.Visible;
        ActionButton.Content = "下载中";
        ActionButton.IsEnabled = false;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    public void ShowDownloadProgress(double percentage)
    {
        DownloadProgressBar.IsIndeterminate = false;
        DownloadProgressBar.Value = percentage;
    }

    public void ShowDownloadComplete()
    {
        _isDownloaded = true;
        _isDownloading = false;

        BannerText.Text = "更新已就绪，点击开始安装";
        BannerText.Visibility = Visibility.Visible;
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        ActionButton.Content = "立即安装";
        ActionButton.IsEnabled = true;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    public void ShowDownloadFailed(string error)
    {
        _isDownloading = false;

        BannerText.Text = $"更新下载失败：{error}";
        BannerText.Visibility = Visibility.Visible;
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        ActionButton.Content = "重试";
        ActionButton.IsEnabled = true;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloaded)
        {
            UpdateService.LaunchUpdateFromItem();
            return;
        }

        if (_isDownloading) return;

        if (_updateInfo is not null)
        {
            var item = UpdateService.AutoDownloadUpdate(_updateInfo);
            if (item is not null)
            {
                ShowDownloading();
                item.PropertyChanged += OnDownloadItemPropertyChanged;
            }
        }
    }

    private void OnDownloadItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not DownloadItem item) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(DownloadItem.State):
                    switch (item.State)
                    {
                        case DownloadItemState.Completed:
                            item.PropertyChanged -= OnDownloadItemPropertyChanged;
                            ShowDownloadComplete();
                            break;
                        case DownloadItemState.Failed:
                            item.PropertyChanged -= OnDownloadItemPropertyChanged;
                            ShowDownloadFailed(item.ErrorMessage ?? "未知错误");
                            break;
                    }
                    break;
                case nameof(DownloadItem.Progress):
                    if (item.Progress is not null && item.Progress.TotalBytes > 0)
                    {
                        ShowDownloadProgress(item.Progress.Percentage);
                    }
                    break;
            }
        });
    }

    private void ChangelogButton_Click(object sender, RoutedEventArgs e)
    {
        WhatsNewWindow.Show();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        UpdateService.ClearPendingUpdate();
    }
}
