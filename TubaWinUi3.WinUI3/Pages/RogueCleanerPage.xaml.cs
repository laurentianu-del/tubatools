using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Runtime.InteropServices.WindowsRuntime;
using System.ComponentModel;
using TubaWinUi3.Services;
using TubaWinUi3.Services.ActiveIntercept;
using TubaWinUi3.Services.RogueCleaner;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>「流氓软件的克星」内置工具页面（移植自 RogueCleaner，MIT）。</summary>
public sealed partial class RogueCleanerPage : Page
{
    private readonly DataStore _store = DataStore.CreateDefault();
    private readonly ScannerEngine _scanner = new();
    private readonly CleanerEngine _cleaner;
    private List<Finding> _allFindings = [];
    private string _filter = "overview";
    private CancellationTokenSource? _cts;
    private bool _scanning;
    private bool _suppressRender;
    private bool _hasScanned;
    private bool _startupAllMode;
    private bool _findingsAllMode;
    private Finding? _flyoutFinding;

    // 软件图标缓存（原版结果行展示软件图标）
    private readonly Dictionary<int, BitmapImage> _findingIcons = [];
    private readonly Dictionary<string, BitmapImage> _menuIcons = [];

    // 统计
    private int _statFound;
    private int _statSuggested;
    private int _statManageable;
    private int _statReportOnly;

    // 右键菜单管理
    private bool _cmAllMode;
    private string _cmSearchKeyword = "";
    private List<ContextMenuEntry> _cmEntries = [];
    private List<SpecialMenuEntry> _specialEntries = [];
    private List<AdvancedMenuEntry> _advancedEntries = [];
    private List<CleanupBatch> _batches = [];

    public RogueCleanerPage()
    {
        InitializeComponent();
        _cleaner = new CleanerEngine(_store);
        _store.Ensure();
        Logger.Initialize(_store);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Nav.SelectedItem is null) Nav.SelectedItem = NavOverview;
        BuildStatCards();
        RefreshContextMenus();
        RefreshRecovery();
        // 进入页面自动扫描一次；之后点「刷新」重新扫描
        ScanNow();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string target && target == "contextmenu")
        {
            Nav.SelectedItem = NavContextMenu;
        }
        else if (e.Parameter is string target2 && target2 == "activeintercept")
        {
            Nav.SelectedItem = NavActiveIntercept;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cts?.Cancel();
        StopAiPolling();
    }

    #region 导航

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "overview";
        bool scanPanel = tag is "overview" or "startup" or "popup";
        ScanPanel.Visibility = scanPanel ? Visibility.Visible : Visibility.Collapsed;
        ContextMenuPanel.Visibility = tag == "contextmenu" ? Visibility.Visible : Visibility.Collapsed;
        ActiveInterceptPanel.Visibility = tag == "activeintercept" ? Visibility.Visible : Visibility.Collapsed;
        RecoveryPanel.Visibility = tag == "recovery" ? Visibility.Visible : Visibility.Collapsed;
        if (scanPanel)
        {
            _filter = tag;
            _findingsAllMode = false;
            RenderFindings();
        }
        else if (tag == "activeintercept")
        {
            RefreshActiveIntercept();
        }
    }

    private List<Finding> FilteredFindings()
    {
        if (_findingsAllMode) return _allFindings;
        if (_filter == "startup")
        {
            return _allFindings.Where(RogueCleanerViewFilters.MatchesStartupTab).ToList();
        }
        if (_filter == "popup")
        {
            return _allFindings.Where(RogueCleanerViewFilters.MatchesPopupTab).ToList();
        }
        return _allFindings;
    }

    #endregion

