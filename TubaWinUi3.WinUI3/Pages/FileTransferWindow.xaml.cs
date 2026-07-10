using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class FileTransferWindow : Window
{
    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);
    private static readonly Color AccentOrange = Color.FromArgb(255, 251, 146, 60);

    public FileTransferWindow()
    {
        InitializeComponent();

        AppWindow.Title = "文件传输助手";
        AppWindow.Resize(new SizeInt32(960, 680));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ApplyTitleBarTheme();

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        FileTransferOrchestrator.Initialize();

        FileTransferOrchestrator.GroupJoined += OnGroupJoined;
        FileTransferOrchestrator.GroupLeft += OnGroupLeft;
        FileTransferOrchestrator.DeviceJoined += OnDeviceJoined;
        FileTransferOrchestrator.DeviceLeft += OnDeviceLeft;
        FileTransferOrchestrator.TransferStarted += OnTransferStarted;
        FileTransferOrchestrator.TransferProgressChanged += OnTransferProgressChanged;
        FileTransferOrchestrator.TransferCompleted += OnTransferCompleted;
        FileTransferOrchestrator.TransferFailed += OnTransferFailed;
        FileTransferOrchestrator.FileOfferReceived += OnFileOfferReceived;
        FileTransferOrchestrator.Error += OnError;

        LoadSignalingUrl();
    }

    private void LoadSignalingUrl()
    {
        var saved = AppSettings.Get("FileTransfer_SignalingUrl");
        if (!string.IsNullOrEmpty(saved))
        {
            SignalingUrlInput.Text = saved;
            FileTransferOrchestrator.SetSignalingUrl(saved);
        }
    }


    private async void CreateGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var groupName = GroupNameInput.Text.Trim();
        if (string.IsNullOrEmpty(groupName)) groupName = "我的传输群组";

        CreateGroupButton.IsEnabled = false;
        try
        {
            await FileTransferOrchestrator.CreateGroupAsync(groupName, GroupPasswordInput.Password);
        }
        finally
        {
            CreateGroupButton.IsEnabled = true;
        }
    }

    private async void JoinGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var code = JoinCodeInput.Text.Trim();
        if (code.Length != 6)
        {
            ShowToast("请输入6位群组码", InfoBarSeverity.Warning);
            return;
        }

        JoinGroupButton.IsEnabled = false;
        try
        {
            await FileTransferOrchestrator.JoinGroupAsync(code, JoinPasswordInput.Password);
        }
        finally
        {
            JoinGroupButton.IsEnabled = true;
        }
    }

    private async void LeaveGroupButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveGroupButton.IsEnabled = false;
        try
        {
            await FileTransferOrchestrator.LeaveGroupAsync();
        }
        finally
        {
            LeaveGroupButton.IsEnabled = true;
        }
    }

    private void CopyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var code = FileTransferOrchestrator.CurrentGroup?.GroupId;
        if (code is null) return;

        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(code);
        dp.Properties.Title = "群组码";
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        ShowToast("群组码已复制到剪贴板", InfoBarSeverity.Success);
    }

    private void SaveSignalingUrlButton_Click(object sender, RoutedEventArgs e)
    {
        var url = SignalingUrlInput.Text.Trim();
        if (!string.IsNullOrEmpty(url))
        {
            AppSettings.Set("FileTransfer_SignalingUrl", url);
            FileTransferOrchestrator.SetSignalingUrl(url);
            ShowToast("信令服务器地址已保存", InfoBarSeverity.Success);
        }
    }

    private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
    {
        var group = FileTransferOrchestrator.CurrentGroup;
        if (group is null)
        {
            ShowToast("请先加入群组", InfoBarSeverity.Warning);
            return;
        }

        var onlineDevices = group.Devices.Where(d => d.DeviceId != FileTransferOrchestrator.DeviceId && d.IsOnline).ToList();
        if (onlineDevices.Count == 0)
        {
            ShowToast("群组内没有其他在线设备", InfoBarSeverity.Warning);
            return;
        }

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var targetDevice = await PickTargetDevice(onlineDevices);
        if (targetDevice is null) return;

        await FileTransferOrchestrator.SendFileAsync(file.Path, targetDevice.DeviceId);
    }

    private async void SendToAllButton_Click(object sender, RoutedEventArgs e)
    {
        var group = FileTransferOrchestrator.CurrentGroup;
        if (group is null) return;

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        await FileTransferOrchestrator.SendFileToAllAsync(file.Path);
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        DropZone.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(255, 96, 165, 250));
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone.ClearValue(Border.BorderBrushProperty);

        var group = FileTransferOrchestrator.CurrentGroup;
        if (group is null)
        {
            ShowToast("请先加入群组", InfoBarSeverity.Warning);
            return;
        }

        var onlineDevices = group.Devices.Where(d => d.DeviceId != FileTransferOrchestrator.DeviceId && d.IsOnline).ToList();
        if (onlineDevices.Count == 0)
        {
            ShowToast("群组内没有其他在线设备", InfoBarSeverity.Warning);
            return;
        }

        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (item is Windows.Storage.StorageFile file)
                {
                    var targetDevice = await PickTargetDevice(onlineDevices);
                    if (targetDevice is not null)
                    {
                        await FileTransferOrchestrator.SendFileAsync(file.Path, targetDevice.DeviceId);
                    }
                }
            }
        }
    }

    private async Task<GroupDevice?> PickTargetDevice(List<GroupDevice> devices)
    {
        if (devices.Count == 1) return devices[0];

        var dialog = new ContentDialog
        {
            Title = "选择目标设备",
            CloseButtonText = "取消",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single };
        foreach (var d in devices)
        {
            list.Items.Add($"{d.DeviceName} ({d.ConnectionTypeLabel})");
        }
        list.SelectedIndex = 0;
        dialog.Content = list;

        dialog.PrimaryButtonText = "发送";
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && list.SelectedIndex >= 0)
        {
            return devices[list.SelectedIndex];
        }
        return null;
    }

    private void OnGroupJoined(TransferGroup group)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            GroupInfoCard.Visibility = Visibility.Visible;
            NoGroupCard.Visibility = Visibility.Collapsed;
            DeviceListCard.Visibility = Visibility.Visible;
            SendToAllButton.IsEnabled = true;

            GroupCodeText.Text = group.GroupId;
            GroupNameText.Text = group.GroupName;
            StatusText.Text = $"已加入群组 {group.GroupId}";

            RefreshDeviceList();
            ShowToast($"已加入群组 {group.GroupId}", InfoBarSeverity.Success);
        });
    }

    private void OnGroupLeft()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            GroupInfoCard.Visibility = Visibility.Collapsed;
            NoGroupCard.Visibility = Visibility.Visible;
            DeviceListCard.Visibility = Visibility.Collapsed;
            SendToAllButton.IsEnabled = false;

            GroupCodeText.Text = "";
            GroupNameText.Text = "";
            StatusText.Text = "未加入群组";

            TransferListContainer.Children.Clear();
            EmptyTransferPanel.Visibility = TransferListContainer.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void OnDeviceJoined(GroupDevice device)
    {
        DispatcherQueue.TryEnqueue(RefreshDeviceList);
    }

    private void OnDeviceLeft(string deviceId)
    {
        DispatcherQueue.TryEnqueue(RefreshDeviceList);
    }

    private void RefreshDeviceList()
    {
        var group = FileTransferOrchestrator.CurrentGroup;
        if (group is null) return;

        var onlineDevices = group.Devices.Where(d => d.IsOnline).ToList();
        DeviceListView.ItemsSource = onlineDevices;
        DeviceCountText.Text = $"{onlineDevices.Count} 台设备";
    }

    private void OnTransferStarted(FileTransferTask task)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            EmptyTransferPanel.Visibility = Visibility.Collapsed;
            AddTransferItem(task);
        });
    }

    private void OnTransferProgressChanged(FileTransferTask task)
    {
        DispatcherQueue.TryEnqueue(() => UpdateTransferItem(task));
    }

    private void OnTransferCompleted(FileTransferTask task)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateTransferItem(task);
            ShowToast($"{task.FileName} 传输完成", InfoBarSeverity.Success);
        });
    }

    private void OnTransferFailed(FileTransferTask task)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateTransferItem(task);
            ShowToast($"{task.FileName} 传输失败: {task.ErrorMessage}", InfoBarSeverity.Error);
        });
    }

    private async Task<bool> OnFileOfferReceived(FileTransferTask task)
    {
        var tcs = new TaskCompletionSource<bool>();

        DispatcherQueue.TryEnqueue(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = "收到文件",
                PrimaryButtonText = "接收",
                CloseButtonText = "拒绝",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };

            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = $"来自: {task.FromDeviceName}" });
            content.Children.Add(new TextBlock { Text = $"文件: {task.FileName}" });
            content.Children.Add(new TextBlock { Text = $"大小: {FileChunkService.FormatFileSize(task.FileSize)}" });
            content.Children.Add(new TextBlock { Text = $"连接: {task.ConnectionTypeLabel}" });
            dialog.Content = content;

            var result = await dialog.ShowAsync();
            tcs.SetResult(result == ContentDialogResult.Primary);
        });

        return await tcs.Task;
    }

    private void OnError(string msg)
    {
        DispatcherQueue.TryEnqueue(() => ShowToast(msg, InfoBarSeverity.Error));
    }

    private void AddTransferItem(FileTransferTask task)
    {
        EmptyTransferPanel.Visibility = Visibility.Collapsed;

        var border = new Border
        {
            Tag = task.FileId,
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dirIcon = new FontIcon
        {
            Glyph = task.Direction == TransferDirection.Sending ? "\uE898" : "\uE896",
            FontSize = 14,
            Opacity = 0.7
        };
        Grid.SetColumn(dirIcon, 0);
        grid.Children.Add(dirIcon);

        var nameText = new TextBlock
        {
            Text = task.FileName,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(nameText, 1);
        grid.Children.Add(nameText);

        var sizeText = new TextBlock
        {
            Text = task.FileSizeText,
            FontSize =11,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(sizeText, 2);
        grid.Children.Add(sizeText);

        var progressBar = new ProgressBar
        {
            Value = task.Progress,
            Width = 90,
            VerticalAlignment = VerticalAlignment.Center,
            ShowError = task.Status == TransferStatus.Failed,
            ShowPaused = task.Status == TransferStatus.Paused
        };
        Grid.SetColumn(progressBar, 3);
        grid.Children.Add(progressBar);

        var speedText = new TextBlock
        {
            Text = task.SpeedText,
            FontSize = 11,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(speedText, 4);
        grid.Children.Add(speedText);

        var connText = new TextBlock
        {
            Text = task.ConnectionTypeLabel,
            FontSize = 11,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(connText, 5);
        grid.Children.Add(connText);

        var cancelButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
            Tag = task.FileId,
            Padding = new Thickness(4),
            MinWidth = 0,
            MinHeight = 0
        };
        cancelButton.Click += (s, e) =>
        {
            var fileId = (string)((Button)s).Tag;
            FileTransferOrchestrator.CancelTransfer(fileId);
        };
        Grid.SetColumn(cancelButton, 6);
        grid.Children.Add(cancelButton);

        border.Child = grid;
        TransferListContainer.Children.Add(border);
    }

    private void UpdateTransferItem(FileTransferTask task)
    {
        var border = TransferListContainer.Children.OfType<Border>()
            .FirstOrDefault(b => b.Tag as string == task.FileId);
        if (border?.Child is not Grid grid) return;

        var progressBar = grid.Children.OfType<ProgressBar>().FirstOrDefault();
        if (progressBar is not null)
        {
            progressBar.Value = task.Progress;
            progressBar.ShowError = task.Status == TransferStatus.Failed;
            progressBar.ShowPaused = task.Status == TransferStatus.Paused;
        }

        var speedTexts = grid.Children.OfType<TextBlock>().ToList();
        foreach (var t in speedTexts)
        {
            if (t.Text.EndsWith("MB/s") || t.Text.EndsWith("KB/s") || t.Text.EndsWith("GB/s") || string.IsNullOrEmpty(t.Text))
            {
                if (Grid.GetColumn(t) == 4)
                    t.Text = task.SpeedText;
            }
        }
    }

    private void ShowToast(string message, InfoBarSeverity severity)
    {
        ToastBar.Title = message;
        ToastBar.Severity = severity;
        ToastBar.IsOpen = true;
    }

    private void ApplyTitleBarTheme()
    {
        var tb = AppWindow.TitleBar;
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
}
