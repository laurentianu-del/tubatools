using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

public sealed partial class StressTestControl : UserControl
{
    private const int ChartMaxPoints = 120;
    private const int DownsampleThreshold = 800;
    private const int MaxLogLines = 200;

    private static string FurMarkSettingsPath => Path.Combine(ConfigManager.GetDataDir(), "furmark_settings.json");
    private static string NetSettingsPath => Path.Combine(ConfigManager.GetDataDir(), "net_stress_settings.json");

    private static readonly SKColor TempC = new(248, 113, 113);
    private static readonly SKColor UsageC = new(96, 165, 250);
    private static readonly SKColor ClockC = new(251, 191, 36);
    private static readonly SKColor PowerC = new(52, 211, 153);
    private static readonly SKColor GpuTempC = new(251, 146, 60);
    private static readonly SKColor GpuClockC = new(167, 139, 250);
    private static readonly SKColor GpuPowerC = new(244, 114, 182);
    private static readonly SKColor NetTxC = new(56, 189, 248);
    private static readonly SKColor NetRxC = new(251, 113, 133);

    private readonly ObservableCollection<double> _cpuTempChart = [];
    private readonly ObservableCollection<double> _cpuUsageChart = [];
    private readonly ObservableCollection<double> _cpuClockChart = [];
    private readonly ObservableCollection<double> _cpuPowerChart = [];
    private readonly ObservableCollection<double> _gpuTempChart = [];
    private readonly ObservableCollection<double> _gpuClockChart = [];
    private readonly ObservableCollection<double> _gpuPowerChart = [];
    private readonly ObservableCollection<double> _netTxChart = [];
    private readonly ObservableCollection<double> _netRxChart = [];

    private readonly List<double> _cpuTempReport = [];
    private readonly List<double> _cpuUsageReport = [];
    private readonly List<double> _cpuClockReport = [];
    private readonly List<double> _cpuPowerReport = [];
    private readonly List<double> _gpuTempReport = [];
    private readonly List<double> _gpuClockReport = [];
    private readonly List<double> _gpuPowerReport = [];
    private readonly List<double> _netTxReport = [];
    private readonly List<double> _netRxReport = [];

    private readonly Aida64WmiReader _reader = new();

    private DispatcherTimer? _monitorTimer;
    private DispatcherTimer? _elapsedTimer;
    private DateTime _startTime;
    private int _targetMinutes;

    private Process? _aidaProcess;
    private Process? _furmarkProcess;
    private bool _weStartedAida;

    private bool _isRunning;
    private bool _wmiConnected;
    private bool _settingsReady;

    private double _cpuTempPeak, _cpuUsagePeak, _cpuClockPeak, _cpuPowerPeak;
    private double _gpuTempPeak, _gpuClockPeak, _gpuPowerPeak;
    private double _cpuTempSum, _cpuUsageSum, _cpuClockSum, _cpuPowerSum;
    private double _gpuTempSum, _gpuClockSum, _gpuPowerSum;
    private int _sampleCount;
    private int _cpuTempCount, _cpuUsageCount, _cpuClockCount, _cpuPowerCount;
    private int _gpuTempCount, _gpuClockCount, _gpuPowerCount;
    private int _monitorRetryCount;

    private NetworkStressRunner? _netRunner;
    private bool _netRunnerActive;
    private bool _netOnlyMode;
    private string _modeName = "烤机";
    private double _netTxPeak, _netRxPeak;
    private double _netTxSum, _netRxSum;
    private int _netSampleCount;
    private string _nicSpeedText = "--";
    private bool _netErrorLogged;
    private int _netHeartbeatCount;
    private long _lastNetSent, _lastNetRecv;
    private DateTime _lastNetSampleUtc;

    public event EventHandler? StressStarted;
    public event EventHandler? StressStopped;

    public Window? OwnerWindow { get; set; }

    public bool IsRunning => _isRunning;

    public StressTestControl()
    {
        InitializeComponent();
        InitCharts();
        CheckAida64Wmi();
        ActualThemeChanged += (_, _) => ApplyAidaStatusColors();
        _settingsReady = true;
        LoadFurMarkSettings();
        UpdateDxt5Availability();
        LoadNetSettings();
        UpdateNetReference();
    }

    public void Cleanup() => StopStress();

    private sealed class NetSettings
    {
        public double DataRateMbps { get; set; } = 100;
        public int DurationMinutes { get; set; } = 15;
        public int TargetModeIndex { get; set; }
    }

    private void LoadNetSettings()
    {
        try
        {
            if (File.Exists(NetSettingsPath))
            {
                var s = JsonSerializer.Deserialize<NetSettings>(File.ReadAllText(NetSettingsPath));
                if (s is not null) ApplyNetSettings(s);
            }
        }
        catch { }
    }

    private void ApplyNetSettings(NetSettings s)
    {
        if (s.DataRateMbps >= 1) NetDataRateBox.Value = Math.Clamp(s.DataRateMbps, 1, 10000);
        if (s.DurationMinutes >= 1) NetDurationBox.Value = Math.Clamp(s.DurationMinutes, 1, 1440);
        if (NetTargetModeBox.Items.Count > s.TargetModeIndex && s.TargetModeIndex >= 0) NetTargetModeBox.SelectedIndex = s.TargetModeIndex;
    }

