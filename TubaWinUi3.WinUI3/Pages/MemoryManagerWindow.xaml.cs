using System.Diagnostics;
using System.IO;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.Graphics;

namespace TubaWinUi3.Pages;

public sealed partial class MemoryManagerWindow : Window
{
    private static readonly string[] CleanTypeNames = ["全部清理", "清理待机内存", "收紧系统工作集"];

    private const string KeyEnabled = "MemoryManager.ScheduleEnabled";
    private const string KeyInterval = "MemoryManager.IntervalMinutes";
    private const string KeyThreshold = "MemoryManager.ThresholdGb";
    private const string KeyCleanType = "MemoryManager.CleanType";

    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly List<PageFileRowUi> _pageFileRows = [];

    private bool _loadingUi;
    private bool _refreshing;
    private bool _cleaning;
    private DateTime _lastScheduledClean = DateTime.MinValue;
    private DateTime _lastPageFileFetch = DateTime.MinValue;

    private sealed class PageFileRowUi
    {
        public required PageFileEntry Entry { get; init; }
        public required ComboBox TypeCombo { get; init; }
        public required NumberBox InitialBox { get; init; }
        public required NumberBox MaxBox { get; init; }
    }

    private sealed class ProcRowUi
    {
        public required TextBlock NameText { get; init; }
        public required TextBlock PidText { get; init; }
        public required ProgressBar Bar { get; init; }
        public required TextBlock PctText { get; init; }
        public required TextBlock MemText { get; init; }
        public required TextBlock PrivateText { get; init; }
    }

