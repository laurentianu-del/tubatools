using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Diagnostics;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed class StressTestWindow
{
    private static StressTestWindow? _instance;
    private Window? _window;

    public static void Show()
    {
        if (_instance?._window != null)
        {
            try { _instance._window.Activate(); return; } catch { }
        }
        _instance = new StressTestWindow();
        _instance.CreateWindow();
    }

    public static void CloseInstance() { _instance = null; }

    private void CreateWindow()
    {
        _window = new Window();
        _window.Title = "烤机测试";
        _window.ExtendsContentIntoTitleBar = true;

        var page = new StressTestPage(this);
        _window.Content = page;
        _window.AppWindow.Resize(new SizeInt32(1200, 800));

        try
        {
            var mainPos = App.MainWindow.AppWindow.Position;
            _window.AppWindow.Move(new PointInt32(mainPos.X + 50, mainPos.Y + 50));
        }
        catch { }

        _window.Closed += (_, _) => { page.Cleanup(); CloseInstance(); };
        _window.Activate();
    }
}

public sealed partial class StressTestPage : Page
{
    private const int MaxHistory = 120;
    private const double ChartPaddingLeft = 42;
    private const double ChartPaddingRight = 42;
    private const double ChartPaddingTop = 8;
    private const double ChartPaddingBottom = 4;

    private const int ParticleCount = 5000;
    private const int MaxOffW = 1920;
    private const int MaxOffH = 1080;
    private const int MinBlurPasses = 1;
    private const int MaxBlurPasses = 30;
    private const int MinBlendLayers = 1;
    private const int MaxBlendLayers = 20;

    private readonly StressTestService _svc = new();
    private readonly LiteMonitorService _monitor = LiteMonitorService.Instance;
    private readonly StressTestWindow _owner;

    private readonly List<float> _cpuLoadH = [], _cpuTempH = [], _cpuClockH = [], _cpuPowerH = [];
    private readonly List<float> _gpuLoadH = [], _gpuTempH = [], _gpuClockH = [], _gpuPowerH = [];

    private DispatcherTimer? _timer;
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _gpuInfoTimer;
    private readonly double[] _intervals = [0.5, 1, 2];
    private readonly int[] _durations = [1, 5, 10, 15, 30, 60];
    private Stopwatch? _stopwatch;
    private TimeSpan _maxDuration;

    private bool _gpuStressActive;
    private GpuComputeStress? _gpuCompute;

    private CanvasRenderTarget? _renderTarget;
    private CanvasSwapChain? _swapChain;
    private Thread? _render3dThread;
    private volatile bool _render3dRunning;
    private float _gpuTime;
    private StressParticle[] _particles = [];
    private readonly object _swapChainLock = new();
    private float _render3dFps;
    private long _render3dFrameTs;
    private int _render3dFrameCnt;

    private volatile float _gpuLoad3d;
    private int _currentBlurPasses = 4;
    private int _currentBlendLayers = 4;
    private long _lastAdaptTs;
    private const int AdaptIntervalSec = 2;

    private static readonly Color LoadColor = Color.FromArgb(255, 66, 133, 244);
    private static readonly Color TempColor = Color.FromArgb(255, 255, 107, 53);
    private static readonly Color ClockColor = Color.FromArgb(255, 52, 168, 83);
    private static readonly Color PowerColor = Color.FromArgb(255, 123, 31, 162);
    private static readonly Color CpuAccent = Color.FromArgb(255, 66, 133, 244);
    private static readonly Color GpuAccent = Color.FromArgb(255, 234, 67, 53);