    private void SaveNetSettings()
    {
        try
        {
            var s = new NetSettings
            {
                DataRateMbps = SafeNetDataRate(),
                DurationMinutes = SafeNetDuration(),
                TargetModeIndex = Math.Clamp(NetTargetModeBox.SelectedIndex, 0, 2),
            };
            var dir = Path.GetDirectoryName(NetSettingsPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(NetSettingsPath, JsonSerializer.Serialize(s));
        }
        catch { }
    }

    private sealed class FurMarkSettings
    {
        public int ApiIndex { get; set; }
        public int ResolutionIndex { get; set; } = 1;
        public int MsaaIndex { get; set; } = 2;
        public int VramIndex { get; set; }
        public int GpuIndex { get; set; }
        public int VsyncIndex { get; set; }
        public int BkgImgIndex { get; set; }
        public int FurImgIndex { get; set; }
        public bool Fullscreen { get; set; } = true;
        public bool BackfaceCulling { get; set; } = true;
        public bool Dxt5 { get; set; }
        public bool Osi { get; set; } = true;
        public bool GpuMonitor { get; set; } = true;
        public bool GlCoreProfile { get; set; }
    }

    private void LoadFurMarkSettings()
    {
        try
        {
            if (File.Exists(FurMarkSettingsPath))
            {
                var s = JsonSerializer.Deserialize<FurMarkSettings>(File.ReadAllText(FurMarkSettingsPath));
                if (s is not null) ApplyFurMarkSettings(s);
            }
        }
        catch { }
    }

    private void ApplyFurMarkSettings(FurMarkSettings s)
    {
        if (ApiBox.Items.Count > s.ApiIndex && s.ApiIndex >= 0) ApiBox.SelectedIndex = s.ApiIndex;
        if (ResolutionBox.Items.Count > s.ResolutionIndex && s.ResolutionIndex >= 0) ResolutionBox.SelectedIndex = s.ResolutionIndex;
        if (MsaaBox.Items.Count > s.MsaaIndex && s.MsaaIndex >= 0) MsaaBox.SelectedIndex = s.MsaaIndex;
        if (VramBox.Items.Count > s.VramIndex && s.VramIndex >= 0) VramBox.SelectedIndex = s.VramIndex;
        if (VsyncBox.Items.Count > s.VsyncIndex && s.VsyncIndex >= 0) VsyncBox.SelectedIndex = s.VsyncIndex;
        if (BkgImgBox.Items.Count > s.BkgImgIndex && s.BkgImgIndex >= 0) BkgImgBox.SelectedIndex = s.BkgImgIndex;
        if (FurImgBox.Items.Count > s.FurImgIndex && s.FurImgIndex >= 0) FurImgBox.SelectedIndex = s.FurImgIndex;
        GpuIndexBox.Value = Math.Clamp(s.GpuIndex, 0, 15);
        FullscreenToggle.IsOn = s.Fullscreen;
        BfcToggle.IsOn = s.BackfaceCulling;
        Dxt5Toggle.IsOn = s.Dxt5;
        OsiToggle.IsOn = s.Osi;
        GpumonToggle.IsOn = s.GpuMonitor;
        GlCoreToggle.IsOn = s.GlCoreProfile;
    }

    private void SaveFurMarkSettings()
    {
        try
        {
            var s = new FurMarkSettings
            {
                ApiIndex = ApiBox.SelectedIndex,
                ResolutionIndex = ResolutionBox.SelectedIndex,
                MsaaIndex = MsaaBox.SelectedIndex,
                VramIndex = VramBox.SelectedIndex,
                GpuIndex = SafeGpuIndex(),
                VsyncIndex = VsyncBox.SelectedIndex,
                BkgImgIndex = BkgImgBox.SelectedIndex,
                FurImgIndex = FurImgBox.SelectedIndex,
                Fullscreen = FullscreenToggle.IsOn,
                BackfaceCulling = BfcToggle.IsOn,
                Dxt5 = Dxt5Toggle.IsOn,
                Osi = OsiToggle.IsOn,
                GpuMonitor = GpumonToggle.IsOn,
                GlCoreProfile = GlCoreToggle.IsOn,
            };
            var dir = Path.GetDirectoryName(FurMarkSettingsPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(FurMarkSettingsPath, JsonSerializer.Serialize(s));
        }
        catch { }
    }

    private void ResetFurMarkSettings_Click(object sender, RoutedEventArgs e)
    {
        ApplyFurMarkSettings(new FurMarkSettings());
        SaveFurMarkSettings();
        Log("FurMark 参数已恢复默认");
    }

    private void FurMarkSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsReady) return;
        SaveFurMarkSettings();
        UpdateDxt5Availability();
    }
    private void FurMarkToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        SaveFurMarkSettings();
    }
    private void FurMarkNumber_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_settingsReady) return;
        SaveFurMarkSettings();
    }

    private void NetSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsReady) return;
        SaveNetSettings();
        UpdateNetReference();
    }

    private void NetSetting_NumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_settingsReady) return;
        SaveNetSettings();
        UpdateNetReference();
    }

    private void ResetNetSettings_Click(object sender, RoutedEventArgs e)
    {
        ApplyNetSettings(new NetSettings());
        SaveNetSettings();
        UpdateNetReference();
        Log("网卡烤机参数已恢复默认");
    }

    private double SafeNetDataRate() => double.IsNaN(NetDataRateBox.Value) ? 100 : Math.Clamp(NetDataRateBox.Value, 1, 10000);
    private int SafeNetDuration() => double.IsNaN(NetDurationBox.Value) ? 15 : Math.Clamp((int)NetDurationBox.Value, 1, 1440);

    private void UpdateNetReference()
    {
        if (NetRefText is null) return;
        var mbps = SafeNetDataRate();
        var minutes = SafeNetDuration();
        var totalGb = mbps * 60d * minutes / 1024d;
        var linkMbps = ParseLinkMbps(GetNicLinkSpeed());

        NetRefText.Text = $"参考指标 — 每秒 {mbps:0.#} MB/s × {minutes} 分钟 ≈ 总传输量 {totalGb:0.#} GB。"
            + (linkMbps > 0
                ? $"当前链路约 {linkMbps:0.#} Mbps（上限 {linkMbps / 8:0.#} MB/s），建议每秒数据量 ≤ {Math.Max(1, linkMbps / 8 * 0.5):0.#} MB/s。"
                : "WiFi 实际可达通常只有链路速率的 20%-50%，广播模式更慢。")
            + $"（链路上限换算：100M=12 MB/s，千兆=117 MB/s，2.5G=290 MB/s，5G=585 MB/s，10G=1170 MB/s）";
    }

    private string GetNicLinkSpeed()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderByDescending(n => n.Speed)
                .FirstOrDefault();
            if (nic is null || nic.Speed <= 0) return "未知";
            var speed = nic.Speed >= 1_000_000_000 ? $"{nic.Speed / 1e9:0.#} Gbps" : $"{nic.Speed / 1e6:0.#} Mbps";
            return $"{nic.Name} ({speed})";
        }
        catch { return "未知"; }
    }

    private static string FormatBps(double bps) => bps >= 1e9 ? $"{bps / 1e9:0.0} GB/s" : bps >= 1e6 ? $"{bps / 1e6:0.0} MB/s" : bps >= 1e3 ? $"{bps / 1e3:0.0} KB/s" : $"{bps:0} B/s";

    private static string FormatBytes(double b) => b >= 1024 * 1024 * 1024 ? $"{b / (1024 * 1024 * 1024):0.0} GB" : b >= 1024 * 1024 ? $"{b / (1024 * 1024):0.0} MB" : b >= 1024 ? $"{b / 1024:0} KB" : $"{b:0} B";

    private void UpdateDxt5Availability()
    {
        if (ApiBox is null || Dxt5Toggle is null) return;
        var isGl = ApiBox.SelectedIndex is 0 or 2;
        Dxt5Toggle.IsEnabled = isGl;
        GlCoreToggle.IsEnabled = isGl;
        if (!isGl && Dxt5Toggle.IsOn) Dxt5Toggle.IsOn = false;
        if (!isGl && GlCoreToggle.IsOn) GlCoreToggle.IsOn = false;
    }

    private int SafeGpuIndex() => double.IsNaN(GpuIndexBox.Value) ? 0 : Math.Clamp((int)GpuIndexBox.Value, 0, 15);

    private string BuildFurMarkArgs(int minutes)
    {
        static string TagOf(ComboBox box) => ((ComboBoxItem)box.SelectedItem).Tag?.ToString() ?? "";

        var api = TagOf(ApiBox);
        var args = new List<string> { $"--demo {api}" };

        if (FullscreenToggle.IsOn)
            args.Add("--fullscreen");
        else
        {
            var res = TagOf(ResolutionBox).Split(',');
            args.Add($"--width {res[0]}");
            args.Add($"--height {res[1]}");
        }

        args.Add($"--msaa {TagOf(MsaaBox)}");

        var vram = int.TryParse(TagOf(VramBox), out var v) ? v : 0;
        if (vram > 0) args.Add($"--furmark-vram-test-gb {vram}");

        var gpuIdx = SafeGpuIndex();
        if (gpuIdx > 0) args.Add($"--gpu-index {gpuIdx}");

        args.Add($"--vsync {TagOf(VsyncBox)}");

        var bkg = int.TryParse(TagOf(BkgImgBox), out var b) ? b : 0;
        if (bkg > 0) args.Add($"--furmark-bkg-img-id {bkg}");

        var fur = int.TryParse(TagOf(FurImgBox), out var f) ? f : 0;
        if (fur > 0) args.Add($"--furmark-fur-img-id {fur}");

        if (!BfcToggle.IsOn) args.Add("--furmark-bfc 0");
        if (Dxt5Toggle.IsOn && api.Contains("gl")) args.Add("--furmark-dxt5");
        if (GlCoreToggle.IsOn && api.Contains("gl")) args.Add("--opengl-core-profile 1");
        if (!OsiToggle.IsOn) args.Add("--no-osi");
        if (!GpumonToggle.IsOn) args.Add("--no-gpumon");

        args.Add($"--max-time {minutes * 60}");
        args.Add("--no-score-box");
        return string.Join(" ", args);
    }

    private void InitCharts()
    {
        var fast = TimeSpan.FromMilliseconds(150);

        CpuTempChart.Series = [MakeSeries(_cpuTempChart, TempC)];
        CpuTempChart.XAxes = [new Axis { IsVisible = false }];
        CpuTempChart.YAxes = [new Axis { IsVisible = false, MinLimit = 20, MaxLimit = 120 }];
        CpuTempChart.AnimationsSpeed = fast;
        CpuTempChart.EasingFunction = null;

        CpuUsageChart.Series = [MakeSeries(_cpuUsageChart, UsageC)];
        CpuUsageChart.XAxes = [new Axis { IsVisible = false }];
        CpuUsageChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0, MaxLimit = 100 }];
        CpuUsageChart.AnimationsSpeed = fast;
        CpuUsageChart.EasingFunction = null;

        CpuClockChart.Series = [MakeSeries(_cpuClockChart, ClockC)];
        CpuClockChart.XAxes = [new Axis { IsVisible = false }];
        CpuClockChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        CpuClockChart.AnimationsSpeed = fast;
        CpuClockChart.EasingFunction = null;

        CpuPowerChart.Series = [MakeSeries(_cpuPowerChart, PowerC)];
        CpuPowerChart.XAxes = [new Axis { IsVisible = false }];
        CpuPowerChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        CpuPowerChart.AnimationsSpeed = fast;
        CpuPowerChart.EasingFunction = null;

        GpuTempChart.Series = [MakeSeries(_gpuTempChart, GpuTempC)];
        GpuTempChart.XAxes = [new Axis { IsVisible = false }];
        GpuTempChart.YAxes = [new Axis { IsVisible = false, MinLimit = 20, MaxLimit = 120 }];
        GpuTempChart.AnimationsSpeed = fast;
        GpuTempChart.EasingFunction = null;

        GpuClockChart.Series = [MakeSeries(_gpuClockChart, GpuClockC)];
        GpuClockChart.XAxes = [new Axis { IsVisible = false }];
        GpuClockChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        GpuClockChart.AnimationsSpeed = fast;
        GpuClockChart.EasingFunction = null;

        GpuPowerChart.Series = [MakeSeries(_gpuPowerChart, GpuPowerC)];
        GpuPowerChart.XAxes = [new Axis { IsVisible = false }];
        GpuPowerChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        GpuPowerChart.AnimationsSpeed = fast;
        GpuPowerChart.EasingFunction = null;

        NetChart.Series = [MakeSeries(_netTxChart, NetTxC), MakeSeries(_netRxChart, NetRxC)];
        NetChart.XAxes = [new Axis { IsVisible = false }];
        NetChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        NetChart.AnimationsSpeed = fast;
        NetChart.EasingFunction = null;
    }

    private static LineSeries<double> MakeSeries(ObservableCollection<double> values, SKColor color)
    {
        return new LineSeries<double>
        {
            Values = values,
            Stroke = new SolidColorPaint(color) { StrokeThickness = 2.5f },
            Fill = new SolidColorPaint(new SKColor(color.Red, color.Green, color.Blue, 50)),
            GeometrySize = 0,
            LineSmoothness = 0.4,
            IsHoverable = true,
        };
    }

    private async void CheckAida64Wmi()
    {
        var (data, _) = await Task.Run(() => _reader.Read());
        if (data is not null)
        {
            _wmiConnected = true;
            AidaStatusIcon.Glyph = "\uE73E";
            AidaStatusText.Text = "AIDA64 WMI 已连接 — 实时监控已就绪";
        }
        else
        {
            _wmiConnected = false;
            AidaStatusIcon.Glyph = "\uE7BA";
            AidaStatusText.Text = "未检测到 AIDA64 WMI 数据。请启动 AIDA64 并开启：文件 → 设置 → 硬件监控 → 外部应用程序 → 允许将监测数据写入WMI。";
        }
        ApplyAidaStatusColors();
    }

    // 横幅颜色按当前主题取色，避免硬编码浅色配色在深色模式下出现"白字 + 浅色底"
    private void ApplyAidaStatusColors()
    {
        var (accentKey, backgroundKey) = _wmiConnected
            ? ("SystemFillColorSuccessBrush", "SystemFillColorSuccessBackgroundBrush")
            : ("SystemFillColorCautionBrush", "SystemFillColorCautionBackgroundBrush");

        var accent = (Brush)Application.Current.Resources[accentKey];
        AidaStatusBorder.BorderBrush = accent;
        AidaStatusBorder.Background = (Brush)Application.Current.Resources[backgroundKey];
        AidaStatusIcon.Foreground = accent;
        AidaStatusText.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    private void StartStress_Click(object sender, RoutedEventArgs e) => StartStress();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopStress();

    private void TripleStress_Click(object sender, RoutedEventArgs e)
    {
        CpuSelBox.IsChecked = true;
        GpuSelBox.IsChecked = true;
        NetSelBox.IsChecked = true;
        StartStress();
    }

    private bool CpuSelected => CpuSelBox.IsChecked == true;
    private bool GpuSelected => GpuSelBox.IsChecked == true;
    private bool NetSelected => NetSelBox.IsChecked == true;

    public void StartStress(int? durationMinutes = null)
    {
        if (_isRunning) return;

        var cpuSel = CpuSelected;
        var gpuSel = GpuSelected;
        var netSel = NetSelected;
        if (!cpuSel && !gpuSel && !netSel)
        {
            Log("请至少勾选一项烤机项目（CPU / GPU / 网卡）");
            return;
        }
        _netOnlyMode = netSel && !cpuSel && !gpuSel;
        _modeName = BuildModeName(cpuSel, gpuSel, netSel);

        _isRunning = true;
        _weStartedAida = false;
        _monitorRetryCount = 0;
        _sampleCount = 0;
        _cpuTempPeak = _cpuUsagePeak = _cpuClockPeak = _cpuPowerPeak = double.MinValue;
        _gpuTempPeak = _gpuClockPeak = _gpuPowerPeak = double.MinValue;
        _cpuTempSum = _cpuUsageSum = _cpuClockSum = _cpuPowerSum = 0;
        _gpuTempSum = _gpuClockSum = _gpuPowerSum = 0;
        _cpuTempCount = _cpuUsageCount = _cpuClockCount = _cpuPowerCount = 0;
        _gpuTempCount = _gpuClockCount = _gpuPowerCount = 0;

        _cpuTempChart.Clear(); _cpuUsageChart.Clear(); _cpuClockChart.Clear(); _cpuPowerChart.Clear();
        _gpuTempChart.Clear(); _gpuClockChart.Clear(); _gpuPowerChart.Clear();
        _cpuTempReport.Clear(); _cpuUsageReport.Clear(); _cpuClockReport.Clear(); _cpuPowerReport.Clear();
        _gpuTempReport.Clear(); _gpuClockReport.Clear(); _gpuPowerReport.Clear();
        _netTxChart.Clear(); _netRxChart.Clear();
        _netTxReport.Clear(); _netRxReport.Clear();
        _netTxPeak = _netRxPeak = 0; _netTxSum = _netRxSum = 0; _netSampleCount = 0;
        _netRunnerActive = false;
        _netErrorLogged = false;
        _lastNetSent = _lastNetRecv = 0;
        _lastNetSampleUtc = DateTime.UtcNow;
        NetTxRateText.Text = NetRxRateText.Text = NetSentText.Text = "--";
        NetLinkText.Text = "--";

        SetButtonsEnabled(false);
        StopBtn.IsEnabled = true;
        ExportBtn.IsEnabled = false;

        var cpuDuration = durationMinutes ?? (int)CpuDurationBox.Value;
        var gpuDuration = durationMinutes ?? (int)GpuDurationBox.Value;
        var netDuration = durationMinutes ?? SafeNetDuration();
        _targetMinutes = Math.Max(
            Math.Max(cpuSel ? cpuDuration : 0, gpuSel ? gpuDuration : 0),
            netSel ? netDuration : 0);

        var started = new List<string>();
        if (cpuSel) { started.Add("CPU (AIDA64)"); StartAida64(cpuDuration, cpuStress: true); }
        if (gpuSel) { started.Add("GPU (FurMark)"); StartFurMarkGpuStress(gpuDuration); }
        if (netSel)
        {
            started.Add("网卡");
            if (!StartNetworkStress(netDuration) && _netOnlyMode)
            {
                _isRunning = false;
                SetButtonsEnabled(true);
                StopBtn.IsEnabled = false;
                Log("网卡烤机无法启动，本次烤机未开始");
                return;
            }
        }

        Log($"烤机开始 — {string.Join(" + ", started)}，总时长 {_targetMinutes} 分钟");

        _startTime = DateTime.UtcNow;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += ElapsedTimer_Tick;
        _elapsedTimer.Start();

        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _monitorTimer.Tick += MonitorTimer_Tick;
        _monitorTimer.Start();

        StressStarted?.Invoke(this, EventArgs.Empty);
    }

    public void StopStress()
    {
        _netRunnerActive = false;
        var netRunner = _netRunner;
        _netRunner = null;
        if (netRunner is not null)
        {
            netRunner.Stop();
            netRunner.Dispose();
            Log("网卡烤机已停止");
        }

        if (!_isRunning) return;

        _isRunning = false;
        _monitorTimer?.Stop(); _monitorTimer = null;
        _elapsedTimer?.Stop(); _elapsedTimer = null;

        KillAidaBenchmarkModule();
        KillProcess(ref _furmarkProcess);
        if (_weStartedAida) KillProcess(ref _aidaProcess);

        SetButtonsEnabled(true);
        StopBtn.IsEnabled = false;
        ExportBtn.IsEnabled = _sampleCount > 0;

        Log("烤机已停止");
        CheckAida64Wmi();
        StressStopped?.Invoke(this, EventArgs.Empty);
    }

    private void KillAidaBenchmarkModule()
    {
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var name = proc.ProcessName.ToLowerInvariant();
                if (name.Contains("aida_bench") || name.Contains("aida64.benchmark") || name.Equals("aidabench") || name.Equals("aidabench64") || name.Equals("aidabench32"))
                {
                    proc.Kill();
                    Log($"已终止烤机进程: {proc.ProcessName} (PID {proc.Id})");
                }
            }
            catch { }
        }

        if (_aidaProcess != null && !_aidaProcess.HasExited)
        {
            try
            {
                foreach (var child in Process.GetProcesses())
                {
                    try { if (child.ProcessName.ToLowerInvariant().Contains("aida")) { child.Kill(); Log($"已终止 AIDA64 相关进程: {child.ProcessName} (PID {child.Id})"); } }
                    catch { }
                }
            }
            catch { }
        }
    }

    private void ElapsedTimer_Tick(object? sender, object e)
    {
        var elapsed = DateTime.UtcNow - _startTime;
        ElapsedText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";

        UpdateNetworkStats();

        if (_targetMinutes > 0 && elapsed.TotalMinutes >= _targetMinutes)
        {
            Log($"已达到设定时长 {_targetMinutes} 分钟，自动停止");
            StopStress();
        }
    }

    private async void MonitorTimer_Tick(object? sender, object e) => await UpdateMonitorAsync();

    private async Task UpdateMonitorAsync()
    {
        if (!_isRunning) return;

        var (data, error) = await Task.Run(() => _reader.Read());

        if (data is null)
        {
            _monitorRetryCount++;
            if (!_wmiConnected)
            {
                if (_monitorRetryCount == 5) Log("等待 AIDA64 WMI 数据就绪中...");
                else if (_monitorRetryCount == 15) Log($"WMI 数据仍不可用: {error}");
            }
            else { _wmiConnected = false; CheckAida64Wmi(); }
            return;
        }

        _monitorRetryCount = 0;
        _sampleCount++;

        if (!_wmiConnected) { _wmiConnected = true; CheckAida64Wmi(); Log("AIDA64 WMI 连接成功，实时监控已启动"); }

        if (data.CpuTemp > 0) { _cpuTempSum += data.CpuTemp; _cpuTempCount++; if (data.CpuTemp > _cpuTempPeak) _cpuTempPeak = data.CpuTemp; }
        if (data.CpuUsage > 0) { _cpuUsageSum += data.CpuUsage; _cpuUsageCount++; if (data.CpuUsage > _cpuUsagePeak) _cpuUsagePeak = data.CpuUsage; }
        if (data.CpuClock > 0) { _cpuClockSum += data.CpuClock; _cpuClockCount++; if (data.CpuClock > _cpuClockPeak) _cpuClockPeak = data.CpuClock; }
        if (data.CpuPower > 0) { _cpuPowerSum += data.CpuPower; _cpuPowerCount++; if (data.CpuPower > _cpuPowerPeak) _cpuPowerPeak = data.CpuPower; }
        if (data.GpuTemp > 0) { _gpuTempSum += data.GpuTemp; _gpuTempCount++; if (data.GpuTemp > _gpuTempPeak) _gpuTempPeak = data.GpuTemp; }
        if (data.GpuClock > 0) { _gpuClockSum += data.GpuClock; _gpuClockCount++; if (data.GpuClock > _gpuClockPeak) _gpuClockPeak = data.GpuClock; }
        if (data.GpuPower > 0) { _gpuPowerSum += data.GpuPower; _gpuPowerCount++; if (data.GpuPower > _gpuPowerPeak) _gpuPowerPeak = data.GpuPower; }

        PushChart(_cpuTempChart, Val(data.CpuTemp));
        PushChart(_cpuUsageChart, Val(data.CpuUsage));
        PushChart(_cpuClockChart, Val(data.CpuClock));
        PushChart(_cpuPowerChart, Val(data.CpuPower));
        PushChart(_gpuTempChart, Val(data.GpuTemp));
        PushChart(_gpuClockChart, Val(data.GpuClock));
        PushChart(_gpuPowerChart, Val(data.GpuPower));

        PushReport(_cpuTempReport, Val(data.CpuTemp));
        PushReport(_cpuUsageReport, Val(data.CpuUsage));
        PushReport(_cpuClockReport, Val(data.CpuClock));
        PushReport(_cpuPowerReport, Val(data.CpuPower));
        PushReport(_gpuTempReport, Val(data.GpuTemp));
        PushReport(_gpuClockReport, Val(data.GpuClock));
        PushReport(_gpuPowerReport, Val(data.GpuPower));

        CpuTempText.Text = Fi(data.CpuTemp, "°C");
        CpuUsageText.Text = Fi(data.CpuUsage, "%");
        CpuClockText.Text = Fi(data.CpuClock, " MHz");
        CpuPowerText.Text = F(data.CpuPower, "W");

        GpuTempText.Text = Fi(data.GpuTemp, "°C");
        GpuClockText.Text = Fi(data.GpuClock, " MHz");
        GpuPowerText.Text = F(data.GpuPower, "W");
    }

    private static void PushChart(ObservableCollection<double> list, double value)
    {
        if (value <= 0) return;
        list.Add(value);
        if (list.Count > ChartMaxPoints) list.RemoveAt(0);
    }

    private static void PushReport(List<double> list, double value)
    {
        if (value <= 0) return;
        list.Add(value);
        if (list.Count > DownsampleThreshold)
        {
            for (int i = 1; i < list.Count - 1; i++)
                list.RemoveAt(i);
        }
    }

    private static double Val(double v) => v > 0 ? Math.Round(v, 1) : 0;

    private static string F(double v, string unit)
    {
        if (v < 0) return "--";
        var s = v.ToString("0.0", CultureInfo.InvariantCulture);
        var parts = s.Split('.');
        return parts.Length == 2 ? $"{parts[0]}.{parts[1]}{unit}" : $"{s}{unit}";
    }

    private static string Fi(double v, string unit) => v < 0 ? "--" : $"{v.ToString("0", CultureInfo.InvariantCulture)}{unit}";

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_sampleCount == 0 && _netSampleCount == 0) return;

        var elapsed = DateTime.UtcNow - _startTime;
        var modeStr = _modeName;

        var cpuTempAvg = Avg(_cpuTempSum, _cpuTempCount); var cpuUsageAvg = Avg(_cpuUsageSum, _cpuUsageCount); var cpuClockAvg = Avg(_cpuClockSum, _cpuClockCount); var cpuPowerAvg = Avg(_cpuPowerSum, _cpuPowerCount);
        var gpuTempAvg = Avg(_gpuTempSum, _gpuTempCount); var gpuClockAvg = Avg(_gpuClockSum, _gpuClockCount); var gpuPowerAvg = Avg(_gpuPowerSum, _gpuPowerCount);

        var netSentTotal = _netRunner?.BytesSent ?? 0;
        var netTxAvg = Avg(_netTxSum, _netSampleCount); var netRxAvg = Avg(_netRxSum, _netSampleCount);

        var html = GenerateReportHtml(modeStr, elapsed, cpuTempAvg, cpuUsageAvg, cpuClockAvg, cpuPowerAvg, gpuTempAvg, gpuClockAvg, gpuPowerAvg, netSentTotal, netTxAvg, netRxAvg);

        try
        {
            var win = OwnerWindow ?? App.MainWindow;
            if (win is not null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(win);
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.SuggestedFileName = $"烤机报告_{DateTime.Now:yyyyMMdd_HHmmss}";
                picker.FileTypeChoices.Add("HTML 报告", new List<string> { ".html" });

                var file = await picker.PickSaveFileAsync();
                if (file is not null)
                {
                    await Windows.Storage.FileIO.WriteTextAsync(file, html);
                    Log($"报告已导出: {file.Path}");
                    Process.Start(new ProcessStartInfo(file.Path) { UseShellExecute = true });
                    return;
                }
            }
        }
        catch { }

        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TubaWinUi3_Reports");
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, $"烤机报告_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            await File.WriteAllTextAsync(filePath, html);
            Log($"报告已导出: {filePath}");
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex2) { Log($"导出失败: {ex2.Message}"); }
    }

    private double Avg(double sum, int count) => count > 0 && sum > 0 ? sum / count : -1;

    private string GenerateReportHtml(string mode, TimeSpan elapsed,
        double cpuTempAvg, double cpuUsageAvg, double cpuClockAvg, double cpuPowerAvg,
        double gpuTempAvg, double gpuClockAvg, double gpuPowerAvg,
        double netSentTotal, double netTxAvg, double netRxAvg)
    {
        var cpuTempPeak = Peak(_cpuTempPeak); var cpuUsagePeak = Peak(_cpuUsagePeak); var cpuClockPeak = Peak(_cpuClockPeak); var cpuPowerPeak = Peak(_cpuPowerPeak);
        var gpuTempPeak = Peak(_gpuTempPeak); var gpuClockPeak = Peak(_gpuClockPeak); var gpuPowerPeak = Peak(_gpuPowerPeak);
        var netTxPeak = Peak(_netTxPeak); var netRxPeak = Peak(_netRxPeak);

        string ChartJs(string canvasId, string label, string color, string unit, List<double> data)
        {
            var vals = string.Join(",", data.Select(v => v.ToString("0.#", CultureInfo.InvariantCulture)));
            var labels = string.Join(",", Enumerable.Range(0, data.Count).Select(_ => "''"));
            return $@"
            new Chart(document.getElementById('{canvasId}'), {{
                type: 'line',
                data: {{
                    labels: [{labels}],
                    datasets: [{{
                        label: '{label}',
                        data: [{vals}],
                        borderColor: '{color}',
                        backgroundColor: '{color}26',
                        fill: true,
                        tension: 0.3,
                        pointRadius: 0,
                        borderWidth: 2
                    }}]
                }},
                options: {{
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {{
                        legend: {{ display: false }},
                        tooltip: {{
                            backgroundColor: 'rgba(32,32,32,0.9)',
                            titleFont: {{ family: 'Segoe UI', size: 12 }},
                            bodyFont: {{ family: 'Segoe UI', size: 13 }},
                            padding: 10,
                            cornerRadius: 6,
                            callbacks: {{ label: ctx => ctx.parsed.y + '{unit}' }}
                        }}
                    }},
                    scales: {{
                        x: {{ display: false }},
                        y: {{
                            grid: {{ color: 'rgba(128,128,128,0.1)' }},
                            ticks: {{ font: {{ family: 'Segoe UI', size: 10 }}, color: '#888' }}
                        }}
                    }},
                    interaction: {{ intersect: false, mode: 'index' }}
                }}
            }});";
        }

        var charts = "";
        charts += ChartJs("cpuTempChart", "CPU 温度", "#f87171", "°C", _cpuTempReport);
        charts += ChartJs("cpuUsageChart", "CPU 占用", "#60a5fa", "%", _cpuUsageReport);
        charts += ChartJs("cpuClockChart", "CPU 频率", "#fbbf24", " MHz", _cpuClockReport);
        charts += ChartJs("cpuPowerChart", "CPU 功耗", "#34d399", "W", _cpuPowerReport);
        charts += ChartJs("gpuTempChart", "GPU 温度", "#fb923c", "°C", _gpuTempReport);
        charts += ChartJs("gpuClockChart", "GPU 频率", "#a78bfa", " MHz", _gpuClockReport);
        charts += ChartJs("gpuPowerChart", "GPU 功耗", "#f472b6", "W", _gpuPowerReport);
        charts += ChartJs("netTxChart", "网卡发送", "#38bdf8", " MB/s", _netTxReport);
        charts += ChartJs("netRxChart", "网卡接收", "#fb7185", " MB/s", _netRxReport);

        string S(string title, string val, string pk, string avg, string c) =>
            $@"<div class=""stat-card""><div class=""stat-title"" style=""color:{c}"">{title}</div><div class=""stat-value"">{val}</div><div class=""stat-detail"">峰值 {pk} · 均值 {avg}</div></div>";

        return $@"<!DOCTYPE html>
<html lang=""zh-CN""><head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1.0"">
<title>烤机报告 - {mode}</title>
<script src=""https://cdn.jsdelivr.net/npm/chart.js@4""></script>
<style>
*{{margin:0;padding:0;box-sizing:border-box}}
body{{font-family:'Segoe UI','Microsoft YaHei',sans-serif;background:#f5f5f5;color:#1a1a1a;padding:32px}}
.container{{max-width:960px;margin:0 auto}}
h1{{font-size:28px;font-weight:600;margin-bottom:4px}}
.subtitle{{font-size:14px;color:#666;margin-bottom:24px}}
.info-bar{{display:flex;gap:16px;margin-bottom:24px;flex-wrap:wrap}}
.info-tag{{background:#fff;border:1px solid #e0e0e0;border-radius:8px;padding:8px 16px;font-size:13px}}
.info-tag strong{{color:#0078d4}}
.stats-grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:24px}}
.stat-card{{background:#fff;border:1px solid #e0e0e0;border-radius:8px;padding:16px;text-align:center}}
.stat-title{{font-size:12px;font-weight:600;text-transform:uppercase;letter-spacing:.5px;margin-bottom:4px}}
.stat-value{{font-size:24px;font-weight:700;margin-bottom:4px}}
.stat-detail{{font-size:11px;color:#888}}
.section{{background:#fff;border:1px solid #e0e0e0;border-radius:8px;padding:20px;margin-bottom:16px}}
.section h2{{font-size:16px;font-weight:600;margin-bottom:16px}}
.charts-grid{{display:grid;grid-template-columns:repeat(2,1fr);gap:16px}}
.chart-box{{position:relative;height:180px}}
.chart-label{{font-size:11px;color:#888;text-align:center;margin-bottom:4px}}
.footer{{text-align:center;font-size:11px;color:#aaa;margin-top:24px}}
@media(max-width:640px){{.stats-grid{{grid-template-columns:repeat(2,1fr)}}.charts-grid{{grid-template-columns:1fr}}}}
</style></head><body>
<div class=""container"">
<h1>烤机报告</h1>
<div class=""subtitle"">{mode} · 由图吧工具箱 WinUI3 生成</div>
<div class=""info-bar"">
<div class=""info-tag"">测试模式 <strong>{mode}</strong></div>
<div class=""info-tag"">运行时长 <strong>{(int)elapsed.TotalMinutes}分{elapsed.Seconds}秒</strong></div>
<div class=""info-tag"">采样次数 <strong>{_sampleCount}</strong></div>
<div class=""info-tag"">生成时间 <strong>{DateTime.Now:yyyy/MM/dd HH:mm:ss}</strong></div>
</div>
<h2 style=""font-size:16px;font-weight:600;margin-bottom:12px"">CPU 传感器</h2>
<div class=""stats-grid"">
{S("CPU 温度",Fi(cpuTempAvg,"°C"),Fi(cpuTempPeak,"°C"),Fi(cpuTempAvg,"°C"),"#f87171")}
{S("CPU 占用",Fi(cpuUsageAvg,"%"),Fi(cpuUsagePeak,"%"),Fi(cpuUsageAvg,"%"),"#60a5fa")}
{S("CPU 频率",Fi(cpuClockAvg," MHz"),Fi(cpuClockPeak," MHz"),Fi(cpuClockAvg," MHz"),"#fbbf24")}
{S("CPU 功耗",F(cpuPowerAvg,"W"),F(cpuPowerPeak,"W"),F(cpuPowerAvg,"W"),"#34d399")}
</div>
<h2 style=""font-size:16px;font-weight:600;margin-bottom:12px"">GPU 传感器</h2>
<div class=""stats-grid"">
{S("GPU 温度",Fi(gpuTempAvg,"°C"),Fi(gpuTempPeak,"°C"),Fi(gpuTempAvg,"°C"),"#fb923c")}
{S("GPU 频率",Fi(gpuClockAvg," MHz"),Fi(gpuClockPeak," MHz"),Fi(gpuClockAvg," MHz"),"#a78bfa")}
{S("GPU 功耗",F(gpuPowerAvg,"W"),F(gpuPowerPeak,"W"),F(gpuPowerAvg,"W"),"#f472b6")}
<div class=""stat-card""><div class=""stat-title"" style=""color:#888"">运行时长</div><div class=""stat-value"">{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}</div><div class=""stat-detail"">设定 {_targetMinutes} 分钟</div></div>
</div>
<div class=""section""><h2>CPU 监控曲线</h2><div class=""charts-grid"">
<div><div class=""chart-label"">温度 (°C)</div><div class=""chart-box""><canvas id=""cpuTempChart""></canvas></div></div>
<div><div class=""chart-label"">占用 (%)</div><div class=""chart-box""><canvas id=""cpuUsageChart""></canvas></div></div>
<div><div class=""chart-label"">频率 (MHz)</div><div class=""chart-box""><canvas id=""cpuClockChart""></canvas></div></div>
<div><div class=""chart-label"">功耗 (W)</div><div class=""chart-box""><canvas id=""cpuPowerChart""></canvas></div></div>
</div></div>
<div class=""section""><h2>GPU 监控曲线</h2><div class=""charts-grid"">
<div><div class=""chart-label"">温度 (°C)</div><div class=""chart-box""><canvas id=""gpuTempChart""></canvas></div></div>
<div><div class=""chart-label"">频率 (MHz)</div><div class=""chart-box""><canvas id=""gpuClockChart""></canvas></div></div>
<div><div class=""chart-label"">功耗 (W)</div><div class=""chart-box""><canvas id=""gpuPowerChart""></canvas></div></div>
</div></div>
<h2 style=""font-size:16px;font-weight:600;margin-bottom:12px"">网卡</h2>
<div class=""stats-grid"">
{S("发送峰值",F(netTxPeak/1e6," MB/s"),F(netTxPeak/1e6," MB/s"),F(netTxAvg/1e6," MB/s"),"#38bdf8")}
{S("接收峰值",F(netRxPeak/1e6," MB/s"),F(netRxPeak/1e6," MB/s"),F(netRxAvg/1e6," MB/s"),"#fb7185")}
<div class=""stat-card""><div class=""stat-title"" style=""color:#38bdf8"">发送总量</div><div class=""stat-value"">{FormatBytes(netSentTotal)}</div><div class=""stat-detail"">设定 {_targetMinutes} 分钟</div></div>
<div class=""stat-card""><div class=""stat-title"" style=""color:#888"">链路速率</div><div class=""stat-value"">{_nicSpeedText}</div><div class=""stat-detail"">发送均值 {F(netTxAvg/1e6," MB/s")} · 接收均值 {F(netRxAvg/1e6," MB/s")}</div></div>
</div>
<div class=""section""><h2>网卡监控曲线</h2><div class=""charts-grid"">
<div><div class=""chart-label"">发送速率 (MB/s)</div><div class=""chart-box""><canvas id=""netTxChart""></canvas></div></div>
<div><div class=""chart-label"">接收速率 (MB/s)</div><div class=""chart-box""><canvas id=""netRxChart""></canvas></div></div>
</div></div>
<div class=""footer"">图吧工具箱 WinUI3 · 数据来源: AIDA64 WMI · {DateTime.Now:yyyy/MM/dd}</div>
</div>
<script>{charts}</script></body></html>";
    }

    private static double Peak(double v) => v > double.MinValue ? v : 0;

    private void StartAida64(int stressMinutes, bool cpuStress)
    {
        var aidaExe = FindExecutable("aida64.exe", "aida64");
        if (aidaExe is null)
        {
            if (Process.GetProcessesByName("aida64").Length == 0 && Process.GetProcessesByName("aida64c").Length == 0)
            { Log("未找到 AIDA64，请确保已安装并放入 Tools 目录，或手动启动 AIDA64"); return; }
            Log("检测到 AIDA64 已在运行，使用现有实例"); return;
        }

        if (Process.GetProcessesByName("aida64").Length > 0 || Process.GetProcessesByName("aida64c").Length > 0)
        {
            Log("检测到 AIDA64 已在运行");
            if (cpuStress)
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = aidaExe, Arguments = $"/SST CPU /SSTDUR {stressMinutes * 60}", UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(aidaExe) });
                    Log($"已向 AIDA64 发送 CPU 稳定性测试指令 ({stressMinutes} 分钟)");
                }
                catch (Exception ex) { Log($"AIDA64 指令发送失败: {ex.Message}"); }
            }
            return;
        }

        try
        {
            var args = cpuStress ? $"/SST CPU /SSTDUR {stressMinutes * 60}" : "";
            _aidaProcess = Process.Start(new ProcessStartInfo { FileName = aidaExe, Arguments = args, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(aidaExe) });
            _weStartedAida = true;
            Log(cpuStress ? $"AIDA64 已启动 (CPU 稳定性测试, {stressMinutes} 分钟)" : "AIDA64 已启动 (仅监控模式)");
        }
        catch (Exception ex) { Log($"AIDA64 启动失败: {ex.Message}"); }
    }

    private void StartFurMarkGpuStress(int minutes)
    {
        var furmarkExe = PerformanceBenchmarkService.FindFurMarkExe();
        if (furmarkExe is null) { Log("未找到 FurMark (烤鸡工具/FurMark_win64/furmark.exe)"); return; }
        try
        {
            var arguments = BuildFurMarkArgs(minutes);
            _furmarkProcess = Process.Start(new ProcessStartInfo { FileName = furmarkExe, Arguments = arguments, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(furmarkExe) });
            Log($"FurMark 已启动 (GPU 压力测试, {minutes} 分钟)");
            Log($"FurMark 参数: {arguments}");
        }
        catch (Exception ex) { Log($"FurMark 启动失败: {ex.Message}"); }
    }

    private bool StartNetworkStress(int minutes)
    {
        if (NetworkInterface.GetAllNetworkInterfaces().All(n =>
                n.OperationalStatus != OperationalStatus.Up ||
                n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
        {
            Log("未检测到活动的网卡（有线/无线），无法进行网卡烤机");
            return false;
        }

        var mbps = SafeNetDataRate();
        var runner = new NetworkStressRunner();
        _netRunner = runner;
        _netRunnerActive = true;
        _netErrorLogged = false;
        _lastNetSent = _lastNetRecv = 0;
        _lastNetSampleUtc = DateTime.UtcNow;
        _netHeartbeatCount = 0;
        _nicSpeedText = GetNicLinkSpeed();
        NetLinkText.Text = _nicSpeedText;

        if (!runner.Start(mbps, minutes, (NetStressTarget)Math.Clamp(NetTargetModeBox.SelectedIndex, 0, 2)))
        {
            Log($"网卡烤机启动失败: {runner.ErrorMessage}");
            _netRunnerActive = false;
            return false;
        }
        Log($"网卡烤机启动 — 目标 {runner.TargetName}，每秒 {mbps:0.#} MB/s，时长 {minutes} 分钟，链路 {_nicSpeedText}");
        if (runner.IsUnicast) Log("单播模式：接收为 0 属正常（路由器不回包），本模式以发送压测为主");
        if (runner.WarningMessage is not "") Log($"网卡烤机警告: {runner.WarningMessage}");

        var linkMbps = ParseLinkMbps(_nicSpeedText);
        if (linkMbps > 0 && mbps > linkMbps / 8)
            Log($"提示：目标 {mbps:0.#} MB/s 超过链路速率上限 {linkMbps / 8:0.#} MB/s，实测将受网络本身限制（WiFi 通常只能达到链路的 20%-50%），属正常现象");
        return true;
    }

    private static double ParseLinkMbps(string nicText)
    {
        var m = System.Text.RegularExpressions.Regex.Match(nicText, @"([\d.]+)\s*(G|M)bps");
        if (!m.Success) return 0;
        var v = double.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        return m.Groups[2].Value == "G" ? v * 1000 : v;
    }

    private void UpdateNetworkStats()
    {
        if (_netRunner is null || !_netRunnerActive)
        {
            if (_netRunner is null) { NetTxRateText.Text = NetRxRateText.Text = NetSentText.Text = "--"; }
            return;
        }

        var runner = _netRunner;
        var sent = runner.BytesSent;
        var recv = runner.BytesReceived;

        var now = DateTime.UtcNow;
        var dt = (now - _lastNetSampleUtc).TotalSeconds;
        var txBps = 0d;
        var rxBps = 0d;
        if (dt >= 0.25)
        {
            txBps = (sent - _lastNetSent) / dt;
            rxBps = (recv - _lastNetRecv) / dt;
            _lastNetSent = sent;
            _lastNetRecv = recv;
            _lastNetSampleUtc = now;
        }
        if (txBps <= 0) txBps = runner.SendRateBps;
        if (rxBps <= 0) rxBps = runner.ReceiveRateBps;

        _netTxSum += txBps; _netRxSum += rxBps; _netSampleCount++;
        if (txBps > _netTxPeak) _netTxPeak = txBps;
        if (rxBps > _netRxPeak) _netRxPeak = rxBps;

        NetTxRateText.Text = FormatBps(txBps);
        NetRxRateText.Text = runner.IsUnicast ? "—" : FormatBps(rxBps);
        NetSentText.Text = FormatBytes(sent);

        PushChart(_netTxChart, txBps / 1e6);
        if (!runner.IsUnicast) PushChart(_netRxChart, rxBps / 1e6);
        PushReport(_netTxReport, txBps / 1e6);
        if (!runner.IsUnicast) PushReport(_netRxReport, rxBps / 1e6);

        if (++_netHeartbeatCount % 5 == 0)
            Log(runner.IsUnicast
                ? $"网卡烤机运行中 — 已发送 {FormatBytes(sent)}，发送 {FormatBps(txBps)}，目标 {SafeNetDataRate():0.#} MB/s"
                : $"网卡烤机运行中 — 已发送 {FormatBytes(sent)}，发送 {FormatBps(txBps)}，接收 {FormatBps(rxBps)}，目标 {SafeNetDataRate():0.#} MB/s");

        if (runner.ErrorMessage is not "" && !_netErrorLogged)
        {
            _netErrorLogged = true;
            Log($"网卡烤机异常: {runner.ErrorMessage}");
        }

        if (!runner.IsActive)
        {
            _netRunnerActive = false;
            Log(runner.ErrorMessage is not ""
                ? $"网卡烤机异常结束: {runner.ErrorMessage}"
                : $"网卡烤机完成 — {runner.FinishedReason}");

            if (_netOnlyMode)
            {
                Log("网卡烤机结束，自动停止");
                StopStress();
            }
        }
    }

    private static string? FindExecutable(params string[] names)
    {
        var toolsRoot = ToolCatalog.ToolsRoot;
        if (Directory.Exists(toolsRoot))
            foreach (var name in names) { var m = Directory.GetFiles(toolsRoot, name, SearchOption.AllDirectories); if (m.Length > 0) return m[0]; }

        foreach (var name in names)
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                if (Directory.Exists(root)) { var c = Directory.GetFiles(root, name, SearchOption.AllDirectories); if (c.Length > 0) return c[0]; }

        return null;
    }

    private static void KillProcess(ref Process? proc)
    {
        if (proc is null || proc.HasExited) return;
        try { proc.Kill(); } catch { }
        try { proc.Dispose(); } catch { }
        proc = null;
    }

    private string BuildModeName(bool cpu, bool gpu, bool net)
    {
        var parts = new List<string>();
        if (cpu) parts.Add("CPU");
        if (gpu) parts.Add("GPU");
        if (net) parts.Add("网卡");
        return parts.Count switch
        {
            3 => "一键三烤",
            0 => "烤机",
            1 => $"{parts[0]} 单烤",
            _ => $"{parts[0]}+{parts[1]} 双烤"
        };
    }

    private void SetButtonsEnabled(bool enabled)
    {
        TripleStressBtn.IsEnabled = enabled;
        CpuSelBox.IsEnabled = enabled;
        GpuSelBox.IsEnabled = enabled;
        NetSelBox.IsEnabled = enabled;
        StartStressBtn.IsEnabled = enabled;
    }

    private void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        LogText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = line + "\n" });
        while (LogText.Inlines.Count > MaxLogLines)
            LogText.Inlines.RemoveAt(0);
        LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogText.Inlines.Clear();
}
