using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SkiaSharp;
using TubaWinUi3.Services;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace TubaWinUi3.Pages;

/// <summary>
/// 原生 WinUI3 网速测试：调用浙大测速节点（speedtest.zju.edu.cn）API，
/// 支持延迟/抖动、多线程并发下载/上传，圆形仪表 + 实时速率曲线的精美界面。
/// </summary>
public sealed partial class SpeedTestPage : Page
{
    // ─── 仪表几何常量（画布 330×330，圆心 (165,150)，270° 表盘） ───
    private const double Cx = 165, Cy = 150, TrackR = 98, NeedleLen = 86;
    private const double DialStartAngle = -135.0; // 指针起始（左下）

    private readonly SpeedTestEngine _engine = new();
    private readonly Stopwatch _testSw = new();
    private readonly ObservableCollection<ObservablePoint> _dlPts = new();
    private readonly ObservableCollection<ObservablePoint> _ulPts = new();
    private LineSeries<ObservablePoint>? _dlSeries, _ulSeries;

    private CancellationTokenSource? _cts;
    private DispatcherTimer? _animTimer;
    private bool _running;
    private double _lastTickSec;

    private enum Phase { Idle, Ping, Download, Upload, Done, Stopped }
    private Phase _phase = Phase.Idle;
    private double _phaseBaseProgress;
    private double _phaseStartGlobalSec;

    private double _targetValue = double.NaN; // 动画目标读数（NaN = 显示占位）
    private double _displayValue;

    private double _pingMs = double.NaN, _jitterMs = double.NaN;
    private double _dlMbps = double.NaN, _ulMbps = double.NaN;

    // 阶段主题色
    private Color _dlColor, _ulColor, _pingColor;
    private Color _primaryColor, _textSecondary, _trackColor;

    // 仪表动态元素
    private Path? _progressArc;
    private RotateTransform? _needleRot;

    // Chip 状态跟踪（用于主题切换时重绘）
    private readonly HashSet<Border> _doneChips = new();
    private Border? _activeChip;

    public SpeedTestPage()
    {
        InitializeComponent();
    }

    // ───────────────────────────── 初始化 / 清理 ─────────────────────────────

    private void SpeedTestPage_Loaded(object sender, RoutedEventArgs e)
    {
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _animTimer.Tick += AnimTimer_Tick;
        _animTimer.Start();
        _lastTickSec = Environment.TickCount64 / 1000.0;

        ActualThemeChanged += (_, _) => RecolorUi();

        ChartInitializer.EnsureConfigured();
        InitColors();
        BuildGauge();
        InitChart();
        SetChipsIdle();
        SetButtonReady();
        ResetToIdle();

        _ = LoadPublicIpAsync();
    }