    public StressTestPage(StressTestWindow owner)
    {
        _owner = owner;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => ResizeChartCanvases();

    private void ResizeChartCanvases()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var w = CpuChartCanvas.Parent is FrameworkElement fe ? fe.ActualWidth : CpuChartCanvas.ActualWidth;
            if (w <= 0) w = 500;
            CpuChartCanvas.Width = w;
            GpuChartCanvas.Width = w;
        });
    }

    #region Monitor & Controls

    private void StartMonitor()
    {
        _stopwatch = Stopwatch.StartNew();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_intervals[IntervalCombo.SelectedIndex]) };
        _timer.Tick += OnTick;
        _timer.Start();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        IntervalCombo.SelectionChanged += (_, _) =>
        {
            if (_timer != null) _timer.Interval = TimeSpan.FromSeconds(_intervals[IntervalCombo.SelectedIndex]);
        };
    }

    private void StopMonitor()
    {
        _timer?.Stop(); _timer = null;
        _clockTimer?.Stop(); _clockTimer = null;
        _stopwatch?.Stop();
    }

    private void UpdateClock()
    {
        if (_stopwatch == null) return;
        var ts = _stopwatch.Elapsed;
        TimerText.Text = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";

        var remain = _maxDuration - ts;
        if (remain <= TimeSpan.Zero)
        {
            RemainText.Text = "超时停止";
            DispatcherQueue.TryEnqueue(() => StopStress());
            return;
        }
        RemainText.Text = $"-{(int)remain.TotalHours:D2}:{remain.Minutes:D2}:{remain.Seconds:D2}";
    }

    private volatile bool _reading;

    private async void OnTick(object? sender, object e)
    {
        if (_reading) return;
        _reading = true;
        try
        {
            var sample = await Task.Run(() => _monitor.Read(false));
            DispatcherQueue.TryEnqueue(() => UpdateUI(sample));
        }
        catch { }
        finally { _reading = false; }
    }

    private void UpdateUI(MonitorSample s)
    {
        CpuLoadText.Text = s.CpuLoad >= 0 ? $"{s.CpuLoad:0}%" : "--";
        CpuTempText.Text = s.CpuTemp >= 0 ? $"{s.CpuTemp:0}°C" : "--";
        CpuClockText.Text = s.CpuClock > 0 ? $"{s.CpuClock / 1000f:0.0}GHz" : "--";
        CpuPowerText.Text = s.CpuPower > 0 ? $"{s.CpuPower:0.0}W" : "--";

        GpuLoadText.Text = s.GpuLoad >= 0 ? $"{s.GpuLoad:0}%" : "--";
        GpuTempText.Text = s.GpuTemp >= 0 ? $"{s.GpuTemp:0}°C" : "--";
        GpuClockText.Text = s.GpuClock > 0 ? $"{s.GpuClock:0}MHz" : "--";
        GpuPowerText.Text = s.GpuPower > 0 ? $"{s.GpuPower:0.0}W" : "--";

        if (s.CpuTemp >= 90 || s.GpuTemp >= 90)
        {
            MaxTempWarning.Text = "\u26A0 高温警告！";
            MaxTempWarning.Foreground = new SolidColorBrush(Colors.Red);
        }
        else if (s.CpuTemp >= 80 || s.GpuTemp >= 80)
        {
            MaxTempWarning.Text = "温度偏高";
            MaxTempWarning.Foreground = new SolidColorBrush(TempColor);
        }
        else MaxTempWarning.Text = "";

        AddH(_cpuLoadH, s.CpuLoad); AddH(_cpuTempH, s.CpuTemp);
        AddH(_cpuClockH, s.CpuClock > 0 ? s.CpuClock / 1000f : -1); AddH(_cpuPowerH, s.CpuPower);
        AddH(_gpuLoadH, s.GpuLoad); AddH(_gpuTempH, s.GpuTemp);
        AddH(_gpuClockH, s.GpuClock > 0 ? s.GpuClock : -1); AddH(_gpuPowerH, s.GpuPower);

        if (_gpuCompute != null) _gpuCompute.SetGpuLoad(s.GpuLoad);
        _gpuLoad3d = s.GpuLoad;

        ResizeChartCanvases();
        DrawChart(CpuChartCanvas, _cpuLoadH, _cpuTempH, _cpuClockH, _cpuPowerH);
        DrawChart(GpuChartCanvas, _gpuLoadH, _gpuTempH, _gpuClockH, _gpuPowerH);
    }

    private static void AddH(List<float> h, float v)
    {
        if (v < 0) return;
        h.Add(v);
        if (h.Count > MaxHistory) h.RemoveAt(0);
    }

    private void CpuBtn_Click(object sender, RoutedEventArgs e) => StartStress(false, true);
    private void GpuBtn_Click(object sender, RoutedEventArgs e) => StartStress(true, false);
    private void BothBtn_Click(object sender, RoutedEventArgs e) => StartStress(true, true);

    private void StartStress(bool gpu, bool cpu)
    {
        StopStress();

        _maxDuration = TimeSpan.FromMinutes(_durations[DurationCombo.SelectedIndex]);

        if (cpu) _svc.StartCpuStress();
        if (gpu)
        {
            _gpuStressActive = true;
            _gpuTime = 0;
            _render3dFrameCnt = 0;
            _render3dFrameTs = Stopwatch.GetTimestamp();
            _render3dFps = 0;
            _currentBlurPasses = 4;
            _currentBlendLayers = 4;
            PreviewBorder.Visibility = Visibility.Visible;

            InitParticles();

            try
            {
                _gpuCompute = new GpuComputeStress();
                _gpuCompute.Initialize();
                _gpuCompute.Start();
            }
            catch (Exception ex)
            {
                _gpuCompute?.Dispose();
                _gpuCompute = null;
            }

            Start3dRenderThread();

            _gpuInfoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _gpuInfoTimer.Tick += (_, _) =>
            {
                var gflops = _gpuCompute?.GetGflops() ?? 0;
                PreviewFpsLabel.Text = $"DirectX 11 | 3D:{_render3dFps:0}FPS | CS:{gflops:0.0}GF | Blur:{_currentBlurPasses} Blend:{_currentBlendLayers}";
                FpsText.Text = $"DX11 3D:{_render3dFps:0}FPS CS:{gflops:0.0}GF | Blur×{_currentBlurPasses} Blend×{_currentBlendLayers}";
            };
            _gpuInfoTimer.Start();
        }

        if (cpu && gpu)
        {
            StatusText.Text = "双烤运行中";
            StatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 234, 67, 53));
        }
        else if (cpu)
        {
            StatusText.Text = "CPU 单烤运行中";
            StatusDot.Fill = new SolidColorBrush(CpuAccent);
        }
        else if (gpu)
        {
            StatusText.Text = "GPU 单烤运行中";
            StatusDot.Fill = new SolidColorBrush(GpuAccent);
        }

        StopBtn.IsEnabled = true;
        CpuBtn.IsEnabled = !cpu;
        GpuBtn.IsEnabled = !gpu;
        BothBtn.IsEnabled = false;

        _cpuLoadH.Clear(); _cpuTempH.Clear(); _cpuClockH.Clear(); _cpuPowerH.Clear();
        _gpuLoadH.Clear(); _gpuTempH.Clear(); _gpuClockH.Clear(); _gpuPowerH.Clear();

        StartMonitor();
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e) => StopStress();

    private void StopStress()
    {
        _svc.StopCpuStress();
        _gpuInfoTimer?.Stop(); _gpuInfoTimer = null;

        Stop3dRenderThread();

        _gpuCompute?.Stop();
        _gpuCompute?.Dispose();
        _gpuCompute = null;
        _gpuStressActive = false;

        PreviewBorder.Visibility = Visibility.Collapsed;
        StopMonitor();

        StatusText.Text = "已停止";
        StatusDot.Fill = new SolidColorBrush(Colors.Gray);
        StopBtn.IsEnabled = false;
        CpuBtn.IsEnabled = true;
        GpuBtn.IsEnabled = true;
        BothBtn.IsEnabled = true;
        FpsText.Text = "";
        RemainText.Text = "";
    }

    #endregion

    #region GPU 3D Render (Win2D) + Compute Shader

    private void InitParticles()
    {
        var rng = new Random();
        _particles = new StressParticle[ParticleCount];
        for (int i = 0; i < _particles.Length; i++)
        {
            _particles[i] = new StressParticle
            {
                X = (float)rng.NextDouble() * MaxOffW,
                Y = (float)rng.NextDouble() * MaxOffH,
                Vx = (float)(rng.NextDouble() - 0.5) * 4f,
                Vy = (float)(rng.NextDouble() - 0.5) * 4f,
                Size = (float)rng.NextDouble() * 40f + 15f,
                Hue = (float)rng.NextDouble() * 360f,
                Alpha = (float)rng.NextDouble() * 0.4f + 0.1f
            };
        }
    }

    private void Start3dRenderThread()
    {
        _render3dRunning = true;
        _lastAdaptTs = Stopwatch.GetTimestamp();
        _render3dThread = new Thread(Render3dLoop)
        {
            Name = "GPU-3D",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        _render3dThread.Start();
    }

    private void Stop3dRenderThread()
    {
        _render3dRunning = false;
        try { _render3dThread?.Join(1000); } catch { }
        _render3dThread = null;

        lock (_swapChainLock)
        {
            _swapChain?.Dispose(); _swapChain = null;
            _renderTarget?.Dispose(); _renderTarget = null;
        }
    }

    private void Render3dLoop()
    {
        var device = CanvasDevice.GetSharedDevice();
        CanvasSwapChain? swapChain = null;

        while (_render3dRunning && _gpuStressActive)
        {
            try
            {
                lock (_swapChainLock)
                {
                    if (_renderTarget == null)
                    {
                        _renderTarget = new CanvasRenderTarget(device, MaxOffW, MaxOffH, 96);
                    }
                    if (_swapChain == null)
                    {
                        swapChain = new CanvasSwapChain(device, 400, 200, 96);
                        _swapChain = swapChain;
                        var sc = swapChain;
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            try { SwapChainPanel.SwapChain = sc; } catch { }
                        });
                    }
                }

                if (swapChain == null) continue;

                _gpuTime += 0.016f;
                Draw3dScene();

                _render3dFrameCnt++;
                var now = Stopwatch.GetTimestamp();
                var elapsed = (now - _render3dFrameTs) / (double)Stopwatch.Frequency;
                if (elapsed >= 1.0)
                {
                    _render3dFps = (float)(_render3dFrameCnt / elapsed);
                    _render3dFrameCnt = 0;
                    _render3dFrameTs = now;
                }

                var adaptElapsed = (now - _lastAdaptTs) / (double)Stopwatch.Frequency;
                if (adaptElapsed >= AdaptIntervalSec)
                {
                    _lastAdaptTs = now;
                    Adapt3dLoad();
                }
            }
            catch { break; }
        }
    }

    private void Adapt3dLoad()
    {
        var load = _gpuLoad3d;
        if (load >= 98) return;

        if (load >= 85)
        {
            _currentBlurPasses = Math.Min(_currentBlurPasses + 1, MaxBlurPasses);
        }
        else if (load >= 70)
        {
            _currentBlurPasses = Math.Min(_currentBlurPasses + 2, MaxBlurPasses);
            _currentBlendLayers = Math.Min(_currentBlendLayers + 1, MaxBlendLayers);
        }
        else if (load >= 50)
        {
            _currentBlurPasses = Math.Min(_currentBlurPasses + 3, MaxBlurPasses);
            _currentBlendLayers = Math.Min(_currentBlendLayers + 2, MaxBlendLayers);
        }
        else
        {
            _currentBlurPasses = Math.Min(_currentBlurPasses + 4, MaxBlurPasses);
            _currentBlendLayers = Math.Min(_currentBlendLayers + 3, MaxBlendLayers);
        }
    }

    private void Draw3dScene()
    {
        CanvasRenderTarget rt;
        CanvasSwapChain chain;
        int blurPasses, blendLayers;
        lock (_swapChainLock)
        {
            rt = _renderTarget!;
            chain = _swapChain!;
            blurPasses = _currentBlurPasses;
            blendLayers = _currentBlendLayers;
        }
        if (rt == null || chain == null) return;

        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(255, 8, 8, 16));

            var particles = _particles;
            for (int i = 0; i < particles.Length; i++)
            {
                ref var p = ref particles[i];
                p.X += p.Vx + (float)Math.Sin(_gpuTime * 2 + p.Hue) * 1.0f;
                p.Y += p.Vy + (float)Math.Cos(_gpuTime * 1.5 + p.Hue) * 1.0f;
                if (p.X < 0) p.X += MaxOffW; if (p.X > MaxOffW) p.X -= MaxOffW;
                if (p.Y < 0) p.Y += MaxOffH; if (p.Y > MaxOffH) p.Y -= MaxOffH;

                var color = HslToRgb(p.Hue + _gpuTime * 30, 0.9f, 0.6f);
                ds.FillCircle(p.X, p.Y, p.Size, Color.FromArgb((byte)(p.Alpha * 255), color.R, color.G, color.B));
            }
        }

        ICanvasImage current = rt;

        for (int pass = 0; pass < blurPasses; pass++)
        {
            var blurAmount = 2f + pass * 1.5f + (float)Math.Sin(_gpuTime + pass) * 2f;
            current = new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect
            {
                Source = current,
                BlurAmount = Math.Max(blurAmount, 0.1f),
                BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Soft,
                Optimization = Microsoft.Graphics.Canvas.Effects.EffectOptimization.Speed
            };

            if (pass % 3 == 0)
            {
                var blendColor = HslToRgb(pass * 45f + _gpuTime * 20, 0.7f, 0.4f);
                current = new Microsoft.Graphics.Canvas.Effects.BlendEffect
                {
                    Background = current,
                    Foreground = new Microsoft.Graphics.Canvas.Effects.ColorSourceEffect { Color = Color.FromArgb(30, blendColor.R, blendColor.G, blendColor.B) },
                    Mode = Microsoft.Graphics.Canvas.Effects.BlendEffectMode.Screen
                };
            }
        }

        for (int layer = 0; layer < blendLayers; layer++)
        {
            current = new Microsoft.Graphics.Canvas.Effects.BlendEffect
            {
                Background = current,
                Foreground = rt,
                Mode = Microsoft.Graphics.Canvas.Effects.BlendEffectMode.LinearDodge
            };

            current = new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect
            {
                Source = current,
                BlurAmount = Math.Max(1f + layer * 0.5f, 0.1f),
                BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Soft,
                Optimization = Microsoft.Graphics.Canvas.Effects.EffectOptimization.Speed
            };
        }

        using (var ds = chain.CreateDrawingSession(Color.FromArgb(255, 8, 8, 16)))
        {
            ds.DrawImage(current);
        }
        chain.Present();
    }

    private static Color HslToRgb(float h, float s, float l)
    {
        h = ((h % 360) + 360) % 360;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        var m = l - c / 2;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromArgb(255, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    #endregion

    #region Chart Drawing

    private static readonly Dictionary<Color, SolidColorBrush> _brushCache = [];
    private static readonly Dictionary<(Color, byte), SolidColorBrush> _fillCache = [];

    private static SolidColorBrush GetBrush(Color c)
    {
        if (!_brushCache.TryGetValue(c, out var b)) { b = new SolidColorBrush(c); _brushCache[c] = b; }
        return b;
    }

    private static SolidColorBrush GetFillBrush(Color c)
    {
        var key = (c, (byte)28);
        if (!_fillCache.TryGetValue(key, out var b)) { b = new SolidColorBrush(Color.FromArgb(28, c.R, c.G, c.B)); _fillCache[key] = b; }
        return b;
    }

    private static void DrawChart(Canvas canvas, List<float> loadH, List<float> tempH, List<float> clockH, List<float> powerH)
    {
        canvas.Children.Clear();
        var totalW = canvas.Width; var totalH = canvas.Height;
        if (totalW <= 0 || totalH <= 0) return;
        var drawW = totalW - ChartPaddingLeft - ChartPaddingRight;
        var drawH = totalH - ChartPaddingTop - ChartPaddingBottom;
        if (drawW <= 0 || drawH <= 0) return;

        var isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        var gridColor = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);
        var labelColor = isDark ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(120, 0, 0, 0);
        var gridBrush = GetBrush(gridColor);
        var labelBrush = new SolidColorBrush(labelColor);

        for (int i = 0; i <= 4; i++)
        {
            var y = ChartPaddingTop + drawH * i / 4;
            canvas.Children.Add(new Line { X1 = ChartPaddingLeft, Y1 = y, X2 = ChartPaddingLeft + drawW, Y2 = y, Stroke = gridBrush, StrokeThickness = 0.5, StrokeDashArray = new DoubleCollection { 4, 3 } });
            var pctLabel = new TextBlock { Text = $"{100 - i * 25}%", FontSize = 9, Foreground = labelBrush, Opacity = 0.7 };
            Canvas.SetLeft(pctLabel, 0); Canvas.SetTop(pctLabel, y - 6);
            canvas.Children.Add(pctLabel);
        }

        var tempMax = 120f;
        for (int i = 0; i <= 4; i++)
        {
            var y = ChartPaddingTop + drawH * i / 4;
            var tempLabel = new TextBlock { Text = $"{tempMax - i * tempMax / 4:0}\u00B0", FontSize = 9, Foreground = labelBrush, Opacity = 0.7 };
            Canvas.SetLeft(tempLabel, ChartPaddingLeft + drawW + 4); Canvas.SetTop(tempLabel, y - 6);
            canvas.Children.Add(tempLabel);
        }

        DrawLine(canvas, loadH, LoadColor, 0, 100, drawW, drawH);
        DrawLine(canvas, tempH, TempColor, 0, tempMax, drawW, drawH);
        if (clockH.Count > 0) DrawLine(canvas, clockH, ClockColor, 0, Math.Max(1, clockH.Max() * 1.2f), drawW, drawH);
        if (powerH.Count > 0) DrawLine(canvas, powerH, PowerColor, 0, Math.Max(10, powerH.Max() * 1.2f), drawW, drawH);
    }

    private static void DrawLine(Canvas canvas, List<float> data, Color color, float minVal, float maxVal, double drawW, double drawH)
    {
        if (data.Count < 2) return;
        var range = Math.Max(maxVal - minVal, 0.001f);
        var step = drawW / Math.Max(data.Count - 1, 1);
        var pc = new PointCollection();
        for (int i = 0; i < data.Count; i++) { var x = ChartPaddingLeft + i * step; var y = ChartPaddingTop + drawH - ((data[i] - minVal) / range) * drawH; y = Math.Max(ChartPaddingTop, Math.Min(ChartPaddingTop + drawH, y)); pc.Add(new Windows.Foundation.Point(x, y)); }
        canvas.Children.Add(new Polyline { Points = pc, Stroke = GetBrush(color), StrokeThickness = 1.5 });
        var fc = new PointCollection();
        for (int i = 0; i < data.Count; i++) { var x = ChartPaddingLeft + i * step; var y = ChartPaddingTop + drawH - ((data[i] - minVal) / range) * drawH; y = Math.Max(ChartPaddingTop, Math.Min(ChartPaddingTop + drawH, y)); fc.Add(new Windows.Foundation.Point(x, y)); }
        fc.Add(new Windows.Foundation.Point(ChartPaddingLeft + (data.Count - 1) * step, ChartPaddingTop + drawH));
        fc.Add(new Windows.Foundation.Point(ChartPaddingLeft, ChartPaddingTop + drawH));
        canvas.Children.Add(new Polygon { Points = fc, Fill = GetFillBrush(color) });
    }

    #endregion

    public void Cleanup() { StopStress(); }
}

internal struct StressParticle
{
    public float X, Y, Vx, Vy, Size, Hue, Alpha;
}