    public MemoryManagerWindow()
    {
        InitializeComponent();

        AppWindow.Title = "内存管理";
        AppWindow.Resize(new SizeInt32(1220, 860));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        CleanTypeCombo.Items.Clear();
        foreach (var name in CleanTypeNames)
            CleanTypeCombo.Items.Add(name);

        _loadingUi = true;
        ScheduleToggle.IsOn = AppSettings.GetBool(KeyEnabled, false);
        IntervalBox.Value = AppSettings.GetInt(KeyInterval, 5);
        ThresholdBox.Value = AppSettings.GetInt(KeyThreshold, 12);
        CleanTypeCombo.SelectedIndex = AppSettings.GetInt(KeyCleanType, 0);
        _loadingUi = false;

        UpdateSchedulePanelVisibility();
        LoadPageFileUi();

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastBar.IsOpen = false;
        };

        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _toastTimer.Stop();
        };

        _refreshTimer.Tick += async (_, _) => await RefreshAllAsync();
        _refreshTimer.Start();
        _ = RefreshAllAsync();
    }

    // ---------- 定时刷新统计与进程 ----------

    private async Task RefreshAllAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var (stats, procs) = await Task.Run(() => MemoryManagerService.GetSnapshot(10));
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateStatsUi(stats);
                UpdateProcessList(procs, stats.PhysicalTotal);
                CheckScheduledClean(stats);
            });
            await UpdatePageFileUsageAsync();
        }
        catch { }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateStatsUi(MemoryStats stats)
    {
        PhysUsedText.Text = $"已用 {MemoryManagerService.FormatBytes(stats.PhysicalUsed)}";
        PhysAvailText.Text = $"可用 {MemoryManagerService.FormatBytes(stats.PhysicalAvailable)}";
        PhysTotalText.Text = $"总共 {MemoryManagerService.FormatBytes(stats.PhysicalTotal)}";
        SetBar(PhysBar, PhysPercentText, stats.PhysicalUsed, stats.PhysicalTotal);

        VirtualUsedText.Text = $"已用 {MemoryManagerService.FormatBytes(stats.VirtualUsed)}";
        VirtualAvailText.Text = $"可用 {MemoryManagerService.FormatBytes(stats.VirtualAvailable)}";
        VirtualTotalText.Text = $"总共 {MemoryManagerService.FormatBytes(stats.VirtualTotal)}";
        SetBar(VirtualBar, VirtualPercentText, stats.VirtualUsed, stats.VirtualTotal);

        WsUsedText.Text = $"已用 {MemoryManagerService.FormatBytes(stats.WorkingSetUsed)}";
        WsAvailText.Text = $"可用 {MemoryManagerService.FormatBytes(stats.WorkingSetAvailable)}";
        WsTotalText.Text = $"总共 {MemoryManagerService.FormatBytes(stats.WorkingSetTotal)}";
        SetBar(WsBar, WsPercentText, stats.WorkingSetUsed, stats.WorkingSetTotal);
    }

    private static void SetBar(ProgressBar bar, TextBlock percentText, long used, long total)
    {
        if (total <= 0) return;
        var pct = (double)used / total * 100;
        bar.Value = Math.Clamp(pct, 0, 100);
        percentText.Text = $"已使用 {pct:F1}%";
    }

    private void UpdateProcessList(List<ProcessMemoryInfo> procs, long totalBytes)
    {
        ProcCountText.Text = $"按工作集排序 · 共 {procs.Count} 个进程";

        var n = procs.Count;
        for (var i = 0; i < n; i++)
        {
            ListViewItem item;
            if (i < ProcList.Items.Count)
            {
                item = (ListViewItem)ProcList.Items[i];
            }
            else
            {
                item = CreateProcItem(procs[i], totalBytes);
                ProcList.Items.Add(item);
            }
            UpdateProcItem(item, procs[i], totalBytes);
        }

        while (ProcList.Items.Count > n)
            ProcList.Items.RemoveAt(ProcList.Items.Count - 1);
    }

    private ListViewItem CreateProcItem(ProcessMemoryInfo p, long totalBytes)
    {
        var grid = new Grid { Padding = new Thickness(0, 6, 0, 6), ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        var avatar = new Border
        {
            Width = 30,
            Height = 30,
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = p.Name.Length > 0 ? p.Name[0].ToString().ToUpperInvariant() : "?",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var nameText = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var pidText = new TextBlock
        {
            FontSize = 10.5,
            Opacity = 0.55
        };
        var nameStack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(nameText);
        nameStack.Children.Add(pidText);

        var bar = new ProgressBar { Maximum = 100, Height = 5 };
        var pctText = new TextBlock { FontSize = 10.5, Opacity = 0.55 };
        var barStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        barStack.Children.Add(bar);
        barStack.Children.Add(pctText);

        var memText = new TextBlock
        {
            FontSize = 12.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var privateText = new TextBlock
        {
            FontSize = 10.5,
            Opacity = 0.55,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var memStack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        memStack.Children.Add(memText);
        memStack.Children.Add(privateText);

        grid.Children.Add(avatar);
        Grid.SetColumn(avatar, 0);
        grid.Children.Add(nameStack);
        Grid.SetColumn(nameStack, 1);
        grid.Children.Add(barStack);
        Grid.SetColumn(barStack, 2);
        grid.Children.Add(memStack);
        Grid.SetColumn(memStack, 3);

        var item = new ListViewItem
        {
            Content = grid,
            Padding = new Thickness(0, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = new ProcRowUi
            {
                NameText = nameText,
                PidText = pidText,
                Bar = bar,
                PctText = pctText,
                MemText = memText,
                PrivateText = privateText
            }
        };
        UpdateProcItem(item, p, totalBytes);
        return item;
    }

    private static void UpdateProcItem(ListViewItem item, ProcessMemoryInfo p, long totalBytes)
    {
        if (item.Tag is not ProcRowUi ui) return;
        ui.NameText.Text = p.Name;
        ui.PidText.Text = $"PID {p.Pid}";
        var pct = totalBytes > 0 ? (double)p.WorkingSet / totalBytes * 100 : 0;
        ui.Bar.Value = Math.Clamp(pct, 0, 100);
        ui.PctText.Text = $"{pct:F1}%";
        ui.MemText.Text = MemoryManagerService.FormatBytes(p.WorkingSet);
        ui.PrivateText.Text = $"私有 {MemoryManagerService.FormatBytes(p.PrivateMemory)}";
    }

    // ---------- 分页文件磁盘占用 (每 10 秒查询一次 WMI) ----------

    private async Task UpdatePageFileUsageAsync()
    {
        if ((DateTime.Now - _lastPageFileFetch).TotalSeconds < 10) return;
        _lastPageFileFetch = DateTime.Now;

        var usage = await Task.Run(MemoryManagerService.GetPageFileUsage);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (usage.Count == 0)
            {
                VirtualPageFileText.Text = "分页文件: 未启用或无数据";
                return;
            }
            var curMB = usage.Sum(u => u.CurrentUsageMB);
            var allocMB = usage.Sum(u => u.AllocatedMB);
            VirtualPageFileText.Text =
                $"分页文件: {MemoryManagerService.FormatBytes(curMB * 1024L * 1024L)} / 分配 {MemoryManagerService.FormatBytes(allocMB * 1024L * 1024L)}";
        });
    }

    // ---------- 手动清理 ----------

    private void CleanAllButton_Click(object sender, RoutedEventArgs e) => RunClean(0, "全部清理");

    private void CleanStandbyButton_Click(object sender, RoutedEventArgs e) => RunClean(1, "清理待机内存");

    private void TrimWorkingSetButton_Click(object sender, RoutedEventArgs e) => RunClean(2, "收紧系统工作集");

    private void RunClean(int type, string label)
    {
        if (_cleaning) return;
        _cleaning = true;
        ShowToast($"正在执行{label}...", InfoBarSeverity.Informational);

        Task.Run(() =>
        {
            switch (type)
            {
                case 1: MemoryManagerService.CleanStandbyMemory(); break;
                case 2: MemoryManagerService.TrimWorkingSets(); break;
                default: MemoryManagerService.CleanAll(); break;
            }
        }).ContinueWith(t =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _cleaning = false;
                _lastScheduledClean = DateTime.Now;
                LastCleanText.Text = $"上次清理: {DateTime.Now:HH:mm:ss}";
                ShowToast($"{label}完成。", InfoBarSeverity.Success);
            });
        });
    }

    // ---------- 定时清理 ----------

    private void ScheduleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateSchedulePanelVisibility();
        if (!_loadingUi)
            AppSettings.Set(KeyEnabled, ScheduleToggle.IsOn);
    }

    private void UpdateSchedulePanelVisibility()
    {
        SchedulePanel.Visibility = ScheduleToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void IntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loadingUi && sender.Value is double v)
            AppSettings.Set(KeyInterval, (int)Math.Clamp(Math.Round(v), 1, 1440));
    }

    private void ThresholdBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loadingUi && sender.Value is double v)
            AppSettings.Set(KeyThreshold, (int)Math.Clamp(Math.Round(v), 1, 524288));
    }

    private void CleanTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingUi && CleanTypeCombo.SelectedIndex >= 0)
            AppSettings.Set(KeyCleanType, CleanTypeCombo.SelectedIndex);
    }

    private void CheckScheduledClean(MemoryStats stats)
    {
        if (!ScheduleToggle.IsOn || _cleaning) return;

        var intervalMinutes = Math.Max(1, (int)Math.Round(IntervalBox.Value));
        if ((DateTime.Now - _lastScheduledClean).TotalMinutes < intervalMinutes) return;

        var thresholdGb = ThresholdBox.Value;
        if (MemoryManagerService.BytesToGb(stats.PhysicalUsed) < thresholdGb) return;

        _lastScheduledClean = DateTime.Now;
        var typeIndex = CleanTypeCombo.SelectedIndex >= 0 ? CleanTypeCombo.SelectedIndex : 0;
        RunClean(typeIndex, CleanTypeNames[typeIndex]);
    }

    // ---------- 虚拟内存 (分页文件) ----------

    private void LoadPageFileUi()
    {
        _loadingUi = true;
        try
        {
            PageFilePanel.Children.Clear();
            _pageFileRows.Clear();

            var auto = MemoryManagerService.IsAutomaticPageFile();
            AutoManageToggle.IsOn = auto;

            var entriesByDrive = MemoryManagerService.GetPageFileEntries()
                .Where(e => !string.IsNullOrWhiteSpace(e.DriveLetter))
                .GroupBy(e => e.DriveLetter, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                var letter = drive.Name.TrimEnd('\\');
                var key = letter.Length > 0 ? letter[0].ToString() : "";
                var entry = entriesByDrive.TryGetValue(key, out var existing)
                    ? new PageFileEntry
                    {
                        FilePath = existing.FilePath,
                        SystemManaged = existing.SystemManaged,
                        Disabled = existing.Disabled,
                        InitialMB = existing.InitialMB,
                        MaximumMB = existing.MaximumMB
                    }
                    : new PageFileEntry { FilePath = letter + "\\pagefile.sys", Disabled = true };

                _pageFileRows.Add(CreatePageFileRow(entry));
            }

            if (!MemoryManagerService.IsElevated)
                AdminHintText.Text = "未以管理员身份运行，分页文件设置可能无法保存。";
        }
        finally
        {
            UpdatePageFileRowStates();
            _loadingUi = false;
        }
    }

    private PageFileRowUi CreatePageFileRow(PageFileEntry entry)
    {
        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var driveStack = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        driveStack.Children.Add(new TextBlock
        {
            Text = entry.DriveLetter + ":",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        driveStack.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(entry.FilePath) ?? "pagefile.sys",
            FontSize = 10.5,
            Opacity = 0.55
        });

        var typeCombo = new ComboBox { FontSize = 12 };
        typeCombo.Items.Add("系统管理的大小");
        typeCombo.Items.Add("无分页文件");
        typeCombo.Items.Add("自定义大小");
        typeCombo.SelectedIndex = entry.SystemManaged ? 0 : entry.Disabled ? 1 : 2;

        var initialBox = new NumberBox
        {
            Minimum = 0,
            Maximum = 524288,
            SmallChange = 256,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            FontSize = 12,
            Value = entry.InitialMB > 0 ? entry.InitialMB : 1024
        };
        var maxBox = new NumberBox
        {
            Minimum = 0,
            Maximum = 524288,
            SmallChange = 256,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            FontSize = 12,
            Value = entry.MaximumMB > 0 ? entry.MaximumMB : 2048
        };

        var row = new PageFileRowUi
        {
            Entry = entry,
            TypeCombo = typeCombo,
            InitialBox = initialBox,
            MaxBox = maxBox
        };

        typeCombo.SelectionChanged += (_, _) => UpdateRowState(row);

        grid.Children.Add(driveStack);
        Grid.SetColumn(driveStack, 0);
        grid.Children.Add(typeCombo);
        Grid.SetColumn(typeCombo, 1);
        grid.Children.Add(initialBox);
        Grid.SetColumn(initialBox, 2);
        grid.Children.Add(maxBox);
        Grid.SetColumn(maxBox, 3);

        PageFilePanel.Children.Add(grid);
        return row;
    }

    private void UpdatePageFileRowStates()
    {
        foreach (var row in _pageFileRows)
            UpdateRowState(row);
    }

    private void UpdateRowState(PageFileRowUi row)
    {
        var custom = row.TypeCombo.SelectedIndex == 2;
        var enabled = custom && !AutoManageToggle.IsOn;
        row.InitialBox.IsEnabled = enabled;
        row.MaxBox.IsEnabled = enabled;

        if (custom && (row.InitialBox.Value) <= 0)
            row.InitialBox.Value = row.Entry.InitialMB > 0 ? row.Entry.InitialMB : 1024;
        if (custom && (row.MaxBox.Value) <= 0)
            row.MaxBox.Value = row.Entry.MaximumMB > 0 ? row.Entry.MaximumMB : 2048;
    }

    private void AutoManageToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdatePageFileRowStates();
    }

    private void SavePageFileButton_Click(object sender, RoutedEventArgs e)
    {
        var auto = AutoManageToggle.IsOn;
        var entries = new List<PageFileEntry>();
        foreach (var row in _pageFileRows)
        {
            var entry = row.Entry;
            entry.SystemManaged = row.TypeCombo.SelectedIndex == 0;
            entry.Disabled = row.TypeCombo.SelectedIndex == 1;
            if (entry.SystemManaged || entry.Disabled)
            {
                entry.InitialMB = 0;
                entry.MaximumMB = 0;
            }
            else
            {
                var min = (long)Math.Round(row.InitialBox.Value);
                var max = (long)Math.Round(row.MaxBox.Value);
                entry.InitialMB = Math.Clamp(min, 0, 524288);
                entry.MaximumMB = Math.Clamp(max, entry.InitialMB, 524288);
            }
            entries.Add(entry);
        }

        var ok = MemoryManagerService.ApplyPageFileConfig(auto, entries);
        PageFileStatusText.Text = ok
            ? "已保存，重启电脑后生效。"
            : "保存失败，请以管理员身份运行后重试。";
        ShowToast(ok
            ? "虚拟内存设置已应用，重启电脑后生效。"
            : "保存失败，请以管理员身份运行后重试。",
            ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private void ReloadPageFileButton_Click(object sender, RoutedEventArgs e)
    {
        LoadPageFileUi();
        ShowToast("已重新读取分页文件设置。", InfoBarSeverity.Informational);
    }

    private void OpenSystemSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        MemoryManagerService.OpenSystemVirtualMemorySettings();
    }

    // ---------- 内存知识帮助 ----------

    private async void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(0, 0, 8, 0) };

        panel.Children.Add(BuildHintCard());
        panel.Children.Add(BuildHelpCard(
            "物理内存（内存条）", "\uE963", ThemeColors.AccentBlue,
            ("是什么", "插在主板上的一根硬件，程序运行时它的代码和数据就放在这里，读写极快。"),
            ("有什么用", "它相当于你的\u201C办公桌\u201D——越大，能同时摊开的程序越多，多开不卡。"),
            ("意味着什么", "已用很高 + 可用很低 → 内存真紧张，可能要卡；可用还充裕（好几 GB）→ 内存很够用。")));
        panel.Children.Add(BuildHelpCard(
            "虚拟内存（提交限制）", "\uE8A8", ThemeColors.AccentGreen,
            ("是什么？", "Windows 允许所有程序\u201C记账\u201D的总量 = 物理内存 + 硬盘上的分页文件（pagefile.sys）。"),
            ("有什么用", "内存条不够时，把暂不用的数据挪到硬盘分页文件，给程序兜底，避免因为内存不足崩溃。"),
            ("意味着什么", "占用率高基本是正常的——大头都是物理内存在扛。真正要警惕的是：物理内存可用很低，且下方“分页文件”磁盘占用很高。")));
        panel.Children.Add(BuildHelpCard(
            "系统工作集（Working Set）", "\uE90F", ThemeColors.AccentPurple,
            ("是什么？", "所有进程当前正攥在物理内存里的数据总和（真正在用的那部分）。"),
            ("有什么用", "判断\u201C进程到底花了多少内存条\u201D，下方排行榜就是按它排序的。"),
            ("意味着什么", "工作集 = 正在摊开看的资料。收紧工作集 = 让进程把不看的资料收进硬盘，能应急腾内存，但要用时得再搬回来，所以别频繁点。")));
        panel.Children.Add(BuildHelpCard(
            "三个清理按钮", "\uE896", ThemeColors.AccentOrange,
            ("清理待机内存", "清掉系统\u201C预读缓存\u201D（待机列表），安全、不伤程序，放心点。"),
            ("收紧系统工作集", "给所有进程内存瘦身，内存告急时应急用，不建议频繁点。"),
            ("全部清理", "上面两个一起做，最干净。")));
        panel.Children.Add(BuildHintCard2());

        var dialog = new ContentDialog
        {
            Title = "内存小课堂",
            CloseButtonText = "知道了",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
            Content = new ScrollViewer
            {
                MaxHeight = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            }
        };
        await dialog.ShowAsync();
    }

    private static Border BuildHelpCard(string title, string glyph, Windows.UI.Color accent,
        params (string Label, string Text)[] lines)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 15,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var content = new StackPanel { Spacing = 4 };
        foreach (var (label, text) in lines)
        {
            content.Children.Add(new TextBlock
            {
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                Inlines =
                {
                    new Microsoft.UI.Xaml.Documents.Run { Text = $"{label}：", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(accent) },
                    new Microsoft.UI.Xaml.Documents.Run { Text = text }
                }
            });
        }

        var border = new Border
        {
            Background = new SolidColorBrush(ThemeColors.SubtleBg),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10)
        };
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(header);
        stack.Children.Add(content);
        border.Child = stack;
        return border;
    }

    private static Border BuildHintCard()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(ThemeColors.SubtleBg),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10)
        };
        border.Child = new TextBlock
        {
            Text = "一句话记住：物理内存 = 办公桌（内存条）；虚拟内存 = 信用额度（内存条+硬盘分页文件）；系统工作集 = 正摊开看的资料。",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };
        return border;
    }

    private static Border BuildHintCard2()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(ThemeColors.SubtleBg),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10)
        };
        border.Child = new TextBlock
        {
            Text = "小提醒：虚拟内存占用高 ≠ 内存不足。电脑不卡就无需整理内存；是否卡顿，可看物理内存“可用”是否充足、分页文件磁盘占用是否很大。",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        return border;
    }

    // ---------- 其他 ----------

    private void ShowToast(string message, InfoBarSeverity severity)
    {
        ToastBar.Severity = severity;
        ToastBar.Message = message;
        ToastBar.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