    private void SpeedTestPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _animTimer?.Stop();
        _running = false;
        _engine.Dispose();
    }

    private async Task LoadPublicIpAsync()
    {
        try
        {
            IpText.Text = "本机 IP：检测中…";
            using var ipCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var ip = await _engine.GetPublicIpAsync(ipCts.Token);
            IpText.Text = "本机 IP：" + ip;
        }
        catch
        {
            IpText.Text = "本机 IP：--";
        }
    }

    // ───────────────────────────── 颜色 / 重绘 ─────────────────────────────

    private void InitColors()
    {
        _dlColor = ColorRes("SystemFillColorSuccessBrush", Color.FromArgb(255, 22, 163, 74));
        _ulColor = Color.FromArgb(255, 139, 92, 246); // 品牌紫，两套主题下均可读
        _pingColor = ColorRes("SystemFillColorCautionBrush", Color.FromArgb(255, 234, 160, 0));
        _primaryColor = ColorRes("TextFillColorPrimaryBrush", Color.FromArgb(255, 30, 30, 30));
        _textSecondary = ColorRes("TextFillColorSecondaryBrush", Color.FromArgb(255, 90, 90, 90));
        _trackColor = ColorRes("ControlStrokeColorDefaultBrush", Color.FromArgb(255, 190, 190, 190));

        StyleStatIcon(DlIconBg, "\uE896", _dlColor);
        StyleStatIcon(UlIconBg, "\uE898", _ulColor);
        StyleStatIcon(PingIconBg, "\uE823", _pingColor);
        StyleStatIcon(JitIconBg, "\uE81E", ColorRes("SystemAccentColor", Color.FromArgb(255, 0, 120, 212)));
        DlDot.Fill = new SolidColorBrush(_dlColor);
        UlDot.Fill = new SolidColorBrush(_ulColor);
        IpIcon.Foreground = BrushRes("TextFillColorSecondaryBrush", _textSecondary);
    }

    private void RecolorUi()
    {
        InitColors();
        BuildGauge();
        RebuildChartTheme();
        ReapplyChips();
    }

    private void ResetToIdle()
    {
        _phase = Phase.Idle;
        _targetValue = double.NaN;
        _displayValue = 0;
        UnitText.Text = "Mbps";
        StageText.Text = "准备就绪";
        StatusText.Text = "点击下方按钮开始测速，完整测试约需 20~25 秒";
        PhaseBar.Value = 0;
        PctText.Text = "0%";
        ErrorBar.IsOpen = false;
        ResultBanner.Visibility = Visibility.Collapsed;
        _pingMs = _jitterMs = _dlMbps = _ulMbps = double.NaN;
        DlValue.Text = UlValue.Text = PingValue.Text = JitValue.Text = "--";
        ChartHint.Text = "开始测速后，下载 / 上传实时速率将在此绘制（Mbps）";
        ValueText.Text = "--";
    }

    // ───────────────────────────── 仪表盘绘制 ─────────────────────────────

    private void BuildGauge()
    {
        GaugeCanvas.Children.Clear();
        double dim = ActualTheme == ElementTheme.Dark ? 0.55 : 0.4;

        // 底弧轨道
        var track = new Path
        {
            Stroke = new SolidColorBrush(Color.FromArgb((byte)(dim * 255), _trackColor.R, _trackColor.G, _trackColor.B)),
            StrokeThickness = 20,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        track.Data = BuildArc(0, 1, TrackR);
        GaugeCanvas.Children.Add(track);

        // 进度弧（颜色随阶段切换）
        _progressArc = new Path
        {
            Stroke = new SolidColorBrush(_dlColor),
            StrokeThickness = 20,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            Data = null
        };
        GaugeCanvas.Children.Add(_progressArc);

        // 刻度（长/短间隔）
        for (int i = 0; i <= 8; i++)
        {
            double f = i / 8.0;
            bool major = i % 2 == 0;
            var line = new Line
            {
                X1 = PolarX(f, TrackR + 16), Y1 = PolarY(f, TrackR + 16),
                X2 = PolarX(f, TrackR + (major ? 27 : 22)), Y2 = PolarY(f, TrackR + (major ? 27 : 22)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)((dim + 0.22) * 255), _primaryColor.R, _primaryColor.G, _primaryColor.B)),
                StrokeThickness = major ? 2.6 : 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            GaugeCanvas.Children.Add(line);
        }

        // 指针
        var needle = new Line
        {
            X1 = Cx, Y1 = Cy, X2 = Cx, Y2 = Cy - NeedleLen,
            Stroke = new SolidColorBrush(_primaryColor),
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        _needleRot = new RotateTransform { CenterX = Cx, CenterY = Cy, Angle = DialStartAngle };
        needle.RenderTransform = _needleRot;
        GaugeCanvas.Children.Add(needle);

        // 中心轴：外环 + 内芯
        var hubOuter = new Ellipse
        {
            Width = 32, Height = 32,
            Fill = new SolidColorBrush(Color.FromArgb(44, _primaryColor.R, _primaryColor.G, _primaryColor.B)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(hubOuter, Cx - 16);
        Canvas.SetTop(hubOuter, Cy - 16);
        GaugeCanvas.Children.Add(hubOuter);

        var hubInner = new Ellipse
        {
            Width = 15, Height = 15,
            Fill = new SolidColorBrush(_primaryColor),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(hubInner, Cx - 7.5);
        Canvas.SetTop(hubInner, Cy - 7.5);
        GaugeCanvas.Children.Add(hubInner);
    }

    private static double PolarX(double f, double r)
        => Cx + r * Math.Cos(Math.PI * (0.75 + f * 1.5));

    private static double PolarY(double f, double r)
        => Cy + r * Math.Sin(Math.PI * (0.75 + f * 1.5));

    private static Geometry BuildArc(double f0, double f1, double radius)
    {
        var fig = new PathFigure
        {
            StartPoint = new Point(PolarX(f0, radius), PolarY(f0, radius)),
            IsClosed = false,
            IsFilled = false
        };
        fig.Segments.Add(new ArcSegment
        {
            Point = new Point(PolarX(f1, radius), PolarY(f1, radius)),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            // IsLargeArc 的语义是“扫过 >180°”，整圈 270° ⇒ 阈值 = 180/270 = 2/3。
            // 之前误用 0.5：当弧长处于 135°~180°（读数过半但未到 2/3）时被错误标记为大弧，
            // ArcSegment 会绕远路补弧 → 进度弧“飞出去”。此边界必须用 2/3。
            IsLargeArc = f1 - f0 > 2.0 / 3.0
        });
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    // ───────────────────────────── 开始 / 停止 ─────────────────────────────

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running) { StopTest(); return; }
        await RunTestAsync();
    }

    private void StopTest()
    {
        _cts?.Cancel();
        StatusText.Text = "正在停止…";
    }

    private async Task RunTestAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _running = true;
        SetButtonRunning();

        _testSw.Restart();
        ResetToIdle();
        SetChipsIdle();
        ErrorBar.IsOpen = false;
        _dlPts.Clear();
        _ulPts.Clear();

        try
        {
            // 1) 本机 IP
            StatusText.Text = "正在连接测速节点…";
            try
            {
                IpText.Text = "本机 IP：…";
                var ip = await _engine.GetPublicIpAsync(ct);
                IpText.Text = "本机 IP：" + ip;
            }
            catch (OperationCanceledException) { throw; }
            catch { IpText.Text = "本机 IP：--"; }

            // 2) 延迟 / 抖动
            _phase = Phase.Ping;
            _phaseBaseProgress = 0;
            BeginPhase("网络延迟", "ms", _pingColor, ChipPing);
            var (ping, jitter) = await _engine.MeasureLatencyAsync(
                (p, j, d, t) => EngineLive(() => OnPingLive(p, j, d, t)), ct);
            _pingMs = ping;
            _jitterMs = jitter;
            PingValue.Text = FmtValue(ping);
            JitValue.Text = FmtValue(jitter);
            SetChipDone(ChipPing, _pingColor);

            // 3) 下载
            _phase = Phase.Download;
            _phaseBaseProgress = 0.08;
            BeginPhase("下载速度", "Mbps", _dlColor, ChipDownload);
            _targetValue = 0;
            _dlMbps = await _engine.MeasureDownloadAsync(
                (m, p, s) => EngineLive(() => OnDlLive(m, p, s)), ct);
            DlValue.Text = FmtValue(_dlMbps);
            SetChipDone(ChipDownload, _dlColor);

            // 4) 上传
            _phase = Phase.Upload;
            _phaseBaseProgress = 0.65;
            BeginPhase("上传速度", "Mbps", _ulColor, ChipUpload);
            _targetValue = 0;
            _ulMbps = await _engine.MeasureUploadAsync(
                (m, p, s) => EngineLive(() => OnUlLive(m, p, s)), ct);
            UlValue.Text = FmtValue(_ulMbps);
            SetChipDone(ChipUpload, _ulColor);

            // 5) 完成
            _phase = Phase.Done;
            FinishTest();
        }
        catch (OperationCanceledException)
        {
            OnAborted("已手动停止测速", false);
        }
        catch (Exception ex)
        {
            OnAborted("测速失败：" + ex.Message, true);
        }
        finally
        {
            _running = false;
            SetButtonReady();
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>引擎回调跑在工作线程，统一切回 UI 线程安全更新。</summary>
    private void EngineLive(Action action)
    {
        if (DispatcherQueue is null) return;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            try { action(); }
            catch { /* 页面已卸载等场景直接忽略 */ }
        });
    }

    private void BeginPhase(string stage, string unit, Color accent, Border chip)
    {
        StageText.Text = stage;
        UnitText.Text = unit;
        StatusText.Text = stage + "测试进行中…";
        SetProgress(_phaseBaseProgress);
        _phaseStartGlobalSec = _testSw.Elapsed.TotalSeconds;
        _targetValue = double.NaN;
        ValueText.Text = "--";
        if (_progressArc is not null) _progressArc.Stroke = new SolidColorBrush(accent);
        SetChipActive(chip);
    }

    private void FinishTest()
    {
        double elapsed = _testSw.Elapsed.TotalSeconds;
        StatusText.Text = $"测速完成 · 总耗时 {elapsed:0} 秒";
        StageText.Text = "测速完成";
        UnitText.Text = "Mbps";
        _targetValue = double.IsNaN(_dlMbps) ? 0 : _dlMbps;
        SetProgress(1);

        var (title, color, comment) = Evaluate(_dlMbps, _pingMs);
        ResultTitleText.Text = "网络状况：" + title;
        ResultDetailText.Text =
            $"下载 {FmtValue(_dlMbps)} Mbps · 上传 {FmtValue(_ulMbps)} Mbps · 延迟 {FmtValue(_pingMs)} ms · 抖动 {FmtValue(_jitterMs)} ms";
        if (comment.Length > 0) ResultDetailText.Text += "　" + comment;
        ApplyResultBanner();
        ResultBanner.Visibility = Visibility.Visible;

        double peak = Math.Max(
            _dlPts.Count > 0 ? _dlPts.Max(s => s.Y) ?? 0 : 0,
            _ulPts.Count > 0 ? _ulPts.Max(s => s.Y) ?? 0 : 0);
        ChartHint.Text = "峰值速率 " + FmtValue(peak) + " Mbps · 图表由 LiveCharts 渲染，纵轴自动缩放";
    }

    private void ApplyResultBanner()
    {
        var (_, color, _) = Evaluate(_dlMbps, _pingMs);
        ResultTitleText.Foreground = new SolidColorBrush(color);
        ResultBanner.Background = new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B));
        ResultIcon.Foreground = new SolidColorBrush(color);
    }

    private void OnAborted(string status, bool isError)
    {
        StatusText.Text = status;
        StageText.Text = isError ? "测速失败" : "已停止";
        UnitText.Text = "Mbps";
        _targetValue = double.NaN;
        ValueText.Text = "--";
        if (_activeChip is not null) SetChipIdle(_activeChip);
        if (isError) ErrorBar.IsOpen = true;
    }

    private static (string Title, Color Color, string Comment) Evaluate(double dl, double ping)
    {
        if (double.IsNaN(dl))
            return ("无法评定", Color.FromArgb(255, 160, 160, 160), "");
        if (dl >= 800)
            return ("极速", Color.FromArgb(255, 139, 92, 246), "带宽惊人，接近万兆级网络体验");
        if (dl >= 200)
            return ("优秀", Color.FromArgb(255, 22, 163, 74), "带宽充足，4K 流媒体与大型下载毫无压力");
        if (dl >= 50)
            return ("良好", Color.FromArgb(255, 0, 120, 212), "可满足高清视频与在线游戏需求");
        if (dl >= 10)
            return ("一般", Color.FromArgb(255, 234, 160, 0), "适合网页浏览与标清视频，建议优化网络");
        return ("较差", Color.FromArgb(255, 220, 53, 69), "网络较慢，建议检查设备或联系运营商");
    }

    // ───────────────────────────── 引擎实时回调（UI 线程） ─────────────────────────────

    private void OnPingLive(double pingMs, double jitterMs, int done, int total)
    {
        if (!_running || _phase != Phase.Ping) return;
        _pingMs = pingMs;
        _jitterMs = jitterMs;
        _targetValue = pingMs;
        PingValue.Text = FmtValue(pingMs);
        JitValue.Text = FmtValue(jitterMs);
        StatusText.Text = $"正在测量延迟：第 {done}/{total} 次 · 当前中位 {FmtValue(pingMs)} ms";
        SetProgress(_phaseBaseProgress + (done / (double)total) * 0.08);
    }

    private void OnDlLive(double mbps, double progress, double seconds)
    {
        if (!_running || _phase != Phase.Download) return;
        _targetValue = mbps;
        DlValue.Text = FmtValue(mbps);
        StatusText.Text = $"下载测试中：{FmtValue(mbps)} Mbps · 4 路并发";
        SetProgress(_phaseBaseProgress + progress * 0.57);
        _dlPts.Add(new ObservablePoint(_phaseStartGlobalSec + seconds, mbps));
        TrimSeries(_dlPts);
    }

    private void OnUlLive(double mbps, double progress, double seconds)
    {
        if (!_running || _phase != Phase.Upload) return;
        _targetValue = mbps;
        UlValue.Text = FmtValue(mbps);
        StatusText.Text = $"上传测试中：{FmtValue(mbps)} Mbps · 3 路并发";
        SetProgress(_phaseBaseProgress + progress * 0.35);
        _ulPts.Add(new ObservablePoint(_phaseStartGlobalSec + seconds, mbps));
        TrimSeries(_ulPts);
    }

    private void SetProgress(double frac)
    {
        frac = Math.Clamp(frac, 0, 1);
        PhaseBar.Value = frac * 100;
        PctText.Text = (frac * 100).ToString("0") + "%";
    }

    // ───────────────────────────── 动画循环（≈30 FPS） ─────────────────────────────

    private void AnimTimer_Tick(object? sender, object e)
    {
        double now = Environment.TickCount64 / 1000.0;
        double dt = Math.Min(0.1, now - _lastTickSec);
        _lastTickSec = now;

        if (!double.IsNaN(_targetValue))
        {
            double k = 1 - Math.Exp(-dt * 7);
            _displayValue += (_targetValue - _displayValue) * k;
            if (Math.Abs(_targetValue - _displayValue) < 0.05) _displayValue = _targetValue;
        }
        else
        {
            _displayValue = 0;
        }

        double frac = ValueToFraction(_displayValue);
        if (_needleRot is not null) _needleRot.Angle = DialStartAngle + frac * 270.0;
        if (_progressArc is not null)
            _progressArc.Data = frac > 0.004 ? BuildArc(0, frac, TrackR) : null;

        string txt = double.IsNaN(_targetValue) && Math.Abs(_displayValue) < 0.01 ? "--" : FmtValue(_displayValue);
        if (ValueText.Text != txt) ValueText.Text = txt;
    }

    private double ValueToFraction(double v)
    {
        if (v <= 0) return 0;
        if (_phase == Phase.Ping) return Math.Min(1, v / 120.0); // 延迟：线性 0..120ms
        return Math.Clamp(1 - 1 / Math.Pow(1.12, Math.Sqrt(v)), 0, 1); // 速率：对数刻度
    }

    // ───────────────────────────── 阶段 Chip ─────────────────────────────

    private enum ChipState { Idle, Active, Done }

    private void SetChipsIdle()
    {
        SetChipIdle(ChipPing);
        SetChipIdle(ChipDownload);
        SetChipIdle(ChipUpload);
    }

    private void SetChipIdle(Border chip)
    {
        _doneChips.Remove(chip);
        if (_activeChip == chip) _activeChip = null;
        SetChipCore(chip, ChipState.Idle, default);
    }

    private void SetChipActive(Border chip)
    {
        _doneChips.Remove(chip);
        _activeChip = chip;
        SetChipCore(chip, ChipState.Active, default);
    }

    private void SetChipDone(Border chip, Color color)
    {
        if (_activeChip == chip) _activeChip = null;
        _doneChips.Add(chip);
        SetChipCore(chip, ChipState.Done, color);
    }

    private void ReapplyChips()
    {
        ReapplyChip(ChipPing, _pingColor);
        ReapplyChip(ChipDownload, _dlColor);
        ReapplyChip(ChipUpload, _ulColor);
    }

    private void ReapplyChip(Border chip, Color doneColor)
    {
        if (_doneChips.Contains(chip)) SetChipCore(chip, ChipState.Done, doneColor);
        else if (_activeChip == chip) SetChipCore(chip, ChipState.Active, default);
        else SetChipCore(chip, ChipState.Idle, default);
    }

    private void SetChipCore(Border chip, ChipState state, Color doneColor)
    {
        var icon = ChipIconOf(chip);
        var text = ChipTextOf(chip);
        if (icon is null || text is null) return;

        switch (state)
        {
            case ChipState.Active:
            {
                var onAccent = BrushRes("TextOnAccentFillColorPrimaryBrush", Microsoft.UI.Colors.White);
                chip.Background = BrushRes("AccentFillColorDefaultBrush", Color.FromArgb(255, 0, 120, 212));
                icon.Foreground = onAccent;
                text.Foreground = onAccent;
                icon.Glyph = OriginalGlyph(chip);
                break;
            }
            case ChipState.Done:
            {
                var white = BrushRes("TextOnAccentFillColorPrimaryBrush", Microsoft.UI.Colors.White);
                chip.Background = new SolidColorBrush(doneColor);
                icon.Foreground = white;
                text.Foreground = white;
                icon.Glyph = "\uE73E"; // 完成对勾
                break;
            }
            default:
            {
                var dim = BrushRes("TextFillColorSecondaryBrush", _textSecondary);
                chip.Background = BrushRes("SubtleFillColorSecondaryBrush", Color.FromArgb(255, 240, 240, 240));
                icon.Foreground = dim;
                text.Foreground = dim;
                icon.Glyph = OriginalGlyph(chip);
                break;
            }
        }
    }

    private string OriginalGlyph(Border chip)
        => chip == ChipPing ? "\uE823" : chip == ChipDownload ? "\uE896" : "\uE898";

    private FontIcon? ChipIconOf(Border chip) =>
        chip == ChipPing ? ChipPingIcon : chip == ChipDownload ? ChipDownloadIcon : ChipUploadIcon;

    private TextBlock? ChipTextOf(Border chip) =>
        chip == ChipPing ? ChipPingText : chip == ChipDownload ? ChipDownloadText : ChipUploadText;

    // ───────────────────────────── 实时曲线（LiveCharts2） ─────────────────────────────

    private void InitChart()
    {
        _dlSeries = new LineSeries<ObservablePoint>
        {
            Values = _dlPts,
            Stroke = new SolidColorPaint(Sk(_dlColor)) { StrokeThickness = 2.5f },
            Fill = new SolidColorPaint(SkA(_dlColor, 45)),
            GeometrySize = 0,
            LineSmoothness = 0.35,
            IsHoverable = false
        };
        _ulSeries = new LineSeries<ObservablePoint>
        {
            Values = _ulPts,
            Stroke = new SolidColorPaint(Sk(_ulColor)) { StrokeThickness = 2.5f },
            Fill = new SolidColorPaint(SkA(_ulColor, 45)),
            GeometrySize = 0,
            LineSmoothness = 0.35,
            IsHoverable = false
        };

        RateChart.Series = new ISeries[] { _dlSeries, _ulSeries };
        RateChart.AnimationsSpeed = TimeSpan.FromMilliseconds(120);
        RateChart.EasingFunction = null;
        RateChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden;

        RebuildChartTheme();
    }

    private void RebuildChartTheme()
    {
        RateChart.XAxes = new Axis[] { new Axis { IsVisible = false } };
        RateChart.YAxes = new Axis[]
        {
            new Axis
            {
                MinLimit = 0,
                Labeler = v => v >= 100 ? v.ToString("0") : v.ToString("0.#"),
                LabelsPaint = new SolidColorPaint(Sk(_textSecondary)),
                SeparatorsPaint = new SolidColorPaint(SkA(_textSecondary, 36)),
                TextSize = 10,
                ShowSeparatorLines = true,
                TicksPaint = null
            }
        };
    }

    private static void TrimSeries(ObservableCollection<ObservablePoint> pts)
    {
        if (pts.Count <= 900) return;
        for (int i = 0; i < 150; i++) pts.RemoveAt(0);
    }

    private static SKColor Sk(Color c) => new(c.R, c.G, c.B, 255);

    private static SKColor SkA(Color c, byte alpha) => new(c.R, c.G, c.B, alpha);

    // ───────────────────────────── 通用辅助 ─────────────────────────────

    private static string FmtValue(double v)
        => double.IsNaN(v) || double.IsInfinity(v) ? "--" : v < 100 ? v.ToString("0.0") : v.ToString("0");

    private void StyleStatIcon(Border bg, string glyph, Color color)
    {
        bg.Background = new SolidColorBrush(Color.FromArgb(34, color.R, color.G, color.B));
        bg.Child = new FontIcon { Glyph = glyph, FontSize = 13, Foreground = new SolidColorBrush(color) };
    }

    private void SetButtonReady()
    {
        // 原生 AccentButtonStyle：悬停/按下/禁用反馈全部交给系统
        StartButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        // 清除停止态残留的红色前景局部值，让 AccentButtonStyle 的白色文字生效
        StartButton.ClearValue(Button.ForegroundProperty);
        StartIcon.ClearValue(FontIcon.ForegroundProperty);
        StartText.ClearValue(TextBlock.ForegroundProperty);
        StartIcon.Glyph = "\uE768";
        StartText.Text = "开始测速";
    }

    private void SetButtonRunning()
    {
        // 停止态：原生默认按钮样式 + 红色文字图标（不改 Background，保留系统悬停反馈）
        StartButton.Style = null;
        var red = ColorRes("SystemFillColorCriticalBrush", Color.FromArgb(255, 220, 53, 69));
        var redBrush = new SolidColorBrush(red);
        StartButton.Foreground = redBrush;
        StartIcon.Foreground = redBrush;
        StartText.Foreground = redBrush;
        StartIcon.Glyph = "\uE71A";
        StartText.Text = "停止测速";
    }

    private static Color ColorRes(string key, Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v))
        {
            if (v is Color c) return c;
            if (v is SolidColorBrush b) return b.Color;
        }
        return fallback;
    }

    private static Brush BrushRes(string key, Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : new SolidColorBrush(fallback);

    private void BackButton_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateBack();
}