#region 主动拦截（后端审核）

    private InterceptEventDto? _aiSelectedEvent;
    private bool _aiPolicyInitializing;
    private readonly List<InterceptEventDto> _aiAllEvents = [];
    private List<InterceptEventDto> _aiVisible = [];
    private string _aiSearchKeyword = "";
    private string _aiFilter = "all";
    private readonly HashSet<string> _aiPendingRemovedRows = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _aiPollTimer;

    // ---------- 生命周期与刷新 ----------

    private void RefreshActiveIntercept()
    {
        if (ActiveInterceptPanel is null) return;

        var enabled = AppSettings.GetBool("ActiveInterceptEnabled", false);
        var running = ActiveInterceptService.IsRunning;

        AiStatusDot.Fill = new SolidColorBrush(
            running ? Microsoft.UI.Colors.LimeGreen : enabled ? Microsoft.UI.Colors.OrangeRed : Microsoft.UI.Colors.Gray);
        AiRunningText.Text = running
            ? "主动拦截后端：运行中"
            : enabled
                ? "主动拦截后端：未运行"
                : "主动拦截后端：已关闭";
        AiRunningText.Foreground = new SolidColorBrush(
            running ? Microsoft.UI.Colors.LimeGreen : enabled ? Microsoft.UI.Colors.OrangeRed : Microsoft.UI.Colors.Gray);

        AiEnableBackendBtn.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        AiDisableBackendBtn.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (!enabled)
        {
            ShowAiStatus("主动拦截后端已关闭：新增第三方右键菜单不会被自动拦截。可在上方或设置页开启。", InfoBarSeverity.Warning);
        }
        else if (!running)
        {
            ShowAiStatus("主动拦截后端未在运行，请点击「启用主动拦截」启动常驻后端。", InfoBarSeverity.Warning);
        }
        else
        {
            CloseAiStatus();
        }

        StartAiPolling();
        LoadActiveInterceptEvents(quiet: false);
        RefreshIgnoredCount();
    }

    private void LoadActiveInterceptEvents(bool quiet)
    {
        if (AiEventList is null) return;

        List<InterceptEventDto> fresh;
        try { fresh = ActiveInterceptData.ReadEvents(); }
        catch { fresh = []; }

        bool changed = !quiet && AiContentChanged(fresh);

        // 保留勾选与详情选中（按 RowId 归并）；行内 CheckBox 勾选计数通过 INPC 实时刷新
        var checkedRowIds = new HashSet<string>(_aiAllEvents.Where(e => e.Selected).Select(e => e.RowId), StringComparer.OrdinalIgnoreCase);
        var detailRowId = _aiSelectedEvent?.RowId;

        _aiAllEvents.Clear();
        foreach (var evt in fresh)
        {
            if (_aiPendingRemovedRows.Contains(evt.RowId)) continue; // 已乐观删除、后端尚未消费
            evt.Selected = checkedRowIds.Contains(evt.RowId);
            evt.PropertyChanged += OnAiEventPropertyChanged;
            if (!string.IsNullOrEmpty(detailRowId) && string.Equals(evt.RowId, detailRowId, StringComparison.OrdinalIgnoreCase))
            {
                _aiSelectedEvent = evt;
            }
            _aiAllEvents.Add(evt);
        }
        if (_aiSelectedEvent is not null && _aiAllEvents.All(e => !ReferenceEquals(e, _aiSelectedEvent)))
        {
            _aiSelectedEvent = null; // 详情行已被删除
        }

        // 后端已消费删除指令：清除 pending 标记
        if (_aiPendingRemovedRows.Count > 0)
        {
            var rowsOnDisk = new HashSet<string>(fresh.Select(e => e.RowId), StringComparer.OrdinalIgnoreCase);
            _aiPendingRemovedRows.RemoveWhere(id => !rowsOnDisk.Contains(id));
        }

        if (changed || !quiet || AiEventList.ItemsSource is null)
        {
            ApplyAiFilter();
            if (_aiSelectedEvent is null) RenderAiDetails(null);
        }
        UpdateAiSummary();
        UpdateAiBatch();
        RefreshIgnoredCount();
    }

    private bool AiContentChanged(List<InterceptEventDto> fresh)
    {
        if (_aiAllEvents.Count != fresh.Count) return true;
        if (fresh.Count == 0) return false;
        return !string.Equals(_aiAllEvents[0].RowId, fresh[0].RowId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_aiAllEvents[^1].RowId, fresh[^1].RowId, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyAiFilter()
    {
        if (AiEventList is null) return;

        List<InterceptEventDto> filtered = _aiFilter switch
        {
            "blocked" => _aiAllEvents.Where(e => e.Action is "Blocked" or "Reblocked").ToList(),
            "allowed" => _aiAllEvents.Where(e => e.Action is "Allowed" or "Unblocked").ToList(),
            "failed" => _aiAllEvents.Where(e => e.Action is "BlockedFailed").ToList(),
            _ => new List<InterceptEventDto>(_aiAllEvents),
        };

        if (!string.IsNullOrWhiteSpace(_aiSearchKeyword))
        {
            var kw = _aiSearchKeyword;
            filtered = filtered.Where(e =>
                (!string.IsNullOrEmpty(e.Name) && e.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.SubKey) && e.SubKey.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.ExePath) && e.ExePath.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.Clsid) && e.Clsid.Contains(kw, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }
        _aiVisible = filtered;

        var keepRowId = _aiSelectedEvent?.RowId;
        AiEventList.ItemsSource = null;
        AiEventList.ItemsSource = filtered;
        if (!string.IsNullOrEmpty(keepRowId))
        {
            AiEventList.SelectedItem = filtered.FirstOrDefault(e => string.Equals(e.RowId, keepRowId, StringComparison.OrdinalIgnoreCase));
        }

        AiEmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AiEmptyText.Text = string.IsNullOrWhiteSpace(_aiSearchKeyword)
            ? _aiFilter switch
            {
                "blocked" => "没有已拦截的记录",
                "allowed" => "没有已放行的记录",
                "failed" => "没有拦截失败的记录",
                _ => "暂无拦截记录",
            }
            : $"没有匹配\u201C{_aiSearchKeyword}\u201D的拦截记录";
    }

    // ---------- 轮询刷新 ----------

    private void StartAiPolling()
    {
        if (_aiPollTimer is null)
        {
            _aiPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _aiPollTimer.Tick += (_, _) => LoadActiveInterceptEvents(quiet: true);
        }
        if (!_aiPollTimer.IsEnabled) _aiPollTimer.Start();
    }

    private void StopAiPolling()
    {
        if (_aiPollTimer is not null && _aiPollTimer.IsEnabled) _aiPollTimer.Stop();
    }

    /// <summary>操作提交后短延时刷新一次，让后端处理结果尽快回显。</summary>
    private void AiScheduleRefresh()
    {
        Task.Delay(2500).ContinueWith(_ =>
            DispatcherQueue.TryEnqueue(() => LoadActiveInterceptEvents(quiet: true)));
    }

    // ---------- 统计 / 多选计数（实时） ----------

    private void UpdateAiSummary()
    {
        if (AiCountText is null) return;
        var total = _aiAllEvents.Count;
        var blocked = _aiAllEvents.Count(e => e.Action is "Blocked" or "Reblocked");
        var allowed = _aiAllEvents.Count(e => e.Action is "Allowed" or "Unblocked");
        var failed = _aiAllEvents.Count(e => e.Action is "BlockedFailed");
        AiCountText.Text = total == 0
            ? "暂无拦截记录"
            : $"共 {total} 条 · 拦截 {blocked} 次 · 放行 {allowed} 次" + (failed > 0 ? $" · 失败 {failed}" : "");
    }

    private void UpdateAiBatch()
    {
        if (AiSelectionText is null) return;
        var checkedCount = _aiVisible.Count(e => e.Selected);

        // 「已选 N 项」主多选指示
        AiSelectionPill.Visibility = checkedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        AiSelectionText.Text = $"已选 {checkedCount} 项";

        if (AiBatchAllowBtn is not null)
        {
            AiBatchAllowBtn.Content = checkedCount > 0 ? $"放行选中（{checkedCount}）" : "放行选中";
            AiBatchAllowBtn.IsEnabled = checkedCount > 0;
        }
        if (AiBatchReblockBtn is not null)
        {
            AiBatchReblockBtn.Content = checkedCount > 0 ? $"重新拦截选中（{checkedCount}）" : "重新拦截选中";
            AiBatchReblockBtn.IsEnabled = checkedCount > 0;
        }
        if (AiBatchDeleteBtn is not null)
        {
            AiBatchDeleteBtn.Content = checkedCount > 0 ? $"删除记录（{checkedCount}）" : "删除记录";
            AiBatchDeleteBtn.IsEnabled = checkedCount > 0;
        }
        if (AiBatchTrackBtn is not null)
        {
            AiBatchTrackBtn.Content = checkedCount > 0 ? $"停止追踪（{checkedCount}）" : "停止追踪";
            AiBatchTrackBtn.IsEnabled = checkedCount > 0;
        }

        // 表头全选框状态（三态）
        if (AiMasterCheck is not null)
        {
            AiMasterCheck.IsChecked = _aiVisible.Count > 0 && _aiVisible.All(e => e.Selected)
                ? true
                : _aiVisible.Any(e => e.Selected) ? (bool?)null : false;
        }

        if (_aiSelectedEvent is null && AiDetailsText is not null)
        {
            AiDetailsText.Text = checkedCount > 0
                ? $"已勾选 {checkedCount} 条记录，可使用上方批量操作"
                : "选中一条记录查看详情";
        }
    }

    private void RefreshIgnoredCount()
    {
        if (AiShowIgnoredBtn is null) return;
        var count = 0;
        try { count = ActiveInterceptData.ReadIgnored().Count; } catch { }
        AiShowIgnoredBtn.Content = $"已忽略（{count}）";
    }

    private List<InterceptEventDto> GetCheckedEvents() => _aiVisible.Where(e => e.Selected).ToList();

    private void OnAiEventPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InterceptEventDto.Selected)) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateAiBatch();
            if (_aiSelectedEvent is null) RenderAiDetails(null);
        });
    }

    // ---------- 搜索 / 筛选 ----------

    private void AiSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _aiSearchKeyword = AiSearchBox.Text.Trim();
        AiSearchClearBtn.Visibility = _aiSearchKeyword.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyAiFilter();
        UpdateAiBatch();
    }

    private void AiSearchClear_Click(object sender, RoutedEventArgs e) => AiSearchBox.Text = string.Empty;

    private void AiFilterBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var tag = (sender.SelectedItem as SelectorBarItem)?.Tag as string ?? "all";
        _aiFilter = tag;
        ApplyAiFilter();
        UpdateAiBatch();
    }

    // ---------- 全选 ----------

    private void AiSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var evt in _aiVisible) evt.Selected = true;   // INPC → 行内复选框自动同步
        UpdateAiBatch();
    }

    private void AiDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var evt in _aiVisible) evt.Selected = false;
        UpdateAiBatch();
    }

    private void AiMasterCheck_Click(object sender, RoutedEventArgs e)
    {
        var target = AiMasterCheck.IsChecked == true;
        foreach (var evt in _aiVisible) evt.Selected = target;
        UpdateAiBatch();
    }

    // ---------- 选择与详情 ----------

    private void AiEventList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var evt = AiEventList.SelectedItem as InterceptEventDto;
        _aiSelectedEvent = evt;
        RenderAiDetails(evt);
    }

    private void AiEventList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is InterceptEventDto evt)
        {
            AiEventList.SelectedItem = evt;
            _aiSelectedEvent = evt;
            var isBlocked = evt.Action is "Blocked" or "Reblocked";
            AiFlyAllow.IsEnabled = isBlocked;
            AiFlyTrust.IsEnabled = isBlocked && !string.IsNullOrWhiteSpace(evt.ExePath);
            AiFlyReblock.IsEnabled = !isBlocked;
            AiFlyPolicy.IsEnabled = !string.IsNullOrWhiteSpace(evt.ExePath);
            RenderAiDetails(evt);
        }
    }

    private void RenderAiDetails(InterceptEventDto? evt)
    {
        if (AiDetailsText is null) return;

        if (evt is null)
        {
            AiDetailsText.Text = _aiVisible.Count(e => e.Selected) > 0
                ? $"已勾选 {_aiVisible.Count(e => e.Selected)} 条记录，可使用上方批量操作"
                : "选中一条记录查看详情";
            if (AiDetailActions is not null) AiDetailActions.Visibility = Visibility.Collapsed;
            if (AiPolicySection is not null) AiPolicySection.Visibility = Visibility.Collapsed;
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"动作：{evt.ActionText}");
        sb.AppendLine($"时间：{evt.TimeText}");
        sb.AppendLine($"名称：{evt.Name}");
        if (!string.IsNullOrWhiteSpace(evt.SubKey)) sb.AppendLine($"位置：{evt.SubKey}");
        if (!string.IsNullOrWhiteSpace(evt.ExePath)) sb.AppendLine($"程序：{evt.ExePath}");
        if (!string.IsNullOrWhiteSpace(evt.Clsid)) sb.AppendLine($"CLSID：{evt.Clsid}");
        if (!string.IsNullOrWhiteSpace(evt.Note)) sb.AppendLine($"备注：{evt.Note}");
        AiDetailsText.Text = sb.ToString();

        if (AiDetailActions is not null)
        {
            AiDetailActions.Visibility = Visibility.Visible;
            var isBlocked = evt.Action is "Blocked" or "Reblocked";
            AiAllowBtn.IsEnabled = isBlocked;
            AiAllowTrustBtn.IsEnabled = isBlocked && !string.IsNullOrWhiteSpace(evt.ExePath);
            AiReblockBtn.IsEnabled = evt.Action is "Allowed" or "Unblocked";
            AiIgnoreBtn.IsEnabled = true;
            AiDeleteBtn.IsEnabled = true;
        }

        if (!string.IsNullOrWhiteSpace(evt.ExePath) && AiPolicySection is not null)
        {
            AiPolicySection.Visibility = Visibility.Visible;
            InitPolicyComboBox(evt.ExePath);
        }
        else if (AiPolicySection is not null)
        {
            AiPolicySection.Visibility = Visibility.Collapsed;
        }
    }

    private void AiCopyDetail_Click(object sender, RoutedEventArgs e)
    {
        var evt = _aiSelectedEvent ?? AiEventList?.SelectedItem as InterceptEventDto;
        if (evt is null) return;
        var text = $"动作：{evt.ActionText}\n时间：{evt.TimeText}\n名称：{evt.Name}\n位置：{evt.SubKey}\n程序：{evt.ExePath}\nCLSID：{evt.Clsid}\n备注：{evt.Note}";
        try
        {
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
            data.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        }
        catch { }
    }

    private void AiFlyPolicy_Click(object sender, RoutedEventArgs e)
    {
        if (_aiSelectedEvent is null || string.IsNullOrWhiteSpace(_aiSelectedEvent.ExePath)) return;
        if (AiPolicySection is null) return;
        AiPolicySection.Visibility = Visibility.Visible;
        InitPolicyComboBox(_aiSelectedEvent.ExePath);
    }

    // ---------- 信任策略 ----------

    private void InitPolicyComboBox(string exePath)
    {
        _aiPolicyInitializing = true;
        AiPolicyComboBox.Items.Clear();
        AiPolicyComboBox.Items.Add("每次都询问（默认）");
        AiPolicyComboBox.Items.Add("总是放行（信任此程序）");
        AiPolicyComboBox.Items.Add("总是拦截（阻止此程序）");
        AiPolicyComboBox.SelectedIndex = ActiveInterceptData.ReadTrustPolicy(exePath) switch
        {
            "allow" => 1,
            "block" => 2,
            _ => 0,
        };
        _aiPolicyInitializing = false;
    }

    private void AiSavePolicy_Click(object sender, RoutedEventArgs e)
    {
        if (_aiSelectedEvent is null || string.IsNullOrWhiteSpace(_aiSelectedEvent.ExePath)) return;
        if (_aiPolicyInitializing) return;

        var policy = AiPolicyComboBox.SelectedIndex switch
        {
            1 => "allow",
            2 => "block",
            _ => "ask",
        };
        ActiveInterceptData.WritePolicyCommand(_aiSelectedEvent.ExePath, policy);

        var policyText = AiPolicyComboBox.SelectedIndex switch
        {
            1 => "总是放行",
            2 => "总是拦截",
            _ => "每次都询问",
        };
        ShowAiStatus($"已提交信任策略：{policyText}（对该程序所有条目生效，后端下一轮轮询时应用）", InfoBarSeverity.Success);
    }

    // ---------- 单条操作 ----------

    private void AiAllow_Click(object sender, RoutedEventArgs e) => AiAllowTrustOrAllow(trust: false);
    private void AiFlyAllow_Click(object sender, RoutedEventArgs e) => AiAllowTrustOrAllow(trust: false);
    private void AiAllowTrust_Click(object sender, RoutedEventArgs e) => AiAllowTrustOrAllow(trust: true);
    private void AiFlyTrust_Click(object sender, RoutedEventArgs e) => AiAllowTrustOrAllow(trust: true);

    private async void AiAllowTrustOrAllow(bool trust)
    {
        var evt = _aiSelectedEvent;
        if (evt is null || string.IsNullOrWhiteSpace(evt.Id)) return;

        if (trust)
        {
            var dialog = new ContentDialog
            {
                Title = "放行并信任此程序？",
                Content = new TextBlock
                {
                    Text = $"将放行「{evt.Name}」，并把 {evt.ExePath} 加入信任列表：\n\n" +
                           "· 该程序当前所有被拦截的右键菜单立即解除屏蔽\n" +
                           "· 该程序以后新增的右键菜单自动放行，不再拦截、不再提醒\n\n" +
                           "可在详情面板的「程序信任策略」中随时改回。",
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "放行并信任",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        ActiveInterceptData.WriteAllowCommand(evt.Id, trust);
        evt.Action = "Allowed";
        evt.TimestampUtc = DateTime.UtcNow.ToString("o");
        evt.Selected = false;
        ApplyAiFilter();
        UpdateAiSummary();
        UpdateAiBatch();
        ShowAiStatus(trust
            ? "已提交放行并信任此程序，同名程序的新增菜单将自动放行"
            : "已提交放行，后端将在数秒内生效", InfoBarSeverity.Success);
        AiScheduleRefresh();
    }

    private void AiReblock_Click(object sender, RoutedEventArgs e) => AiReblockCore();

    private void AiFlyReblock_Click(object sender, RoutedEventArgs e) => AiReblockCore();

    private void AiReblockCore()
    {
        var evt = _aiSelectedEvent;
        if (evt is null || string.IsNullOrWhiteSpace(evt.Id)) return;

        ActiveInterceptData.WriteReblockCommand(evt.Id);
        evt.Action = "Reblocked";
        evt.TimestampUtc = DateTime.UtcNow.ToString("o");
        evt.Selected = false;
        ApplyAiFilter();
        UpdateAiSummary();
        UpdateAiBatch();
        ShowAiStatus("已提交重新拦截，后端将在数秒内重新屏蔽", InfoBarSeverity.Success);
        AiScheduleRefresh();
    }

    private void AiDelete_Click(object sender, RoutedEventArgs e) => _ = AiDeleteCoreAsync(_aiSelectedEvent);

    private async void AiFlyDelete_Click(object sender, RoutedEventArgs e) => await AiDeleteCoreAsync(_aiSelectedEvent);

    private async Task AiDeleteCoreAsync(InterceptEventDto? evt)
    {
        if (evt is null || string.IsNullOrWhiteSpace(evt.RowId)) return;

        var dialog = new ContentDialog
        {
            Title = "删除这条记录？",
            Content = new TextBlock
            {
                Text = "只删除这条事件记录（历史），不改变该条目的屏蔽/放行状态。\n\n" +
                       "若想彻底不再拦截与提醒，请使用「停止追踪」。",
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "删除记录",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ActiveInterceptData.WriteRemoveRowsCommand(new[] { evt.RowId });
        AiOptimisticRemoveRows(new[] { evt.RowId });
        ShowAiStatus("已提交删除记录，正在与后端同步", InfoBarSeverity.Success);
        AiScheduleRefresh();
    }

    private void AiIgnore_Click(object sender, RoutedEventArgs e) => _ = AiIgnoreCoreAsync(_aiSelectedEvent);

    private async void AiFlyIgnore_Click(object sender, RoutedEventArgs e) => await AiIgnoreCoreAsync(_aiSelectedEvent);

    private async Task AiIgnoreCoreAsync(InterceptEventDto? evt)
    {
        if (evt is null || string.IsNullOrWhiteSpace(evt.Id)) return;

        var dialog = new ContentDialog
        {
            Title = "停止追踪？",
            Content = new TextBlock
            {
                Text = $"将删除「{evt.Name}」的全部拦截记录，并停止追踪：\n\n" +
                       "· 该条目的屏蔽状态保持当前状态（不会主动解除）\n" +
                       "· 之后该条目再次出现也不会拦截、不会提醒\n\n" +
                       "可在「已忽略」列表中随时恢复追踪。",
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "停止追踪",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ActiveInterceptData.WriteIgnoreCommand(evt.Id);
        var rows = _aiAllEvents.Where(x => string.Equals(x.Id, evt.Id, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.RowId).ToList();
        AiOptimisticRemoveRows(rows);
        ShowAiStatus("已提交停止追踪，该条目不再拦截、不再提醒", InfoBarSeverity.Success);
        AiScheduleRefresh();
    }

    /// <summary>乐观删除：立即从界面移除，后端稍后落盘（pending 集合防轮询复活）。</summary>
    private void AiOptimisticRemoveRows(IEnumerable<string> rowIds)
    {
        var set = new HashSet<string>(rowIds, StringComparer.OrdinalIgnoreCase);
        foreach (var id in set) _aiPendingRemovedRows.Add(id);
        _aiAllEvents.RemoveAll(e => set.Contains(e.RowId));
        if (_aiSelectedEvent is not null && set.Contains(_aiSelectedEvent.RowId)) _aiSelectedEvent = null;
        ApplyAiFilter();
        UpdateAiSummary();
        UpdateAiBatch();
    }

    // ---------- 批量操作（带确认对话框） ----------

    private async void AiBatchAllow_Click(object sender, RoutedEventArgs e) => await AiBatchAllowCoreAsync();

    private async Task AiBatchAllowCoreAsync()
    {
        var selected = GetCheckedEvents();
        if (selected.Count == 0) return;

        var dialog = new ContentDialog
        {
            Title = $"确认放行 {selected.Count} 项？",
            Content = new TextBlock
            {
                Text = "以下项目将被放行（解除屏蔽并加入白名单）：\n\n" + BuildPreview(selected),
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "确认放行",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        foreach (var evt in selected)
        {
            if (string.IsNullOrWhiteSpace(evt.Id)) continue;
            ActiveInterceptData.WriteAllowCommand(evt.Id, trust: false);
            evt.Action = "Allowed";
            evt.TimestampUtc = DateTime.UtcNow.ToString("o");
            evt.Selected = false;
        }
        ApplyAiFilter();
        UpdateAiSummary();
        UpdateAiBatch();
        ShowAiStatus($"已提交放行 {selected.Count} 项，后端将在数秒内生效", InfoBarSeverity.Success);
        AiScheduleRefresh();
    }

    private async void AiBatchReblock_Click(object sender, RoutedEventArgs e) => await AiBatchReblockCoreAsync();

    private async Task AiBatchReblockCoreAsync()
    {
        var selected = GetCheckedEvents();
        if (selected.Count == 0) return;

        var dialog = new ContentDialog
        {
            Title = $"确认重新拦截 {selected.Count} 项？",
            Content = new TextBlock
            {
                Text = "以下项目将被重新拦截（设置期望状态为屏蔽，下轮轮询时自动执行）：\n\n" + BuildPreview(selected),
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "确认拦截",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        foreach (var evt in selected)
        {
            if (string.IsNullOrWhiteSpace(evt.Id)) continue;
            ActiveInterceptData.WriteReblockCommand(evt.Id);
            evt.Action = "Reblocked";
            evt.TimestampUtc = DateTime.UtcNow.ToString("o");
            evt.Selected = false;
        }
        ApplyAiFilter();
        UpdateAiSummary();
        UpdateAiBatch();
        ShowAiStatus($"已提交重新拦截 {selected.Count} 项", InfoBarSeverity.Success);
        AiScheduleRefresh();
    }

    private async void AiBatchDelete_Click(object sender, RoutedEventArgs e) => await AiBatchDeleteCoreAsync();

    private async Task AiBatchDeleteCoreAsync()
    {
        var selected = GetCheckedEvents();
        if (selected.Count == 0) return;

        var dialog = new ContentDialog
        {
            Title = $"确认删除 {selected.Count} 条记录？",
            Content = new TextBlock
            {
                Text = "以下记录将从事件日志中永久删除（不改变屏蔽/放行状态）：\n\n" + BuildPreview(selected),
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "确认删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var rows = selected.Select(e => e.RowId).Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (rows.Count > 0)
        {
            ActiveInterceptData.WriteRemoveRowsCommand(rows);
            AiOptimisticRemoveRows(rows);
        }
        ShowAiStatus($"已提交删除 {selected.Count} 条记录，正在与后端同步", InfoBarSeverity.Success);
        AiScheduleRefresh();
    }

    private async void AiBatchTrack_Click(object sender, RoutedEventArgs e) => await AiBatchTrackCoreAsync();

    private async Task AiBatchTrackCoreAsync()
    {
        var selected = GetCheckedEvents();
        if (selected.Count == 0) return;

        var dialog = new ContentDialog
        {
            Title = $"确认停止追踪 {selected.Count} 项？",
            Content = new TextBlock
            {
                Text = "以下项目将删除全部相关记录并停止追踪：之后再次出现也不拦截、不提醒。\n\n" + BuildPreview(selected) +
                       "\n可在「已忽略」列表中随时恢复追踪。",
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "停止追踪",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var ids = selected.Select(e => e.Id).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var rows = _aiAllEvents.Where(e => idSet.Contains(e.Id)).Select(e => e.RowId).ToList();
        foreach (var id in ids) ActiveInterceptData.WriteIgnoreCommand(id);
        _aiPendingRemovedRows.UnionWith(rows);
        AiOptimisticRemoveRows(rows);
        ShowAiStatus($"已提交停止追踪 {ids.Count} 项", InfoBarSeverity.Success);
        AiScheduleRefresh();
    }

    private async void AiClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (_aiAllEvents.Count == 0)
        {
            await ShowInfo("清空记录", "当前没有任何拦截记录。");
            return;
        }

        var dialog = new ContentDialog
        {
            Title = $"确认清空全部 {_aiAllEvents.Count} 条记录？",
            Content = new TextBlock
            {
                Text = "仅清空事件记录（历史），不影响屏蔽状态、信任策略与停止追踪列表。\n\n后续新的拦截事件仍会继续记录。",
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "清空记录",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ActiveInterceptData.WriteClearEventsCommand();
        _aiAllEvents.Clear();
        _aiPendingRemovedRows.Clear();
        _aiSelectedEvent = null;
        ApplyAiFilter();
        UpdateAiSummary();
        UpdateAiBatch();
        ShowAiStatus("已提交清空记录", InfoBarSeverity.Success);
        AiScheduleRefresh();
    }

    // ---------- 后端开关 / 已忽略管理 ----------

    private void AiRefresh_Click(object sender, RoutedEventArgs e) => RefreshActiveIntercept();

    private void AiOpenSettings_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.NavigateToSettings("ActiveInterceptNotifyMode");
    }

    private void AiEnableBackend_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Set("ActiveInterceptEnabled", true);
        ActiveInterceptService.Start();
        RefreshActiveIntercept();
    }

    private void AiDisableBackend_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Set("ActiveInterceptEnabled", false);
        ActiveInterceptService.Stop();
        RefreshActiveIntercept();
    }

    private async void AiShowIgnored_Click(object sender, RoutedEventArgs e)
    {
        var items = ActiveInterceptData.ReadIgnored();
        if (items.Count == 0)
        {
            await ShowInfo("已忽略（停止追踪）", "当前没有已停止追踪的项目。\n\n停止追踪后，对应右键菜单项将不再被拦截、不再提醒。");
            return;
        }

        var rows = new StackPanel { Spacing = 4 };

        void AddRow(IgnoredItemDto item)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new TextBlock
            {
                Text = $"{item.Name}\n{item.ExePath}\n{item.SubKey}\n{item.TimeText}",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            };
            var btn = new Button
            {
                Content = "恢复追踪",
                FontSize = 12,
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var captured = item;
            btn.Click += (_, _) =>
            {
                ActiveInterceptData.WriteUnignoreCommand(captured.Id);
                rows.Children.Remove(row);
                RefreshIgnoredCount();
                ShowAiStatus($"已提交恢复追踪：{captured.Name}", InfoBarSeverity.Success);
                AiScheduleRefresh();
            };
            Grid.SetColumn(info, 0);
            Grid.SetColumn(btn, 1);
            row.Children.Add(info);
            row.Children.Add(btn);
            rows.Children.Add(row);
        }
        foreach (var item in items) AddRow(item);

        var scroll = new ScrollViewer
        {
            MaxHeight = 340,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = rows,
        };
        var allBtn = new Button
        {
            Content = "全部恢复追踪",
            FontSize = 12,
            Padding = new Thickness(12, 5, 12, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };
        allBtn.Click += (_, _) =>
        {
            foreach (var item in items) ActiveInterceptData.WriteUnignoreCommand(item.Id);
            rows.Children.Clear();
            RefreshIgnoredCount();
            ShowAiStatus($"已提交恢复 {items.Count} 项的追踪", InfoBarSeverity.Success);
            AiScheduleRefresh();
        };

        var panel = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = $"共 {items.Count} 项已停止追踪（不再拦截、不再提醒）：", FontSize = 12, TextWrapping = TextWrapping.Wrap },
                scroll,
                allBtn,
            },
        };

        var dialog = new ContentDialog
        {
            Title = "已忽略（停止追踪）",
            Content = panel,
            PrimaryButtonText = "关闭",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        await dialog.ShowAsync();
    }

    // ---------- 反馈 ----------

    private void ShowAiStatus(string message, InfoBarSeverity severity)
    {
        if (AiStatusBar is null) return;
        AiStatusBar.Severity = severity;
        AiStatusBar.Message = message;
        AiStatusBar.IsOpen = true;
    }

    private void CloseAiStatus()
    {
        if (AiStatusBar is not null) AiStatusBar.IsOpen = false;
    }

    private static string BuildPreview(List<InterceptEventDto> selected)
    {
        var sb = new StringBuilder();
        foreach (var evt in selected.Take(15))
            sb.AppendLine($"· {evt.Name}（{evt.ActionText}）");
        if (selected.Count > 15) sb.AppendLine($"… 还有 {selected.Count - 15} 项");
        return sb.ToString();
    }

    #endregion
    #region 扫描与清理

    private void Scan_Click(object sender, RoutedEventArgs e) => ScanNow();

    private void ScanNow()
    {
        if (_scanning)
        {
            _cts?.Cancel();
            return;
        }
        _cts = new CancellationTokenSource();
        _startupAllMode = false;
        _findingsAllMode = false;
        StartupAllList.Visibility = Visibility.Collapsed;
        BackToFindingsBtn.Visibility = Visibility.Collapsed;
        _allFindings = [];
        _findingIcons.Clear();
        _statFound = _statSuggested = _statManageable = _statReportOnly = 0;
        UpdateStatCards();
        ResultsList.ItemsSource = null;
        EmptyPanel.Visibility = Visibility.Visible;
        EmptyText.Text = "正在扫描…";
        ScanRing.IsActive = true;
        ProgressPanel.Visibility = Visibility.Visible;
        StageText.Text = "准备扫描…";
        ScanBtn.Content = "取消扫描";
        _scanning = true;
        SetActionButtons(false);

        var sink = new PageProgressSink(DispatcherQueue,
            stage => StageText.Text = stage,
            f => OnFindingDiscovered(f));

        Task.Run(() =>
        {
            List<Finding> findings = [];
            try { findings = _scanner.ScanAll(sink); }
            catch (Exception ex)
            {
                Logger.Error("扫描失败", ex);
                sink.Stage("扫描出错：" + ex.Message);
            }
            return findings;
        }).ContinueWith(t =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _scanning = false;
                _hasScanned = true;
                ScanBtn.Content = "刷新";
                ScanRing.IsActive = false;
                if (t.IsFaulted || t.Result is null && t.Exception != null)
                {
                    var ex = t.Exception?.GetBaseException();
                    ProgressPanel.Visibility = Visibility.Visible;
                    StageText.Text = "扫描失败：" + (ex?.Message ?? "未知错误") + "。可点击「刷新」重试。";
                    EmptyPanel.Visibility = Visibility.Visible;
                    EmptyText.Text = "扫描失败，未能读取本机信息。";
                }
                else
                {
                    var findings = t.Result ?? [];
                    UserWhitelistStore.Apply(_store, findings);
                    _allFindings = findings;
                    ProgressPanel.Visibility = Visibility.Collapsed;
                    var warnings = _scanner.Warnings.Count;
                    StageText.Text = $"扫描完成，发现 {findings.Count} 项" + (warnings > 0 ? $"，另有 {warnings} 个受保护位置无法读取" : "") + "。";
                    ReportBtn.IsEnabled = findings.Count > 0;
                    RenderFindings();
                    HydrateFindingIcons();
                }
                SetActionButtons(true);
            });
        }, TaskScheduler.Default);
    }

    private void OnFindingDiscovered(Finding finding)
    {
        _statFound++;
        if (finding.CanClean) _statManageable++;
        else _statReportOnly++;
        if (finding.Risk == "高" || finding.Risk == "中") _statSuggested++;
        UpdateStatCards();
    }

    private void SetActionButtons(bool enabled)
    {
        CleanBtn.IsEnabled = enabled;
        SelectAllBtn.IsEnabled = enabled;
        SelectLowBtn.IsEnabled = enabled;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in FilteredFindings()) f.Selected = f.BulkSelectable;
    }

    private void SelectLow_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in FilteredFindings()) f.Selected = f.BulkSelectable && f.Risk == "低";
    }

    // 行内「处理」按钮：直接处理这一条（先备份，可在恢复中心还原）
    private async void RowAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Finding finding || !finding.CanClean) return;
        var dialog = new ContentDialog
        {
            Title = "处理：" + finding.CompactTitle,
            Content = new TextBlock
            {
                Text = $"将执行：{finding.ActionText}\n\n位置：{finding.TechnicalLocation}\n\n处理前会自动备份，可在「恢复中心」还原。",
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "开始处理",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        finding.Selected = true;
        CleanBtn.IsEnabled = false;
        StageText.Text = "正在处理：" + finding.CompactTitle + "…";
        ProgressPanel.Visibility = Visibility.Visible;
        CleanupBatch? batch = null;
        try
        {
            batch = await Task.Run(() => _cleaner.Clean(new[] { finding }));
        }
        catch (Exception ex)
        {
            Logger.Error("单条清理失败", ex);
            await ShowInfo("处理失败", ex.Message);
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            CleanBtn.IsEnabled = FilteredFindings().Any(f => f.Selected && f.CanClean);
        }

        if (batch != null)
        {
            var result = batch.Results.FirstOrDefault();
            if (result != null)
            {
                finding.Status = ChineseDisplayText.CleanupStatus(result.Status);
                await ShowInfo("处理完成", result.Status == "Done"
                    ? $"「{finding.CompactTitle}」已处理（{result.Message}）。可在「恢复中心」还原。"
                    : result.Status == "Launched"
                        ? "已打开该产品自己的卸载窗口，请按窗口提示操作。"
                        : $"处理结果：{result.Message}");
            }
            RenderFindings();
            RefreshRecovery();
        }
    }

    private async void ShowAllStartup_Click(object sender, RoutedEventArgs e)
    {
        _startupAllMode = true;
        ShowAllStartupBtn.Visibility = Visibility.Collapsed;
        BackToFindingsBtn.Visibility = Visibility.Visible;
        FilterTitle.Text = "全部启动项（只读，仅核对）";
        CountText.Text = "";
        EmptyPanel.Visibility = Visibility.Visible;
        EmptyText.Text = "正在读取全部启动项…";
        var items = await Task.Run(() => StartupItemEnumerator.List());
        StartupAllList.ItemsSource = items;
        EmptyPanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (items.Count == 0) EmptyText.Text = "没有找到任何启动项。";
    }

    private void BackToFindings_Click(object sender, RoutedEventArgs e)
    {
        _startupAllMode = false;
        RenderFindings();
    }

    // 列表底部「展示全部」：显示被当前 tab 筛选器隐藏的其他分类（与总览一致），再点一次回到筛选结果
    private void FindingsToggle_Click(object sender, RoutedEventArgs e)
    {
        _findingsAllMode = !_findingsAllMode;
        RenderFindings();
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var selected = FilteredFindings().Where(f => f.Selected && f.CanClean).ToList();
        if (selected.Count == 0)
        {
            await ShowInfo("没有可清理的勾选项", "请先勾选要处理的项目（风险「高/中/低」且不是「仅提示」的项目可以清理）。");
            return;
        }

        var preview = new StringBuilder();
        foreach (var f in selected.Take(15)) preview.AppendLine("· " + f.CompactTitle + " → " + f.ActionText);
        if (selected.Count > 15) preview.AppendLine("… 还有 " + (selected.Count - 15) + " 项");
        var adminNote = selected.Any(f => f.RequiresAdmin) && !AdminUtil.IsAdministrator()
            ? "\n\n部分项目属于系统范围，需要管理员权限；当前可能失败。"
            : "";

        var dialog = new ContentDialog
        {
            Title = "确认清理 " + selected.Count + " 项？",
            Content = new ScrollViewer
            {
                MaxHeight = 360,
                Content = new TextBlock
                {
                    Text = "所有操作会先备份到恢复中心，处理后可随时还原。\n\n" + preview + adminNote,
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap
                }
            },
            PrimaryButtonText = "开始清理",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        CleanBtn.IsEnabled = false;
        ScanBtn.IsEnabled = false;
        StageText.Text = "正在清理 " + selected.Count + " 项…";
        ProgressPanel.Visibility = Visibility.Visible;

        var batches = new List<CleanupBatch>();
        try
        {
            batches = await Task.Run(() => new List<CleanupBatch> { _cleaner.Clean(selected) });
        }
        catch (Exception ex)
        {
            Logger.Error("清理失败", ex);
            await ShowInfo("清理失败", ex.Message);
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            CleanBtn.IsEnabled = true;
            ScanBtn.IsEnabled = true;
        }

        if (batches.Count > 0)
        {
            var results = batches[0].Results;
            foreach (var result in results)
            {
                var finding = _allFindings.FirstOrDefault(f => f.Id == result.Id);
                if (finding != null) finding.Status = ChineseDisplayText.CleanupStatus(result.Status);
            }
            RenderFindings();
            var done = results.Count(r => r.Status == "Done");
            var launched = results.Count(r => r.Status == "Launched");
            var failed = results.Count(r => r.Status == "Failed" || r.Status == "Skipped");
            await ShowInfo("清理完成",
                $"成功 {done} 项，打开卸载窗口 {launched} 项，失败/跳过 {failed} 项。\n\n如需还原，请到「恢复中心」选择本次批次。");
            RefreshRecovery();
        }
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(_store.Reports, "scan-evidence-" + _store.Timestamp() + ".json");
            CleanerEngine.WriteJson(path, new ScanEvidenceReport
            {
                ScannedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProductVersion = AppMeta.Version,
                FindingCount = _allFindings.Count,
                WarningCount = _scanner.Warnings.Count,
                Findings = _allFindings,
                Warnings = _scanner.Warnings
            });
            var dialog = new ContentDialog
            {
                Title = "证据报告已导出",
                Content = new TextBlock
                {
                    Text = "报告文件：\n" + path + "\n\n包含全部扫描发现与证据，可用于人工复核或交给管理员处理。",
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "打开所在文件夹",
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                try { Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + path + "\"", UseShellExecute = true }); } catch { }
            }
        }
        catch (Exception ex)
        {
            await ShowInfo("导出失败", ex.Message);
        }
    }

    #endregion

    #region 结果列表与详情

    private void RenderFindings()
    {
        _suppressRender = true;
        List<Finding> list;
        try
        {
            list = FilteredFindings();
            bool allMode = _filter == "startup" && _startupAllMode;
            ResultsList.Visibility = allMode ? Visibility.Collapsed : Visibility.Visible;
            StartupAllList.Visibility = allMode ? Visibility.Visible : Visibility.Collapsed;
            ShowAllStartupBtn.Visibility = _filter == "startup" && !allMode ? Visibility.Visible : Visibility.Collapsed;
            BackToFindingsBtn.Visibility = allMode ? Visibility.Visible : Visibility.Collapsed;

            if (!allMode)
            {
                ResultsList.ItemsSource = null;
                ResultsList.ItemsSource = list;
                FilterTitle.Text = _findingsAllMode ? "全部发现" : _filter switch
                {
                    "startup" => "开机启动相关",
                    "popup" => "弹窗与守护进程诊断",
                    _ => "全部发现"
                };
                RefreshCountText();
            }
            else
            {
                FilterTitle.Text = "全部启动项（只读，仅核对）";
                CountText.Text = "";
                CleanBtn.IsEnabled = false;
            }
            UpdateStatCards();
            ReportBtn.IsEnabled = _allFindings.Count > 0;

            if (list.Count == 0 && !allMode)
            {
                EmptyPanel.Visibility = Visibility.Visible;
                if (!_hasScanned)
                {
                    EmptyText.Text = "正在扫描…";
                }
                else if (_findingsAllMode)
                {
                    EmptyText.Text = "扫描完成，未发现可疑项。";
                }
                else if (_filter == "startup")
                {
                    EmptyText.Text = "扫描完成，未发现可疑启动项。\n可点击上方「查看全部启动项（只读）」核对全部开机启动项。";
                }
                else if (_filter == "popup")
                {
                    EmptyText.Text = "扫描完成，未发现可疑的弹窗与守护进程。";
                }
                else
                {
                    EmptyText.Text = "扫描完成，未发现可疑项。";
                }
            }
            else
            {
                EmptyPanel.Visibility = Visibility.Collapsed;
            }
            UpdateFindingsFooter();
        }
        finally
        {
            _suppressRender = false;
        }
        if (!_startupAllMode && ResultsList.SelectedItem is null && list.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            RenderDetail(ResultsList.SelectedItem as Finding);
        }
        else
        {
            RenderDetail(ResultsList.SelectedItem as Finding);
        }
    }

    // 列表底部「展示全部」的提示与切换按钮；总览 tab 已显示全部，不显示
    private void UpdateFindingsFooter()
    {
        if (_filter is not ("startup" or "popup"))
        {
            FindingsFooter.Visibility = Visibility.Collapsed;
            return;
        }
        int filtered = _allFindings.Count(_filter == "startup"
            ? RogueCleanerViewFilters.MatchesStartupTab
            : RogueCleanerViewFilters.MatchesPopupTab);
        int hidden = _allFindings.Count - filtered;
        if (_findingsAllMode)
        {
            FindingsHiddenHint.Text = "已显示全部 " + _allFindings.Count + " 项";
            FindingsToggleBtn.Content = "仅显示筛选结果";
            FindingsFooter.Visibility = Visibility.Visible;
        }
        else
        {
            FindingsHiddenHint.Text = "另有 " + hidden + " 项被当前筛选器隐藏";
            FindingsToggleBtn.Content = "展示全部 " + _allFindings.Count + " 项";
            FindingsFooter.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RefreshCountText()
    {
        var list = FilteredFindings();
        int selected = list.Count(f => f.Selected && f.CanClean);
        CountText.Text = $"共 {list.Count} 项 · 已勾选 {selected} 项";
        CleanBtn.IsEnabled = !_scanning && selected > 0;
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRender) return;
        RenderDetail(ResultsList.SelectedItem as Finding);
    }

    private void RenderDetail(Finding? f)
    {
        DetailPanel.Children.Clear();
        if (f is null)
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = "在左侧选择一项查看详情。",
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        AddDetailRow("项目", f.UserVisibleName, true);
        AddDetailRow("风险", f.RiskDisplay + (string.IsNullOrWhiteSpace(f.Status) ? "" : " · " + f.Status));
        AddDetailRow("软件", string.IsNullOrWhiteSpace(f.SoftwareName) ? f.Vendor ?? "来源未确认" : f.SoftwareName + (string.IsNullOrWhiteSpace(f.Vendor) ? "" : "（" + f.Vendor + "）"));
        if (!string.IsNullOrWhiteSpace(f.IdentityExplanation)) AddDetailRow("身份依据", f.IdentityExplanation);
        AddDetailRow("位置", f.TechnicalLocation);
        AddDetailRow("影响", f.UserImpact);
        AddDetailRow("处理方式", f.ActionText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        var copy = new Button { Content = "复制详情" };
        copy.Click += (_, _) => CopyFindingDetail(f);
        buttons.Children.Add(copy);
        var wl = new Button { Content = "加入白名单" };
        wl.Click += async (_, _) => { UserWhitelistStore.Add(_store, f); await ReloadWhitelistState(); };
        buttons.Children.Add(wl);
        var fb = new Button { Content = "反馈" };
        fb.Click += async (_, _) => await ShowFeedbackDialog(f);
        buttons.Children.Add(fb);
        DetailPanel.Children.Add(buttons);
    }

    private void AddDetailRow(string label, string? value, bool bold = false)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        grid.Children.Add(new TextBlock
        {
            Text = value ?? "",
            FontSize = bold ? 14 : 12,
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });
        Grid.SetColumn((FrameworkElement)grid.Children[1], 1);
        DetailPanel.Children.Add(grid);
    }

    private void CopyFindingDetail(Finding f)
    {
        var text = $"项目：{f.UserVisibleName}\n风险：{f.RiskDisplay}\n软件：{f.SoftwareName}\n位置：{f.TechnicalLocation}\n影响：{f.UserImpact}\n处理方式：{f.ActionText}\n证据：{f.Evidence}";
        try
        {
            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
            Clipboard.Flush();
        }
        catch { }
    }

    #endregion

    #region 软件图标（原版结果行展示软件图标）

    private void HydrateFindingIcons()
    {
        if (_allFindings.Count == 0) return;
        SoftwarePresentationQueue.Hydrate(DispatcherQueue, _allFindings, () => _ = ConvertFindingIconsAsync());
    }

    private async Task ConvertFindingIconsAsync()
    {
        bool changed = false;
        foreach (var f in _allFindings)
        {
            if (_findingIcons.TryGetValue(f.Id, out var cached))
            {
                f.IconDisplay = cached;
                changed = true;
                continue;
            }
            if (f.SoftwareIcon == null) continue;
            var bmp = await ToBitmapImageAsync(f.SoftwareIcon);
            if (bmp != null)
            {
                _findingIcons[f.Id] = bmp;
                f.IconDisplay = bmp;
                changed = true;
            }
        }
        if (changed) RenderFindings();
    }

    private async Task ConvertMenuIconsAsync()
    {
        bool changed = false;
        foreach (var e in _cmEntries) changed |= await SetMenuIconAsync(e.Id, e.SoftwareIcon, v => e.IconDisplay = v);
        foreach (var e in _specialEntries) changed |= await SetMenuIconAsync(e.Id, e.SoftwareIcon, v => e.IconDisplay = v);
        foreach (var e in _advancedEntries) changed |= await SetMenuIconAsync(e.Id, e.SoftwareIcon, v => e.IconDisplay = v);
        if (changed)
        {
            ApplyCmFilter();
            ApplySpecialFilter();
            ApplyAdvancedFilter();
        }
    }

    private async Task<bool> SetMenuIconAsync(string id, System.Drawing.Image? icon, Action<ImageSource> setter)
    {
        if (string.IsNullOrEmpty(id)) return false;
        // 刷新后条目是全新对象：缓存命中也要把图标赋给新条目并触发重新绑定
        if (_menuIcons.TryGetValue(id, out var cached))
        {
            setter(cached);
            return true;
        }
        if (icon == null) return false;
        var bmp = await ToBitmapImageAsync(icon);
        if (bmp == null) return false;
        _menuIcons[id] = bmp;
        setter(bmp);
        return true;
    }

    private static async Task<BitmapImage?> ToBitmapImageAsync(System.Drawing.Image? bitmap)
    {
        if (bitmap == null) return null;
        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bmp = new BitmapImage();
            using var ras = ms.AsRandomAccessStream();
            await bmp.SetSourceAsync(ras);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void ResultsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
    }

    private void CmList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // 主管理列表的「显示」列开关：已显示=开(绿)，已隐藏=关(灰)
        if (args.Item is ContextMenuEntry entry && args.ItemContainer.ContentTemplateRoot is FrameworkElement root)
        {
            if (root.FindName("ToggleBtn") is Button btn)
            {
                btn.Content = entry.Enabled ? "开" : "关";
                btn.Background = new SolidColorBrush(entry.Enabled ? ParseHex("#16A34A") : ParseHex("#6B7280"));
                btn.Foreground = new SolidColorBrush(ParseHex("#FFFFFF"));
                btn.IsEnabled = !entry.ReadOnly;
            }
        }
    }

    #endregion

    private void ResultsList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is Finding finding)
        {
            ResultsList.SelectedItem = finding;
            _flyoutFinding = finding;
            bool whitelisted = finding.ActionKind == "ReportOnly" && finding.Status == "已白名单";
            WLAddItem.Visibility = whitelisted ? Visibility.Collapsed : Visibility.Visible;
            WLRemoveItem.Visibility = whitelisted ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void WhitelistAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_flyoutFinding is null) return;
        bool added = UserWhitelistStore.Add(_store, _flyoutFinding);
        await ReloadWhitelistState();
        if (!added) await ShowInfo("白名单", "该项已在本地白名单中。");
    }

    private async void WhitelistRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_flyoutFinding is null) return;
        UserWhitelistStore.Remove(_store, _flyoutFinding);
        await ReloadWhitelistState();
    }

    private async Task ReloadWhitelistState()
    {
        UserWhitelistStore.Apply(_store, _allFindings);
        RenderFindings();
        if (ResultsList.SelectedItem is Finding f) RenderDetail(f);
        await Task.CompletedTask;
    }


    #region 统计卡片

    private readonly List<TextBlock> _statValueTexts = [];

    private void BuildStatCards()
    {
        StatCards.ColumnDefinitions.Clear();
        StatCards.Children.Clear();
        _statValueTexts.Clear();
        var cards = new (string label, string glyph, string color)[]
        {
            ("发现项目", "\uE9D9", "#2563EB"),
            ("建议处理", "\uE783", "#EA580C"),
            ("可管理", "\uE74D", "#16A34A"),
            ("仅提示·未知", "\uE9CE", "#6B7280")
        };
        for (int i = 0; i < cards.Length; i++)
        {
            StatCards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var value = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(ThemeColors.PrimaryText), Text = "0" };
            var label = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText), Text = cards[i].label };
            var icon = new FontIcon { Glyph = cards[i].glyph, FontSize = 18, Foreground = new SolidColorBrush(ParseHex(cards[i].color)) };
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Background = new SolidColorBrush(ThemeColors.CardBg),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                BorderThickness = new Thickness(1),
                Child = new StackPanel { Spacing = 2, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { icon, label } }, value } }
            };
            Grid.SetColumn(border, i);
            StatCards.Children.Add(border);
            _statValueTexts.Add(value);
        }
    }

    private void UpdateStatCards()
    {
        if (_statValueTexts.Count != 4) return;
        _statValueTexts[0].Text = _statFound.ToString();
        _statValueTexts[1].Text = _statSuggested.ToString();
        _statValueTexts[2].Text = _statManageable.ToString();
        _statValueTexts[3].Text = _statReportOnly.ToString();
    }

    private static Color ParseHex(string hex)
    {
        try
        {
            return Color.FromArgb(255,
                byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber));
        }
        catch
        {
            return Color.FromArgb(255, 100, 116, 139);
        }
    }

    internal static SolidColorBrush HexBrush(string hex) => new(ParseHex(hex));

    #endregion

    #region 反馈与关于

    private async void Feedback_Click(object sender, RoutedEventArgs e) => await ShowFeedbackDialog(ResultsList.SelectedItem as Finding ?? _allFindings.FirstOrDefault());

    private async void FeedbackItem_Click(object sender, RoutedEventArgs e) => await ShowFeedbackDialog(_flyoutFinding);

    private async Task ShowFeedbackDialog(Finding? finding)
    {
        if (finding is null)
        {
            await ShowInfo("反馈", "请先扫描并选择要反馈的项目。");
            return;
        }
        var types = new ComboBox
        {
            Header = "反馈类型",
            ItemsSource = new[] { "误报", "漏报", "身份错误", "关联错误" },
            SelectedIndex = 0,
            Width = 280
        };
        var expected = new TextBox { Header = "期望结果（选填）", PlaceholderText = "例如：这是正版软件，不应提示", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        var preview = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText), MaxHeight = 200 };
        void UpdatePreview()
        {
            try
            {
                var report = FeedbackService.CreateReport(finding, types.SelectedItem as string ?? "误报", expected.Text, false);
                preview.Text = FeedbackService.BuildMarkdown(report);
            }
            catch { }
        }
        types.SelectionChanged += (_, _) => UpdatePreview();
        expected.TextChanged += (_, _) => UpdatePreview();

        var panel = new StackPanel { Spacing = 10, Width = 440, Children = { types, expected, new TextBlock { Text = "预览（会自动脱敏用户名、路径、邮箱、URL、令牌）：", FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText) }, preview } };
        var dialog = new ContentDialog
        {
            Title = "反馈：" + finding.CompactTitle,
            Content = panel,
            PrimaryButtonText = "保存到本地",
            SecondaryButtonText = "打开 GitHub Issue",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        UpdatePreview();
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;
        try
        {
            var report = FeedbackService.CreateReport(finding, types.SelectedItem as string ?? "误报", expected.Text, false);
            if (result == ContentDialogResult.Primary)
            {
                var saved = FeedbackService.Save(_store, report);
                await ShowInfo("已保存到本地", "反馈已脱敏并保存：\n" + saved.MarkdownPath + "\n" + saved.JsonPath + "\n\n如需上报，可在反馈报告中打开 GitHub Issue。");
            }
            else
            {
                var url = FeedbackService.BuildIssueUrl(report);
                try
                {
                    var data = new DataPackage();
                    data.SetText(FeedbackService.BuildMarkdown(report));
                    Clipboard.SetContent(data);
                    Clipboard.Flush();
                }
                catch { }
                try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
            }
        }
        catch (Exception ex)
        {
            await ShowInfo("反馈失败", ex.Message);
        }
    }

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        var text = $"流氓软件的克星 v{AppMeta.Version}\n\n" +
            "扫描和清理 Windows 流氓右键菜单、自启动、计划任务、服务、浏览器插件和文件关联残留；" +
            "全部处理先备份，恢复中心可还原。\n\n" +
            "本工具移植自开源项目 RogueCleaner（作者 aakk007，52pojie），MIT License。\n" +
            "项目主页：https://github.com/aakk007/RogueCleaner\n" +
            "原版社区：https://www.52pojie.cn/home.php?mod=space&uid=286924\n\n" +
            "厂商识别依据本地规则库与数字签名；白名单为本地文件，不会上传任何数据。";
        var dialog = new ContentDialog
        {
            Title = "关于",
            Content = new TextBlock { Text = text, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "关闭",
            PrimaryButtonText = "打开项目主页",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try { Process.Start(new ProcessStartInfo { FileName = AppMeta.Repository, UseShellExecute = true }); } catch { }
        }
    }

    private async Task ShowInfo(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        await dialog.ShowAsync();
    }

    #endregion

    #region 右键菜单管理

    private ContextMenuInventory? _cmInventory;
    private int _cmPresentationCandidates;
    private List<ContextMenuEntry> _visibleCmEntries = [];
    private List<SpecialMenuEntry> _visibleSpecialEntries = [];
    private List<AdvancedMenuEntry> _visibleAdvancedEntries = [];

    // ---------- 视图切换（原版为三个独立窗口，这里为页面内三个视图） ----------

    private void CmSpecial_Click(object sender, RoutedEventArgs e)
    {
        CmMainView.Visibility = Visibility.Collapsed;
        CmSpecialView.Visibility = Visibility.Visible;
        InitSpecialView();
    }

    private void CmAdvanced_Click(object sender, RoutedEventArgs e)
    {
        CmMainView.Visibility = Visibility.Collapsed;
        CmAdvancedView.Visibility = Visibility.Visible;
        InitAdvancedView();
    }

    private void CmBack_Click(object sender, RoutedEventArgs e)
    {
        CmSpecialView.Visibility = Visibility.Collapsed;
        CmAdvancedView.Visibility = Visibility.Collapsed;
        CmMainView.Visibility = Visibility.Visible;
        RefreshContextMenus();
    }

    // ---------- 跨视图搜索（主列表 / 更多位置 / 系统高级） ----------

    private void CmSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _cmSearchKeyword = CmSearchBox.Text.Trim();
        CmSearchClearBtn.Visibility = _cmSearchKeyword.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // 专用/高级清单是懒加载的；搜索时后台补齐，保证跨视图计数与跳转准确
        if (_cmSearchKeyword.Length > 0 && _specialEntries.Count == 0) RefreshSpecial();
        if (_cmSearchKeyword.Length > 0 && _advancedEntries.Count == 0) RefreshAdvanced();
        if (CmSpecialView.Visibility == Visibility.Visible) ApplySpecialFilter();
        else if (CmAdvancedView.Visibility == Visibility.Visible) ApplyAdvancedFilter();
        else ApplyCmFilter();
        UpdateCmSearchHint();
    }

    private void CmSearchClear_Click(object sender, RoutedEventArgs e) => CmSearchBox.Text = string.Empty;

    private void CmJumpSpecial_Click(object sender, RoutedEventArgs e)
    {
        CmMainView.Visibility = Visibility.Collapsed;
        CmAdvancedView.Visibility = Visibility.Collapsed;
        CmSpecialView.Visibility = Visibility.Visible;
        InitSpecialView();
    }

    private void CmJumpAdvanced_Click(object sender, RoutedEventArgs e)
    {
        CmMainView.Visibility = Visibility.Collapsed;
        CmSpecialView.Visibility = Visibility.Collapsed;
        CmAdvancedView.Visibility = Visibility.Visible;
        InitAdvancedView();
    }

    // 搜索提示行：统计三个视图的匹配数，提供跨视图跳转定位
    private void UpdateCmSearchHint()
    {
        bool searching = !string.IsNullOrWhiteSpace(_cmSearchKeyword);
        CmSearchHintPanel.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        if (!searching) return;
        int mainCount = _cmInventory == null ? 0 : _cmInventory.Entries.Count(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword));
        int specialCount = _specialEntries.Count(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword));
        int advancedCount = _advancedEntries.Count(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword));
        CmSearchHintText.Text = "搜索“" + _cmSearchKeyword + "” · 主列表 " + mainCount + " 项 · 更多位置 " + specialCount + " 项 · 系统高级 " + advancedCount + " 项";
        bool inSpecial = CmSpecialView.Visibility == Visibility.Visible;
        bool inAdvanced = CmAdvancedView.Visibility == Visibility.Visible;
        CmJumpSpecialBtn.Visibility = !inSpecial && specialCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        CmJumpAdvancedBtn.Visibility = !inAdvanced && advancedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        CmJumpSpecialBtn.Content = "去更多位置查看（" + specialCount + "）";
        CmJumpAdvancedBtn.Content = "去系统高级查看（" + advancedCount + "）";
    }

    // ---------- 主管理视图（对应原版 ContextMenuManagerForm） ----------

    private void CmRefresh_Click(object sender, RoutedEventArgs e) => RefreshContextMenus();

    private void RefreshContextMenus()
    {
        if (!CmRefreshBtn.IsEnabled) return;
        CmRefreshBtn.IsEnabled = false;
        CmStatusText.Text = "正在枚举当前用户、所有用户以及 32/64 位右键入口……";
        CmEmptyText.Visibility = Visibility.Visible;
        CmEmptyText.Text = "正在加载右键菜单清单…";
        Task.Run(() => new ContextMenuDiscoveryService(_store).Enumerate(true))
            .ContinueWith(t =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    CmRefreshBtn.IsEnabled = true;
                    if (t.IsFaulted)
                    {
                        Logger.Error("枚举右键菜单失败", t.Exception);
                        CmStatusText.Text = "枚举失败：" + (t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }
                    _cmInventory = t.Result;
                    _cmEntries = _cmInventory.Entries;
                    _cmAllMode = false;
                    foreach (ContextMenuEntry entry in _cmInventory.Entries)
                    {
                        entry.SoftwareIcon = null;
                        entry.SoftwareName = "正在识别…";
                        entry.PresentationResolved = false;
                        entry.IsThirdParty = false;
                    }
                    List<ContextMenuEntry> candidates = _cmInventory.Entries.Where(i => !i.AdvancedOnly).ToList();
                    _cmPresentationCandidates = candidates.Count;
                    ApplyCmFilter();
                    SoftwarePresentationQueue.Hydrate(DispatcherQueue, candidates, () => { _ = ConvertMenuIconsAsync(); ApplyCmFilter(); });
                });
            }, TaskScheduler.Default);
    }

    private void ApplyCmFilter()
    {
        if (_cmInventory == null) return;
        bool searching = !string.IsNullOrWhiteSpace(_cmSearchKeyword);
        if (searching)
        {
            // 搜索模式：从完整清单中匹配（含系统内置/未识别/技术记录），不受「仅第三方」限制
            _visibleCmEntries = _cmInventory.Entries.Where(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword)).ToList();
            CmSummaryText.Text = "搜索“" + _cmSearchKeyword + "”：主列表匹配 " + _visibleCmEntries.Count + " 项（含系统内置与未识别项）";
            CmStatusText.Text = "当前为搜索模式，按名称、软件、命令、位置或组件编号过滤；清空搜索框返回常规列表。";
        }
        else
        {
            // 显示：已识别的第三方菜单 + 用户自己添加的菜单（UserAdded 标记）；「展示全部」时含系统内置与未识别项
            _visibleCmEntries = _cmInventory.Entries.Where(e => _cmAllMode || RogueCleanerViewFilters.MatchesMainMenuList(e)).ToList();
            int resolved = _cmInventory.Entries.Count(e => !e.AdvancedOnly && e.PresentationResolved);
            int visible = _visibleCmEntries.Count;
            int enabled = _visibleCmEntries.Count(e => e.Enabled);
            int hiddenSystem = _cmInventory.Entries.Count(e => !e.AdvancedOnly && e.PresentationResolved && !e.IsThirdParty);
            int hiddenInternal = _cmInventory.Entries.Count - _cmPresentationCandidates;
            CmSummaryText.Text = "第三方菜单 " + visible + " 项  ·  已显示 " + enabled + "  ·  已隐藏 " + (visible - enabled) + "  ·  系统内置不显示";
            CmStatusText.Text = resolved < _cmPresentationCandidates
                ? "正在识别软件来源 " + resolved + " / " + _cmPresentationCandidates + "……"
                : "已隐藏 " + hiddenSystem + " 项系统菜单、" + hiddenInternal + " 项内部技术记录；" + _cmInventory.Warnings.Count + " 个受保护位置未读取。";
        }
        CmList.ItemsSource = null;
        CmList.ItemsSource = _visibleCmEntries;
        CmEmptyText.Visibility = _visibleCmEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_visibleCmEntries.Count == 0) CmEmptyText.Text = searching ? "没有找到匹配“" + _cmSearchKeyword + "”的右键菜单项目。" : "没有找到已识别的第三方右键菜单。";
        UpdateCmActions();
        ShowCmDetails();
        UpdateCmFooter();
        UpdateCmSearchHint();
    }

    // 列表底部「展示全部」的提示与切换按钮
    private void UpdateCmFooter()
    {
        // 搜索模式下列表已含全部匹配项，不再提示「展示全部」
        if (!string.IsNullOrWhiteSpace(_cmSearchKeyword))
        {
            CmFooter.Visibility = Visibility.Collapsed;
            return;
        }
        if (_cmInventory == null) return;
        int filtered = _cmInventory.Entries.Count(RogueCleanerViewFilters.MatchesMainMenuList);
        int hidden = _cmInventory.Entries.Count - filtered;
        if (_cmAllMode)
        {
            CmHiddenHint.Text = "已显示全部 " + _cmInventory.Entries.Count + " 项（含系统内置与未识别项）";
            CmToggleBtn.Content = "仅显示第三方菜单";
            CmFooter.Visibility = Visibility.Visible;
        }
        else
        {
            CmHiddenHint.Text = "另有 " + hidden + " 项被隐藏（系统内置 / 未识别 / 技术记录）";
            CmToggleBtn.Content = "展示全部 " + _cmInventory.Entries.Count + " 项";
            CmFooter.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void CmToggle_Click(object sender, RoutedEventArgs e)
    {
        _cmAllMode = !_cmAllMode;
        ApplyCmFilter();
    }

    private void CmList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowCmDetails();

    private void ShowCmDetails()
    {
        ContextMenuEntry? entry = CmList.SelectedItem as ContextMenuEntry;
        CmDetailsText.Text = entry == null
            ? "请选择一个项目。"
            : "这是什么\r\n" + entry.Name + (string.IsNullOrWhiteSpace(entry.NameReadStatus) ? string.Empty : "\r\n" + entry.NameReadStatus)
            + "\r\n\r\n属于哪个软件\r\n" + (string.IsNullOrEmpty(entry.SoftwareName) ? "来源未确认" : entry.SoftwareName) + "\r\n" + (string.IsNullOrEmpty(entry.IdentityExplanation) ? "正在识别软件来源…" : entry.IdentityExplanation)
            + "\r\n\r\n在哪里出现\r\n" + entry.Scene + "（" + entry.Scope + "）"
            + "\r\n\r\n显示或隐藏的影响\r\n" + (entry.Enabled ? "当前会显示；隐藏后只移除右键入口，不卸载对应软件。" : "当前已隐藏；显示后会恢复右键入口。")
            + "\r\n\r\n技术详情\r\n原始名称：" + (string.IsNullOrWhiteSpace(entry.RawName) ? "无" : entry.RawName)
            + "\r\n类型：" + ChineseDisplayText.ContextMenuType(entry.Type)
            + "\r\n执行命令：" + (string.IsNullOrWhiteSpace(entry.Command) ? "无" : entry.Command)
            + "\r\n组件编号：" + (string.IsNullOrWhiteSpace(entry.Clsid) ? "无" : entry.Clsid)
            + "\r\n注册表位置：" + entry.TechnicalLocation + (entry.ReadOnly ? "\r\n只读原因：" + entry.ReadOnlyReason : string.Empty);
        UpdateCmActions();
    }

    private void UpdateCmActions()
    {
        ContextMenuEntry? entry = CmList.SelectedItem as ContextMenuEntry;
        CmEnableBtn.IsEnabled = entry != null && !entry.ReadOnly && !entry.Enabled;
        CmDisableBtn.IsEnabled = entry != null && !entry.ReadOnly && entry.Enabled;
        CmEditBtn.IsEnabled = entry != null && !entry.ReadOnly
            && !string.Equals(entry.Type, "Shell 扩展", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
        CmDeleteBtn.IsEnabled = entry != null && !entry.ReadOnly
            && !string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
        CmCopyBtn.IsEnabled = entry != null;
        CmLocationBtn.IsEnabled = entry != null;
    }

    // 行内「显示」列开关点击：直接操作点击行，不依赖选中项
    private async void CmRowToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ContextMenuEntry entry && !entry.ReadOnly)
            await CmToggle(entry, !entry.Enabled);
    }

    private async void CmEnable_Click(object sender, RoutedEventArgs e) => await CmToggle(CmList.SelectedItem as ContextMenuEntry, true);

    private async void CmDisable_Click(object sender, RoutedEventArgs e) => await CmToggle(CmList.SelectedItem as ContextMenuEntry, false);

    private async Task CmToggle(ContextMenuEntry? entry, bool enabled)
    {
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator())
        {
            await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。");
            return;
        }
        var dialog = new ContentDialog
        {
            Title = "确认右键菜单操作",
            Content = new TextBlock { Text = "将“" + entry.Name + "”" + (enabled ? "启用" : "禁用") + "？\n\n工具会先保存原值，操作后可在恢复中心还原。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            new ContextMenuMutationService(_store).SetEnabled(entry, enabled);
            RefreshContextMenus();
            CmStatusText.Text = "已" + (enabled ? "启用：" : "禁用：") + entry.Name + "，恢复记录已生成。";
        }
        catch (Exception ex)
        {
            Logger.Error("修改右键菜单失败", ex);
            await ShowInfo("修改失败", ex.Message);
        }
    }

    private async void CmEdit_Click(object sender, RoutedEventArgs e) => await ShowCmEditorDialog(CmList.SelectedItem as ContextMenuEntry);

    private async void CmAdd_Click(object sender, RoutedEventArgs e) => await ShowCmEditorDialog(null);

    private async Task ShowCmEditorDialog(ContextMenuEntry? existing)
    {
        var locationCombo = new ComboBox { Header = "作用位置", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var keyNameBox = new TextBox { Header = "内部项名称", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var displayNameBox = new TextBox { Header = "显示名称", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var iconBox = new TextBox { Header = "图标", Width = 320, HorizontalAlignment = HorizontalAlignment.Left, PlaceholderText = "例如 notepad.exe,0（可留空）" };
        var commandBox = new TextBox { Header = "执行命令", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var subCommandsBox = new TextBox { Header = "子菜单引用", Width = 320, HorizontalAlignment = HorizontalAlignment.Left, PlaceholderText = "CommandStore 项名称，多个用分号分隔（可留空）" };
        var helpText = new TextBlock
        {
            Text = "普通菜单填写执行命令；级联子菜单填写 CommandStore 项名称，多个名称用分号分隔。\n图标和子菜单均可留空。添加操作默认写入当前用户，不影响其他账户。",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        };

        if (existing != null)
        {
            displayNameBox.Text = existing.Name;
            iconBox.Text = existing.Icon;
            commandBox.Text = existing.Command;
            subCommandsBox.Text = existing.SubCommands;
            int slash = existing.SubKey.LastIndexOf('\\');
            keyNameBox.Text = slash < 0 ? existing.SubKey : existing.SubKey.Substring(slash + 1);
            locationCombo.Items.Add(new LocationOption { Scene = existing.Scene, RootSubKey = slash < 0 ? existing.SubKey : existing.SubKey.Substring(0, slash) });
            locationCombo.SelectedIndex = 0;
            locationCombo.IsEnabled = false;
            keyNameBox.IsEnabled = false;
        }
        else
        {
            foreach (var scene in AllScenes()) locationCombo.Items.Add(new LocationOption { Scene = scene, RootSubKey = RootPathForScene(scene) });
            locationCombo.SelectedIndex = 0;
        }

        var panel = new StackPanel { Spacing = 8, Width = 340, Children = { locationCombo, keyNameBox, displayNameBox, iconBox, commandBox, subCommandsBox, helpText } };
        var dialog = new ContentDialog
        {
            Title = existing == null ? "添加右键菜单" : "编辑右键菜单",
            Content = new ScrollViewer { MaxHeight = 560, Content = panel },
            PrimaryButtonText = existing == null ? "添加" : "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (locationCombo.SelectedItem is not LocationOption option) { await ShowInfo("请选择作用位置", "请选择作用位置。"); return; }
        if (string.IsNullOrWhiteSpace(displayNameBox.Text)) { await ShowInfo("请输入显示名称", "请输入显示名称。"); return; }
        if (string.IsNullOrWhiteSpace(commandBox.Text) && string.IsNullOrWhiteSpace(subCommandsBox.Text)) { await ShowInfo("请填写命令或子菜单", "执行命令和子菜单引用至少填写一项。"); return; }
        try
        {
            var mutation = new ContextMenuMutationService(_store);
            if (existing == null)
                mutation.Add(option.Scene, option.RootSubKey, keyNameBox.Text, displayNameBox.Text, iconBox.Text, commandBox.Text, subCommandsBox.Text);
            else
                mutation.Edit(existing, displayNameBox.Text, iconBox.Text, commandBox.Text, subCommandsBox.Text);
            RefreshContextMenus();
            CmStatusText.Text = "已" + (existing == null ? "添加：" : "编辑：") + displayNameBox.Text + "，恢复记录已生成。";
        }
        catch (Exception ex)
        {
            Logger.Error("保存右键菜单失败", ex);
            await ShowInfo(existing == null ? "添加失败" : "编辑失败", ex.Message);
        }
    }

    private sealed class LocationOption
    {
        public string Scene { get; set; } = "";
        public string RootSubKey { get; set; } = "";
        public override string ToString() => Scene;
    }

    private static string[] AllScenes()
    {
        return new[] { "所有文件", "所有文件系统对象", "文件夹", "文件夹背景", "桌面背景", "磁盘", "文件夹对象", "快捷方式", "可执行文件", "未知文件", "命令仓库" };
    }

    private static string RootPathForScene(string scene)
    {
        return scene switch
        {
            "所有文件" => @"Software\Classes\*\shell",
            "所有文件系统对象" => @"Software\Classes\AllFilesystemObjects\shell",
            "文件夹" => @"Software\Classes\Directory\shell",
            "文件夹背景" => @"Software\Classes\Directory\Background\shell",
            "桌面背景" => @"Software\Classes\DesktopBackground\shell",
            "磁盘" => @"Software\Classes\Drive\shell",
            "文件夹对象" => @"Software\Classes\Folder\shell",
            "快捷方式" => @"Software\Classes\lnkfile\shell",
            "可执行文件" => @"Software\Classes\exefile\shell",
            "未知文件" => @"Software\Classes\Unknown\shell",
            "命令仓库" => @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell",
            _ => @"Software\Classes\*\shell"
        };
    }

    private async void CmDelete_Click(object sender, RoutedEventArgs e)
    {
        var entry = CmList.SelectedItem as ContextMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        var dialog = new ContentDialog
        {
            Title = "删除右键菜单",
            Content = new TextBlock { Text = "确定删除“" + entry.Name + "”？\n\n完整注册表结构会先进入恢复中心。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            new ContextMenuMutationService(_store).Delete(entry);
            RefreshContextMenus();
            CmStatusText.Text = "已删除：" + entry.Name + "，可在恢复中心还原。";
        }
        catch (Exception ex)
        {
            Logger.Error("删除右键菜单失败", ex);
            await ShowInfo("删除失败", ex.Message);
        }
    }

    private void CmCopy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(CmDetailsText.Text)) return;
        try
        {
            var data = new DataPackage();
            data.SetText(CmDetailsText.Text);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            CmStatusText.Text = "详情已复制到剪贴板。";
        }
        catch { }
    }

    private void CmLocation_Click(object sender, RoutedEventArgs e)
    {
        var entry = CmList.SelectedItem as ContextMenuEntry;
        if (entry == null) return;
        try
        {
            var data = new DataPackage();
            data.SetText(entry.TechnicalLocation);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            Process.Start(new ProcessStartInfo { FileName = "regedit.exe", UseShellExecute = true });
            CmStatusText.Text = "注册表位置已复制，并已打开注册表编辑器。";
        }
        catch { }
    }

    // ---------- 专用模块视图（对应原版 SpecialContextMenuForm） ----------

    private void InitSpecialView()
    {
        if (SpecialModuleCombo.Items.Count == 0)
        {
            foreach (var m in new[] { "全部模块", "新建菜单", "发送到菜单", "打开方式", "打开方式应用程序", "组件屏蔽" })
                SpecialModuleCombo.Items.Add(m);
            SpecialModuleCombo.SelectedIndex = 0;
        }
        RefreshSpecial();
    }

    private void SpecialModuleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplySpecialFilter();

    private void SpecialRefresh_Click(object sender, RoutedEventArgs e) => RefreshSpecial();

    private void RefreshSpecial()
    {
        if (!SpecialRefreshBtn.IsEnabled) return;
        SpecialRefreshBtn.IsEnabled = false;
        SpecialStatusText.Text = "正在枚举专用模块……";
        Task.Run(() => new SpecialMenuInventoryService(_store).Enumerate())
            .ContinueWith(t =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    SpecialRefreshBtn.IsEnabled = true;
                    if (t.IsFaulted)
                    {
                        Logger.Error("专用模块枚举失败", t.Exception);
                        SpecialStatusText.Text = "枚举失败：" + (t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }
                    _specialEntries = t.Result?.Entries ?? [];
                    foreach (var entry in _specialEntries) { entry.SoftwareIcon = null; entry.SoftwareName = "正在识别…"; }
                    ApplySpecialFilter();
                    SoftwarePresentationQueue.Hydrate(DispatcherQueue, _specialEntries, () => { _ = ConvertMenuIconsAsync(); ApplySpecialFilter(); });
                    SpecialStatusText.Text = "共发现 " + _specialEntries.Count + " 项；" + (t.Result?.Warnings?.Count ?? 0) + " 个位置未读取。";
                });
            }, TaskScheduler.Default);
    }

    private void ApplySpecialFilter()
    {
        bool searching = !string.IsNullOrWhiteSpace(_cmSearchKeyword);
        if (searching)
        {
            // 搜索模式：忽略模块下拉框，在全部专用模块里匹配
            _visibleSpecialEntries = _specialEntries.Where(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword)).ToList();
        }
        else
        {
            string selected = SpecialMenuDisplay.Key(Convert.ToString(SpecialModuleCombo.SelectedItem));
            _visibleSpecialEntries = _specialEntries.Where(e => selected == "全部模块" || e.Module == selected).ToList();
        }
        SpecialList.ItemsSource = null;
        SpecialList.ItemsSource = _visibleSpecialEntries;
        UpdateSpecialActions();
        UpdateCmSearchHint();
    }

    private void SpecialList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSpecialActions();

    private void UpdateSpecialActions()
    {
        var entry = SpecialList.SelectedItem as SpecialMenuEntry;
        SpecialEnableBtn.IsEnabled = entry != null && !entry.ReadOnly && !entry.Enabled;
        SpecialDisableBtn.IsEnabled = entry != null && !entry.ReadOnly && entry.Enabled;
        SpecialDeleteBtn.IsEnabled = entry != null && !entry.ReadOnly && entry.Module != "OpenWith 应用程序";
    }

    private async void SpecialEnable_Click(object sender, RoutedEventArgs e) => await SpecialToggle(true);

    private async void SpecialDisable_Click(object sender, RoutedEventArgs e) => await SpecialToggle(false);

    private async Task SpecialToggle(bool enabled)
    {
        var entry = SpecialList.SelectedItem as SpecialMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        try
        {
            new SpecialContextMenuMutationService(_store).SetEnabled(entry, enabled);
            RefreshSpecial();
        }
        catch (Exception ex) { await ShowInfo("操作失败", ex.Message); }
    }

    private async void SpecialDelete_Click(object sender, RoutedEventArgs e)
    {
        var entry = SpecialList.SelectedItem as SpecialMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        var dialog = new ContentDialog
        {
            Title = "删除专用菜单项",
            Content = new TextBlock { Text = "删除“" + entry.Name + "”？操作前会备份。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            new SpecialContextMenuMutationService(_store).Delete(entry);
            RefreshSpecial();
        }
        catch (Exception ex) { await ShowInfo("删除失败", ex.Message); }
    }

    private async void SpecialAdd_Click(object sender, RoutedEventArgs e)
    {
        string selected = SpecialMenuDisplay.Key(Convert.ToString(SpecialModuleCombo.SelectedItem));
        if (selected == "全部模块" || selected == "OpenWith 应用程序")
        {
            await ShowInfo("请先选择模块", "请先选择新建菜单、发送到菜单、打开方式或组件屏蔽。");
            return;
        }
        string firstLabel = selected.StartsWith("ShellNew") || selected.StartsWith("OpenWith") ? "文件扩展名" : selected.StartsWith("SendTo") ? "显示名称" : "组件编号";
        string secondLabel = selected.StartsWith("ShellNew") ? "模板文件（可空）" : selected.StartsWith("OpenWith") ? "程序关联标识" : selected.StartsWith("SendTo") ? "目标路径" : "说明（可空）";
        var firstBox = new TextBox { Header = firstLabel, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var secondBox = new TextBox { Header = secondLabel, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var dialog = new ContentDialog
        {
            Title = "添加 " + SpecialMenuDisplay.Name(selected),
            Content = new StackPanel { Spacing = 8, Children = { firstBox, secondBox } },
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(firstBox.Text) || (!selected.StartsWith("ShellNew") && !selected.StartsWith("GUID") && string.IsNullOrWhiteSpace(secondBox.Text)))
        {
            await ShowInfo("请填写必填项", "请填写必填项。");
            return;
        }
        try
        {
            var service = new SpecialContextMenuMutationService(_store);
            if (selected == "ShellNew 新建菜单") service.AddShellNew(firstBox.Text, secondBox.Text);
            else if (selected == "SendTo 发送到") service.AddSendTo(firstBox.Text, secondBox.Text);
            else if (selected == "OpenWith 打开方式") service.AddOpenWith(firstBox.Text, secondBox.Text);
            else service.AddBlockedGuid(firstBox.Text, secondBox.Text);
            RefreshSpecial();
        }
        catch (Exception ex) { await ShowInfo("添加失败", ex.Message); }
    }

    // ---------- 高级兼容视图（对应原版 AdvancedContextMenuForm） ----------

    private void InitAdvancedView()
    {
        if (AdvancedModuleCombo.Items.Count == 0)
        {
            foreach (var m in new[] { "全部模块", "系统快捷菜单", "Windows 现代菜单", "IE 旧式菜单", "安全增强菜单" })
                AdvancedModuleCombo.Items.Add(m);
            AdvancedModuleCombo.SelectedIndex = 0;
        }
        RefreshAdvanced();
    }

    private void AdvancedModuleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyAdvancedFilter();

    private void AdvRefresh_Click(object sender, RoutedEventArgs e) => RefreshAdvanced();

    private void RefreshAdvanced()
    {
        if (!AdvRefreshBtn.IsEnabled) return;
        AdvRefreshBtn.IsEnabled = false;
        AdvancedStatusText.Text = "正在后台枚举高级菜单，不阻塞鼠标……";
        Task.Run(() => new AdvancedMenuInventoryService(_store).Enumerate())
            .ContinueWith(t =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    AdvRefreshBtn.IsEnabled = true;
                    if (t.IsFaulted)
                    {
                        Logger.Error("高级菜单枚举失败", t.Exception);
                        AdvancedStatusText.Text = "枚举失败：" + (t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }
                    _advancedEntries = t.Result?.Entries ?? [];
                    foreach (var entry in _advancedEntries) { entry.SoftwareIcon = null; entry.SoftwareName = "正在识别…"; }
                    ApplyAdvancedFilter();
                    SoftwarePresentationQueue.Hydrate(DispatcherQueue, _advancedEntries, () => { _ = ConvertMenuIconsAsync(); ApplyAdvancedFilter(); });
                    AdvancedStatusText.Text = "共发现 " + _advancedEntries.Count + " 项；" + (t.Result?.Warnings?.Count ?? 0) + " 个位置已安全跳过。现代菜单仅列出应用包清单明确声明的文件资源管理器命令。";
                });
            }, TaskScheduler.Default);
    }

    private void ApplyAdvancedFilter()
    {
        bool searching = !string.IsNullOrWhiteSpace(_cmSearchKeyword);
        if (searching)
        {
            // 搜索模式：忽略模块下拉框，在全部高级模块里匹配
            _visibleAdvancedEntries = _advancedEntries.Where(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword)).ToList();
        }
        else
        {
            string selected = AdvancedMenuDisplay.Key(Convert.ToString(AdvancedModuleCombo.SelectedItem));
            _visibleAdvancedEntries = _advancedEntries.Where(e => selected == "全部模块" || e.Module == selected).ToList();
        }
        AdvancedList.ItemsSource = null;
        AdvancedList.ItemsSource = _visibleAdvancedEntries;
        UpdateAdvancedActions();
        UpdateCmSearchHint();
    }

    private void AdvancedList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAdvancedActions();

    private void UpdateAdvancedActions()
    {
        var entry = AdvancedList.SelectedItem as AdvancedMenuEntry;
        AdvEnableBtn.IsEnabled = entry != null && !entry.ReadOnly && !entry.Enabled;
        AdvDisableBtn.IsEnabled = entry != null && !entry.ReadOnly && entry.Enabled;
        AdvEditBtn.IsEnabled = entry != null && entry.Module == "IE 旧式菜单";
        AdvDeleteBtn.IsEnabled = entry != null && (entry.Module == "WinX 快捷菜单" || entry.Module == "IE 旧式菜单" || (entry.Module == "安全增强菜单" && entry.Enabled));
        AdvUpBtn.IsEnabled = AdvDownBtn.IsEnabled = entry != null && entry.Module == "WinX 快捷菜单" && entry.Enabled;
        string selected = AdvancedMenuDisplay.Key(Convert.ToString(AdvancedModuleCombo.SelectedItem));
        AdvAddBtn.IsEnabled = selected == "全部模块" || selected == "IE 旧式菜单";
    }

    private async void AdvEnable_Click(object sender, RoutedEventArgs e) => await AdvancedToggle(true);

    private async void AdvDisable_Click(object sender, RoutedEventArgs e) => await AdvancedToggle(false);

    private async Task AdvancedToggle(bool value)
    {
        var entry = AdvancedList.SelectedItem as AdvancedMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        try
        {
            new AdvancedContextMenuMutationService(_store).SetEnabled(entry, value);
            RefreshAdvanced();
        }
        catch (Exception ex) { await ShowInfo("操作失败", ex.Message); }
    }

    private async void AdvDelete_Click(object sender, RoutedEventArgs e)
    {
        var entry = AdvancedList.SelectedItem as AdvancedMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        var dialog = new ContentDialog
        {
            Title = "高级右键兼容",
            Content = new TextBlock { Text = "删除“" + entry.Name + "”？操作前会完整备份。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            new AdvancedContextMenuMutationService(_store).Delete(entry);
            RefreshAdvanced();
        }
        catch (Exception ex) { await ShowInfo("删除失败", ex.Message); }
    }

    private async void AdvEdit_Click(object sender, RoutedEventArgs e)
    {
        var entry = AdvancedList.SelectedItem as AdvancedMenuEntry;
        if (entry != null && entry.Module == "IE 旧式菜单") await ShowIeEditorDialog(entry);
    }

    private async void AdvAdd_Click(object sender, RoutedEventArgs e) => await ShowIeEditorDialog(null);

    private async Task ShowIeEditorDialog(AdvancedMenuEntry? existing)
    {
        var nameBox = new TextBox { Header = "菜单名称", Text = existing?.Name ?? "", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var urlBox = new TextBox { Header = "脚本或页面地址", Text = existing?.Detail ?? "", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var contextsBox = new NumberBox { Header = "适用位置代码", Minimum = 0, Maximum = int.MaxValue, Value = existing?.Contexts ?? 0, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var dialog = new ContentDialog
        {
            Title = existing == null ? "添加 IE 旧式菜单" : "编辑 IE 旧式菜单",
            Content = new StackPanel { Spacing = 8, Children = { nameBox, urlBox, contextsBox } },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(nameBox.Text) || string.IsNullOrWhiteSpace(urlBox.Text))
        {
            await ShowInfo("名称和地址不能为空", "名称和地址不能为空。");
            return;
        }
        try
        {
            new AdvancedContextMenuMutationService(_store).AddOrEditIe(existing, nameBox.Text, urlBox.Text, (int)contextsBox.Value);
            RefreshAdvanced();
        }
        catch (Exception ex) { await ShowInfo("保存失败", ex.Message); }
    }

    private async void AdvUp_Click(object sender, RoutedEventArgs e) => await MoveWinX(-1);

    private async void AdvDown_Click(object sender, RoutedEventArgs e) => await MoveWinX(1);

    private async Task MoveWinX(int direction)
    {
        var entry = AdvancedList.SelectedItem as AdvancedMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        try
        {
            new AdvancedContextMenuMutationService(_store).MoveWinX(entry, direction);
            RefreshAdvanced();
        }
        catch (Exception ex) { await ShowInfo("调整失败", ex.Message); }
    }
    #endregion

    #region 恢复中心

    private void RefreshRecovery()
    {
        Task.Run(() => _cleaner.LoadBatches())
            .ContinueWith(t =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    try { _batches = t.IsFaulted ? [] : (t.Result ?? []); } catch { _batches = []; }
                    var prev = BatchList.SelectedItem;
                    BatchList.ItemsSource = null;
                    BatchList.ItemsSource = _batches;
                    if (prev is CleanupBatch batch && _batches.Any(b => b.Id == batch.Id)) BatchList.SelectedItem = batch;
                    else if (_batches.Count > 0) BatchList.SelectedIndex = 0;
                });
            }, TaskScheduler.Default);
    }

    private void BatchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var batch = BatchList.SelectedItem as CleanupBatch;
        BatchItemsList.ItemsSource = batch?.Results ?? [];
        BatchItemsTitle.Text = batch is null ? "恢复对象" : "恢复对象 · " + batch.CreatedAt + " · " + (batch.Results?.Count ?? 0) + " 项";
        RecoveryRestoreBtn.IsEnabled = batch != null;
        RecoveryDeleteBtn.IsEnabled = batch != null;
    }

    private async void RecoveryRestore_Click(object sender, RoutedEventArgs e)
    {
        var batch = BatchList.SelectedItem as CleanupBatch;
        if (batch is null) return;
        var dialog = new ContentDialog
        {
            Title = "确认恢复该批次？",
            Content = new TextBlock { Text = $"批次 {batch.CreatedAt}，共 {(batch.Results?.Count ?? 0)} 项。\n\n将还原注册表项/值、文件、服务与计划任务到处理前的状态。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var summary = await Task.Run(() => _cleaner.RestoreBatch(batch));
            var message = $"成功恢复 {summary.Succeeded} 项，失败 {summary.Failed} 项。\n\n" + string.Join("\n", summary.Messages.Take(10).ToArray());
            if (summary.Failed > 0) message += "\n\n失败的条目会保留在批次中，可稍后重试。";
            await ShowInfo(summary.AllSucceeded ? "恢复完成" : "部分恢复失败", message);
            RefreshRecovery();
        }
        catch (Exception ex)
        {
            await ShowInfo("恢复失败", ex.Message);
        }
    }

    private async void RecoveryDelete_Click(object sender, RoutedEventArgs e)
    {
        var batch = BatchList.SelectedItem as CleanupBatch;
        if (batch is null) return;
        long size = 0;
        try { size = _cleaner.GetBatchStorageBytes(batch); } catch { }
        var dialog = new ContentDialog
        {
            Title = "确认删除该批次？",
            Content = new TextBlock { Text = $"将永久删除批次 {batch.CreatedAt} 的备份数据（{(size > 0 ? FormatSize(size) : "无法计算大小")}），删除后无法恢复。\n\n建议先确认其中项目已不需要还原。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "永久删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            _cleaner.DeleteBatchRecord(batch);
            await ShowInfo("已删除", "批次备份已删除。");
            RefreshRecovery();
        }
        catch (Exception ex)
        {
            await ShowInfo("删除失败", ex.Message);
        }
    }

    private async void RecoveryPrune_Click(object sender, RoutedEventArgs e)
    {
        var old = _cleaner.FindOldBatchRecords(_batches, DateTime.Now, 20, 30);
        if (old.Count == 0)
        {
            await ShowInfo("清理旧记录", "没有超过 30 天且不在最近 20 批内的旧记录。");
            return;
        }
        var dialog = new ContentDialog
        {
            Title = "清理旧记录",
            Content = new TextBlock { Text = $"将删除 {old.Count} 个旧批次（超过 30 天且不在最近 20 批内）。删除后无法恢复。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "清理",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            foreach (var batch in old) _cleaner.DeleteBatchRecord(batch);
            await ShowInfo("已清理", $"已删除 {old.Count} 个旧批次。");
            RefreshRecovery();
        }
        catch (Exception ex)
        {
            await ShowInfo("清理失败", ex.Message);
        }
    }

    private async void RecoveryRefresh_Click(object sender, RoutedEventArgs e) => RefreshRecovery();

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
    }

    #endregion

    #region 扫描进度

    private sealed class PageProgressSink : IProgressSink
    {
        private readonly DispatcherQueue _queue;
        private readonly Action<string> _onStage;
        private readonly Action<Finding> _onFinding;

        public PageProgressSink(DispatcherQueue queue, Action<string> onStage, Action<Finding> onFinding)
        {
            _queue = queue;
            _onStage = onStage;
            _onFinding = onFinding;
        }

        public void Stage(string text)
        {
            try { _queue.TryEnqueue(() => _onStage(text)); } catch { }
        }

        public void Finding(Finding finding)
        {
            try { _queue.TryEnqueue(() => _onFinding(finding)); } catch { }
        }
    }

    #endregion
}

#region 转换器

/// <summary>风险等级 → 徽章颜色（高红/中橙/低蓝/仅提示灰）。</summary>
public sealed class RiskToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var risk = value as string;
        var color = risk switch
        {
            "高" => "#C42B1C",
            "中" => "#D97706",
            "低" => "#2563EB",
            _ => "#6B7280"
        };
        return RogueCleanerPage.HexBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>状态 → 徽章颜色。</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value as string;
        var color = status switch
        {
            "已处理" or "已启用" or "Restored" or "Done" => "#16A34A",
            "失败" or "恢复失败" or "RestoreFailed" or "Failed" => "#DC2626",
            "已打开卸载窗口" or "Launched" => "#2563EB",
            "已禁用" => "#EA580C",
            "已白名单" => "#2563EB",
            _ => "#6B7280"
        };
        return RogueCleanerPage.HexBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>恢复状态 → 中文显示。</summary>
public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => ChineseDisplayText.CleanupStatus(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>主动拦截动作 → 徽章颜色。</summary>
public sealed class ActionToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var action = value as string;
        var color = action switch
        {
            "Blocked" or "Reblocked" => "#C42B1C",
            "Allowed" or "Unblocked" => "#16A34A",
            "BlockedFailed" => "#EA580C",
            _ => "#6B7280"
        };
        return RogueCleanerPage.HexBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>字符串非空 → Visible。</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>bool → Visible。</summary>
#endregion
