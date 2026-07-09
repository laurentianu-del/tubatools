using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;

namespace TubaWinUi3.Pages;

public sealed partial class UpdateWindow : Window
{
    private UpdateInfo? _updateInfo;
    private UpdateAsset? _portableAsset;
    private UpdateAsset? _installerAsset;
    private bool _isDownloading;
    private bool _isPortableMode;
    private DownloadItem? _downloadItem;

    public bool SkipThisVersion { get; private set; }

    public UpdateWindow(UpdateInfo updateInfo)
    {
        InitializeComponent();

        AppWindow.Title = "图吧工具箱 - 发现新版本";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var screenArea = displayArea.WorkArea;
        var width = (int)(screenArea.Width * 0.42);
        var height = (int)(screenArea.Height * 0.65);
        width = Math.Max(width, 560);
        height = Math.Max(height, 520);
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            (screenArea.Width - width) / 2,
            (screenArea.Height - height) / 2));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        _isPortableMode = !RuntimeHelper.IsInstalled;

        SetupCardHover(GitCodeCard);
        SetupCardHover(GitHubCard);
        SetupCardHover(SkipCard);
        SetupCardHover(IgnoreCard);

        if (RuntimeHelper.IsLiteBuild)
        {
            TitleText.Text = "发现新版本（精简版）";
        }

        PopulateUpdateInfo(updateInfo);
    }

    private void SetupCardHover(Border card)
    {
        var hoverBrush = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
        var normalBrush = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];

        card.PointerEntered += (s, e) =>
        {
            if (!_isDownloading) card.Background = hoverBrush;
        };
        card.PointerExited += (s, e) =>
        {
            card.Background = normalBrush;
        };
    }

    private void PopulateUpdateInfo(UpdateInfo updateInfo)
    {
        _updateInfo = updateInfo;

        NewVersionText.Text = updateInfo.Version;
        PublishDateText.Text = updateInfo.PublishedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

        var body = updateInfo.Body ?? "暂无更新说明";
        MarkdownTextService.RenderToRichTextBlock(ChangelogText, body);

        if (_isPortableMode)
        {
            _portableAsset = UpdateService.FindBestPortableAsset(updateInfo.Assets);
            _installerAsset = UpdateService.FindBestInstallerAsset(updateInfo.Assets);
        }
        else
        {
            _portableAsset = null;
            _installerAsset = UpdateService.FindBestAsset(updateInfo.Assets);
        }

        var activeAsset = _isPortableMode ? _portableAsset ?? _installerAsset : _installerAsset;
        var hasGitCode = activeAsset is not null && !string.IsNullOrEmpty(activeAsset.GitCodeDownloadUrl);

        GitCodeCard.Visibility = hasGitCode ? Visibility.Visible : Visibility.Collapsed;

        if (activeAsset is null)
        {
            ErrorInfoBar.Message = $"未找到适用于 {UpdateService.CurrentArchitecture} 架构的更新文件";
            ErrorInfoBar.IsOpen = true;
            GitCodeCard.IsHitTestVisible = false;
            GitHubCard.IsHitTestVisible = false;
            GitCodeCard.Opacity = 0.4;
            GitHubCard.Opacity = 0.4;
        }

        if (RuntimeHelper.IsLiteBuild && activeAsset is not null)
        {
            var isLiteAsset = activeAsset.Name.Contains("Lite", StringComparison.OrdinalIgnoreCase);
            if (!isLiteAsset)
            {
                ErrorInfoBar.Message = "当前为精简版，但未找到精简版更新文件。请前往发布页面手动下载。";
                ErrorInfoBar.IsOpen = true;
                GitCodeCard.IsHitTestVisible = false;
                GitHubCard.IsHitTestVisible = false;
                GitCodeCard.Opacity = 0.4;
                GitHubCard.Opacity = 0.4;
            }
        }
    }

    private void OnGitCodeDownloadClick(object sender, TappedRoutedEventArgs e)
    {
        if (_isDownloading) return;

        var asset = _isPortableMode ? _portableAsset ?? _installerAsset : _installerAsset;
        if (asset is null) return;

        StartDownload(asset, useGitCode: true);
    }

    private void OnGitHubDownloadClick(object sender, TappedRoutedEventArgs e)
    {
        if (_isDownloading) return;

        var asset = _isPortableMode ? _portableAsset ?? _installerAsset : _installerAsset;
        if (asset is null) return;

        StartDownload(asset, useGitCode: false);
    }

    private void OnSkipVersionClick(object sender, TappedRoutedEventArgs e)
    {
        Close();
    }

    private void OnIgnoreVersionClick(object sender, TappedRoutedEventArgs e)
    {
        SkipThisVersion = true;
        if (_updateInfo is not null)
            UpdateService.SetSkippedVersion(_updateInfo.Version);
        Close();
    }

    private void StartDownload(UpdateAsset asset, bool useGitCode)
    {
        _isDownloading = true;
        ActionButtonsPanel.Visibility = Visibility.Collapsed;

        DownloadSection.Visibility = Visibility.Visible;
        DownloadTitleText.Text = useGitCode ? "正在从 GitCode 下载更新" : "正在从 GitHub 下载更新";
        StatusText.Text = "已加入下载队列...";
        StatusIcon.Glyph = "\uE896";

        try
        {
            _downloadItem = UpdateService.EnqueueUpdateDownload(asset, useGitCode, _isPortableMode);
            _downloadItem.PropertyChanged += OnDownloadItemPropertyChanged;
        }
        catch (Exception ex)
        {
            ErrorInfoBar.Message = $"下载失败: {ex.Message}";
            ErrorInfoBar.IsOpen = true;
            ResetToIdle();
        }
    }

    private void OnDownloadItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_downloadItem is null) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(DownloadItem.State):
                    UpdateStateUI(_downloadItem);
                    break;
                case nameof(DownloadItem.Progress):
                    UpdateProgressUI(_downloadItem);
                    break;
                case nameof(DownloadItem.ErrorMessage):
                    if (_downloadItem.ErrorMessage is not null)
                    {
                        ErrorInfoBar.Message = $"下载失败: {_downloadItem.ErrorMessage}";
                        ErrorInfoBar.IsOpen = true;
                    }
                    break;
            }
        });
    }

    private void UpdateStateUI(DownloadItem item)
    {
        switch (item.State)
        {
            case DownloadItemState.Queued:
                DownloadTitleText.Text = "等待下载...";
                StatusText.Text = "已加入下载队列，等待中...";
                break;
            case DownloadItemState.Resolving:
                DownloadTitleText.Text = "正在解析下载链接...";
                StatusText.Text = "正在解析下载链接...";
                break;
            case DownloadItemState.Downloading:
                DownloadTitleText.Text = "正在下载更新";
                StatusText.Text = "正在下载更新...";
                DownloadRing.IsActive = true;
                break;
            case DownloadItemState.Processing:
                DownloadTitleText.Text = "正在处理更新文件...";
                StatusText.Text = item.ProcessingStatus ?? "正在处理...";
                DownloadProgressBar.IsIndeterminate = true;
                break;
            case DownloadItemState.Completed:
                _isDownloading = false;
                ShowDownloadComplete(item);
                break;
            case DownloadItemState.Failed:
                _isDownloading = false;
                ErrorInfoBar.Message = $"下载失败: {item.ErrorMessage ?? "未知错误"}";
                ErrorInfoBar.IsOpen = true;
                ResetToIdle();
                break;
            case DownloadItemState.Cancelled:
                _isDownloading = false;
                ResetToIdle();
                break;
        }
    }

    private void UpdateProgressUI(DownloadItem item)
    {
        if (item.Progress is null) return;

        var p = item.Progress;
        DownloadProgressBar.IsIndeterminate = false;
        DownloadProgressBar.Value = p.Percentage;
        DownloadPercentText.Text = $"{p.Percentage:F1}%";
        DownloadSpeedText.Text = DownloadQueueService.FormatSpeed(p.SpeedMbps);
        DownloadSizeText.Text = $"{DownloadQueueService.FormatSize(p.BytesReceived)} / {DownloadQueueService.FormatSize(p.TotalBytes)}";
        DownloadTimeText.Text = DownloadQueueService.FormatTime(p.EstimatedRemaining);
    }

    private void ResetToIdle()
    {
        if (_downloadItem is not null)
        {
            _downloadItem.PropertyChanged -= OnDownloadItemPropertyChanged;
            _downloadItem = null;
        }

        DownloadSection.Visibility = Visibility.Collapsed;
        DownloadProgressBar.IsIndeterminate = false;
        ActionButtonsPanel.Visibility = Visibility.Visible;
        StatusText.Text = "请选择下载源或跳过此版本";
        StatusIcon.Glyph = "\uE946";
    }

    private void ShowDownloadComplete(DownloadItem item)
    {
        DownloadSection.Visibility = Visibility.Collapsed;
        DownloadCompleteSection.Visibility = Visibility.Visible;

        var fileName = item.ResolvedFileName ?? "更新文件";
        var isZip = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var isExe = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        CompleteFileText.Text = $"文件: {fileName}";
        CompleteArchText.Text = $"架构: {UpdateService.CurrentArchitecture}";

        if (isZip && _isPortableMode)
        {
            if (RuntimeHelper.IsLiteBuild)
            {
                CompleteTipText.Text = "精简版更新：下载完成后将自动打开文件夹，请关闭本程序，将压缩包解压覆盖到当前程序目录即可完成更新（工具包将自动从网络获取）";
            }
            else
            {
                CompleteTipText.Text = "便携版更新：下载完成后将自动打开文件夹，请关闭本程序，将压缩包解压覆盖到当前程序目录即可完成更新";
            }
        }
        else if (isExe)
        {
            CompleteTipText.Text = "更新已下载完成，安装程序将由下载队列自动启动";
        }
        else
        {
            CompleteTipText.Text = "更新已下载完成，下载队列将自动打开文件夹";
        }

        if (isExe)
        {
            ActionButtonText.Text = "立即安装";
            ActionButtonIcon.Glyph = "\uE896;";
        }
        else
        {
            ActionButtonText.Text = "打开文件夹";
            ActionButtonIcon.Glyph = "\uED25;";
        }
        ActionButton.Visibility = Visibility.Visible;

        StatusText.Text = "下载完成";
        StatusIcon.Glyph = "\uE73E";
    }

    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        if (_downloadItem is null) return;

        var fileName = _downloadItem.ResolvedFileName ?? "";
        var isExe = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        var tempDir = Path.Combine(Path.GetTempPath(), "TubaWinUi3_Update");
        var filePath = Path.Combine(tempDir, fileName);

        try
        {
            if (isExe && File.Exists(filePath))
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
                System.Diagnostics.Process.Start("explorer.exe", tempDir);
            }
        }
        catch { }
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
