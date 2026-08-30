using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class JunctionLinkManagerPage : Page
{
    private readonly ObservableCollection<JunctionFolderItem> _personal = [];
    private readonly ObservableCollection<CustomRow> _custom = [];
    private readonly ObservableCollection<AppDataAppItem> _appDataShown = [];
    private List<AppDataAppItem> _appDataAll = [];
    private bool _appDataExpanded;
    private readonly Button _restartButton;
    private bool _busy;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _scanCts;
    private bool _scanning => _scanCts != null;

    public JunctionLinkManagerPage()
    {
        InitializeComponent();
        FolderList.ItemsSource = _personal;
        CustomList.ItemsSource = _custom;
        AppDataList.ItemsSource = _appDataShown;
        _restartButton = new Button { Content = "重启资源管理器" };
        _restartButton.Click += RestartButton_Click;
        _ = RefreshAsync();
        _ = ScanAppDataAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _cts?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async Task RefreshAsync()
    {
        var items = await Task.Run(() => JunctionLinkManagerService.LoadItems());
        _personal.Clear();
        foreach (var item in items)
            _personal.Add(item);

        var customs = await Task.Run(() => JunctionLinkManagerService.LoadCustomJunctions());
        _custom.Clear();
        foreach (var c in customs) _custom.Add(new CustomRow { Item = c });

        await ComputeSizesAsync();
    }

    /// <summary>后台统计个人文件夹与自定义超链接的占用大小（静默，并行）。</summary>
    private async Task ComputeSizesAsync()
    {
        var items = _personal.ToArray();
        var sizeTasks = items.Select(i =>
            Task.Run(() => AppDataMigrateService.ComputeDirSize(
                Directory.Exists(i.CurrentPath) ? i.CurrentPath : i.DefaultPath, CancellationToken.None)));
        var sizes = await Task.WhenAll(sizeTasks);
        for (var i = 0; i < items.Length && i < sizes.Length; i++) items[i].Size = sizes[i];

        var rows = _custom.ToArray();
        var targetTasks = rows.Select(r =>
            Task.Run(() => AppDataMigrateService.ComputeDirSize(r.Item.Target, CancellationToken.None)));
        var targetSizes = await Task.WhenAll(targetTasks);
        for (var i = 0; i < rows.Length && i < targetSizes.Length; i++) rows[i].Size = targetSizes[i];
    }

    // ---- AppData 按应用迁移（流式扫描） ----

    /// <summary>
    /// 流式扫描：先快速枚举全部应用（每项显示"正在扫描…"），再并行逐个统计大小，
    /// 算完一个即时回填；扫描期间列表可用（可勾选、可迁移），可随时取消。
    /// </summary>
    private async Task ScanAppDataAsync()
    {
        if (_scanCts != null) return;
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        UpdateUiState();
        try
        {
            ProgressText.Text = "正在枚举 AppData…";
            var items = await Task.Run(() => AppDataMigrateService.EnumerateItems(cts.Token));
            _appDataAll = items;
            RebuildAppDataList();

            var scanProgress = new Progress<FolderMoveProgress>(p => ProgressText.Text = $"{p.Phase} ({p.Current}/{p.Total})  {p.CurrentFile}");
            await Task.Run(() => AppDataMigrateService.ComputeSizesInParallel(
                _appDataAll, null, scanProgress, cts.Token), cts.Token);
            ProgressText.Text = "AppData 统计完成。";
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "AppData 统计已取消（已统计的应用保留）。";
        }
        catch (Exception ex)
        {
            // AggregateException 只显示外层消息（"One or more errors occurred"），拆出内层真实原因
            var inner = (ex as AggregateException)?.Flatten().InnerException ?? ex;
            ShowToast($"扫描 AppData 失败：{inner.Message}", InfoBarSeverity.Warning);
            ProgressText.Text = "AppData 扫描部分完成（已枚举的应用仍可用）。";
        }
        finally
        {
            if (ReferenceEquals(_scanCts, cts)) _scanCts = null;
            cts.Dispose();
            RebuildAppDataList(); // 扫描结束：按大小排序 + 恢复"最多 5 个"显示
            UpdateUiState();
        }
    }

    private void RebuildAppDataList()
    {
        _appDataShown.Clear();
        if (_scanning)
        {
            // 扫描期间展示全部已枚举的项（大小逐个回填），不折叠
            foreach (var item in _appDataAll) _appDataShown.Add(item);
            ExpandAppDataBtn.Visibility = Visibility.Collapsed;
            return;
        }
        var sorted = _appDataAll.OrderByDescending(a => a.Size).ToList();
        var visible = _appDataExpanded ? sorted : sorted.Take(5).ToList();
        foreach (var item in visible) _appDataShown.Add(item);
        ExpandAppDataBtn.Visibility = sorted.Count > 5 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ExpandAppDataBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _scanning) return;
        _appDataExpanded = !_appDataExpanded;
        ExpandAppDataBtn.Content = _appDataExpanded ? "收起" : "展开全部";
        RebuildAppDataList();
    }

    private async void RefreshAppDataBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _scanCts?.Cancel();
        await ScanAppDataAsync();
    }

    private async void MigrateAppsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var selected = _appDataAll.Where(a => a.Selected && !a.Migrated).ToList();
        if (selected.Count == 0)
        {
            ShowToast("请先勾选要迁移的应用。", InfoBarSeverity.Warning);
            return;
        }

        var baseTarget = Win32Dialogs.PickFolder();
        if (string.IsNullOrEmpty(baseTarget)) return;
        var targetError = ValidateAppDataBaseTarget(baseTarget);
        if (targetError != null) { ShowToast(targetError, InfoBarSeverity.Warning); return; }

        var totalSize = selected.Sum(a => a.Size);
        var panel = new StackPanel { Spacing = 10, MaxWidth = 460 };
        panel.Children.Add(new TextBlock
        {
            Text = $"已选 {selected.Count} 个应用，合计 {AppDataMigrateService.FormatSize(totalSize)}：",
            FontSize = 13
        });
        panel.Children.Add(new TextBlock
        {
            Text = string.Join("\n", selected.Select(a => $"· {a.Name}（{a.Area}）"))
                + $"\n\n目标盘基础目录：{baseTarget}\n\n"
                + "迁移后原位置自动创建超链接，使用旧路径的软件不受影响。建议先退出这些软件，正在运行的程序会导致该应用迁移中止（不影响其他应用）。",
            FontSize = 12,
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap
        });
        var dlg = new ContentDialog
        {
            Title = "迁移所选应用",
            Content = panel,
            CloseButtonText = "取消",
            PrimaryButtonText = "开始迁移",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        await RunOperationAsync((p, ct) => AppDataMigrateService.MigrateSelectedAsync(selected, baseTarget, p, ct));
        RebuildAppDataList();
    }

    private static string? ValidateAppDataBaseTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "请选择目标盘基础目录。";
        if (!Path.IsPathFullyQualified(path)) return "目标必须是绝对路径。";
        if (JunctionLinkManagerService.IsDriveRoot(path)) return "请选择盘内的一个文件夹作为基础目录（不要选盘符根目录）。";
        return null;
    }

    private async void UndoAppBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppDataAppItem item } || _busy) return;
        if (!item.Migrated || string.IsNullOrEmpty(item.Target)) return;

        var dlg = new ContentDialog
        {
            Title = "撤销迁移",
            Content = new TextBlock
            {
                Text = $"将移除 {item.Source} 的超链接，并把 {item.Target} 中的文件全部搬回原位置。",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            },
            CloseButtonText = "取消",
            PrimaryButtonText = "撤销并搬回",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        var result = await RunOperationAsync(
            (p, ct) => JunctionLinkManagerService.UndoCustomJunctionAsync(
                new CustomJunctionItem { Source = item.Source, Target = item.Target }, p, ct));
        if (result?.Success == true)
        {
            var customs = JunctionLinkManagerService.LoadCustomJunctions();
            customs.RemoveAll(x => string.Equals(x.Source, item.Source, StringComparison.OrdinalIgnoreCase));
            JunctionLinkManagerService.SaveCustomJunctions(customs);
            item.Migrated = false;
            item.Selected = false;
            item.Target = "";
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_scanCts != null) _scanCts.Cancel();
        else _cts?.Cancel();
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SetBusyUi(true);
        try
        {
            ProgressText.Text = "正在重启资源管理器…";
            var ok = await JunctionLinkManagerService.RestartExplorerAsync();
            ShowToast(ok ? "资源管理器已重启，更改已生效。" : "资源管理器重启失败，请注销登录后生效。",
                ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowToast($"重启资源管理器失败：{ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            SetBusyUi(false);
            _busy = false;
        }
    }

    // ---- 自定义文件夹超链接 ----

    private async void NewJunctionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var source = Win32Dialogs.PickFolder();
        if (string.IsNullOrEmpty(source)) return;
        var error = JunctionLinkManagerService.ValidateCustomSource(source);
        if (error != null) { ShowToast(error, InfoBarSeverity.Warning); return; }

        var target = Win32Dialogs.PickFolder();
        if (string.IsNullOrEmpty(target)) return;
        error = JunctionLinkManagerService.ValidateCustomTarget(source, target);
        if (error != null) { ShowToast(error, InfoBarSeverity.Warning); return; }

        var panel = new StackPanel { Spacing = 10, MaxWidth = 460 };
        panel.Children.Add(new TextBlock { Text = $"源文件夹：{source}", FontSize = 13, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"目标位置：{target}", FontSize = 13, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = "全部文件将复制到目标位置，源文件夹原位置创建超链接；任意一步失败会整体回滚，原文件不受影响。\n\n"
                   + "如果是 AppData 相关目录，建议先退出使用的软件（正在运行的程序可能占用文件导致迁移中止）；"
                   + "迁移后请重启这些软件，它们会通过超链接正常读写新位置。",
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap
        });

        var dlg = new ContentDialog
        {
            Title = "迁移文件夹并创建超链接",
            Content = panel,
            CloseButtonText = "取消",
            PrimaryButtonText = "开始迁移",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        var result = await RunOperationAsync((p, ct) => JunctionLinkManagerService.CreateCustomJunctionAsync(source, target, p, ct));
        if (result?.Success == true)
        {
            var customs = JunctionLinkManagerService.LoadCustomJunctions();
            customs.RemoveAll(x => string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase));
            customs.Add(new CustomJunctionItem { Source = source, Target = target });
            JunctionLinkManagerService.SaveCustomJunctions(customs);
            await RefreshCustomListAsync();
        }
    }

    private async void UndoJunctionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CustomRow row } || _busy) return;

        var dlg = new ContentDialog
        {
            Title = "撤销迁移",
            Content = new TextBlock
            {
                Text = $"将移除 {row.Item.Source} 的超链接，并把 {row.Item.Target} 中的文件全部搬回原位置。",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            },
            CloseButtonText = "取消",
            PrimaryButtonText = "撤销并搬回",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        var result = await RunOperationAsync((p, ct) => JunctionLinkManagerService.UndoCustomJunctionAsync(row.Item, p, ct));
        if (result?.Success == true)
        {
            var customs = JunctionLinkManagerService.LoadCustomJunctions();
            customs.RemoveAll(x => string.Equals(x.Source, row.Item.Source, StringComparison.OrdinalIgnoreCase));
            JunctionLinkManagerService.SaveCustomJunctions(customs);
            await RefreshCustomListAsync();
        }
    }

    private async Task RefreshCustomListAsync()
    {
        var customs = await Task.Run(() => JunctionLinkManagerService.LoadCustomJunctions());
        _custom.Clear();
        foreach (var c in customs) _custom.Add(new CustomRow { Item = c });
    }

    private async void ChangeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: JunctionFolderItem item } && !_busy)
            await ChangeLocationAsync(item);
    }

    private async Task ChangeLocationAsync(JunctionFolderItem item)
    {
        var target = Win32Dialogs.PickFolder();
        if (string.IsNullOrEmpty(target)) return;

        var error = JunctionLinkManagerService.ValidateTarget(item, target);
        if (error != null)
        {
            ShowToast(error, InfoBarSeverity.Warning);
            return;
        }

        var migrateYes = new RadioButton { Content = "迁移原文件到新位置（推荐）", IsChecked = true, FontSize = 13 };
        var migrateNo = new RadioButton { Content = "不迁移：原文件保留在原盘的 [旧文件夹名].old 中", FontSize = 13 };
        var radioGroup = new StackPanel { Spacing = 4 };
        radioGroup.Children.Add(migrateYes);
        radioGroup.Children.Add(migrateNo);

        var panel = new StackPanel { Spacing = 10, MaxWidth = 460 };
        panel.Children.Add(new TextBlock
        {
            Text = $"目标位置：{target}",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"当前位置：{item.CurrentPath}",
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "原文件处理",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0)
        });
        panel.Children.Add(radioGroup);
        panel.Children.Add(new TextBlock
        {
            Text = "迁移完成后，系统已知文件夹与原位置（超链接）都会指向新位置，按旧路径访问的软件仍可正常使用。",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        });
        var restartCheck = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "完成后立即重启资源管理器（资源管理器缓存了旧路径，重启后才会显示新位置；会关闭已打开的文件夹窗口）",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            },
            IsChecked = true,
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        panel.Children.Add(restartCheck);

        var dlg = new ContentDialog
        {
            Title = $"重定向「{item.Name}」",
            Content = panel,
            CloseButtonText = "取消",
            PrimaryButtonText = "开始重定向",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        var migrate = migrateYes.IsChecked == true;
        await RunOperationAsync(
            (p, ct) => JunctionLinkManagerService.RedirectAsync(item, target, migrate, p, ct),
            restartCheck.IsChecked == true);
    }

    private async void RestoreBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: JunctionFolderItem item } || _busy) return;

        var restartCheck = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "完成后立即重启资源管理器（会关闭已打开的文件夹窗口）",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            },
            IsChecked = true,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = $"将移除原位置的超链接，把文件搬回默认位置 {item.DefaultPath}，并恢复系统的默认设置。当前实际位置：{item.CurrentPath}",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440
        });
        panel.Children.Add(restartCheck);

        var dlg = new ContentDialog
        {
            Title = $"还原「{item.Name}」",
            Content = panel,
            CloseButtonText = "取消",
            PrimaryButtonText = "开始还原",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        await RunOperationAsync(
            (p, ct) => JunctionLinkManagerService.RestoreAsync(item, p, ct),
            restartCheck.IsChecked == true);
    }

    private async Task<FolderMoveResult?> RunOperationAsync(
        Func<IProgress<FolderMoveProgress>, CancellationToken, Task<FolderMoveResult>> operation,
        bool restartExplorer = false)
    {
        if (_busy) return null;
        _busy = true;
        _cts = new CancellationTokenSource();
        SetBusyUi(true);

        var progress = new Progress<FolderMoveProgress>(p =>
        {
            var text = p.Total > 0 ? $"{p.Phase} ({p.Current}/{p.Total})" : p.Phase;
            if (!string.IsNullOrEmpty(p.CurrentFile)) text += $"  {p.CurrentFile}";
            if (p.Skipped > 0) text += $"（已跳过 {p.Skipped}）";
            ProgressText.Text = text;
        });

        try
        {
            var result = await Task.Run(() => operation(progress, _cts!.Token));

            var restarted = false;
            var restartFailed = false;
            if (result.Success && restartExplorer)
            {
                ProgressText.Text = "正在重启资源管理器…";
                restarted = await JunctionLinkManagerService.RestartExplorerAsync();
                restartFailed = !restarted;
            }

            if (result.Success)
            {
                var msg = result.Message;
                if (restarted) msg += "\n已重启资源管理器，更改立即生效。";
                else if (restartFailed) msg += "\n资源管理器重启失败，请注销或在任务管理器中重启资源管理器。";
                else msg += "\n提示：资源管理器缓存了旧路径，重启资源管理器（或注销登录）后才会显示新位置。";
                ShowToast(msg, InfoBarSeverity.Success, showRestartAction: !restarted);
            }
            else
            {
                ShowToast(result.Message, InfoBarSeverity.Error);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            ShowToast("操作已取消。", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ShowToast($"操作失败：{ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            SetBusyUi(false);
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            await RefreshAsync();
        }
        return null;
    }

    private void SetBusyUi(bool busy)
    {
        UpdateUiState();
    }

    /// <summary>统一 UI 状态：进度条可见性 = 操作中或扫描中；扫描期间列表保持可交互（可先勾选/迁移）。</summary>
    private void UpdateUiState()
    {
        ProgressPanel.Visibility = (_busy || _scanning) ? Visibility.Visible : Visibility.Collapsed;
        FolderList.IsEnabled = !_busy;
        AppDataList.IsEnabled = !_busy;
        CustomList.IsEnabled = !_busy;
        NewJunctionBtn.IsEnabled = !_busy;
        MigrateAppsBtn.IsEnabled = !_busy;
        RefreshAppDataBtn.IsEnabled = !_busy;
    }

    private void ShowToast(string message, InfoBarSeverity severity, bool showRestartAction = false)
    {
        ToastBar.Severity = severity;
        ToastBar.Message = message;
        ToastBar.ActionButton = showRestartAction ? _restartButton : null;
        ToastBar.IsOpen = true;
    }
}

/// <summary>自定义超链接列表行（绑定用包装）。</summary>
public sealed class CustomRow : INotifyPropertyChanged
{
    public CustomJunctionItem Item { get; init; } = new();
    public string SourceDisplay => Path.GetFileName(Item.Source.TrimEnd('\\'));
    public string TargetDisplay => Path.GetFileName(Item.Target.TrimEnd('\\'));
    public string Source => Item.Source;
    public string Target => Item.Target;

    private long _size;
    /// <summary>目标位置（实际文件）占用大小。</summary>
    public long Size
    {
        get => _size;
        set
        {
            if (_size != value)
            {
                _size = value;
                PropertyChanged?.Invoke(this, new(nameof(Size)));
                PropertyChanged?.Invoke(this, new(nameof(SizeText)));
            }
        }
    }

    public string SizeText => AppDataMigrateService.FormatSize(Size);

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>重定向状态 → 徽章文字。</summary>
public sealed class StateToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        JunctionFolderState.RedirectedWithLink => "已重定向 · 有超链接",
        JunctionFolderState.RedirectedNoLink => "已重定向 · 无超链接",
        _ => "未重定向"
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>重定向状态 → 徽章颜色。</summary>
public sealed class StateToBrushConverter : IValueConverter
{
    private static readonly Brush Gray = new SolidColorBrush(Color.FromArgb(255, 100, 116, 139));
    private static readonly Brush Green = new SolidColorBrush(Color.FromArgb(255, 22, 163, 74));
    private static readonly Brush Orange = new SolidColorBrush(Color.FromArgb(255, 217, 119, 6));

    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        JunctionFolderState.RedirectedWithLink => Green,
        JunctionFolderState.RedirectedNoLink => Orange,
        _ => Gray
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>迁移状态 → 徽章颜色（未迁移灰 / 已迁移绿）。</summary>
public sealed class MigratedBrushConverter : IValueConverter
{
    private static readonly Brush Gray = new SolidColorBrush(Color.FromArgb(255, 100, 116, 139));
    private static readonly Brush Green = new SolidColorBrush(Color.FromArgb(255, 22, 163, 74));

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Green : Gray;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}