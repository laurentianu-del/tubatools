using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;

namespace TubaWinUi3.Pages;

#region Widget type enum

public enum OverlayWidgetType
{
    FpsText, CpuTempText, CpuLoadText, CpuClockText, CpuPowerText,
    GpuTempText, GpuLoadText, GpuClockText, GpuPowerText, GpuVramText,
    MemLoadText, MemUsedText,
    DiskReadText, DiskWriteText,
    NetUpText, NetDownText,
    FpsLow1Text, FpsLow01Text,
    CpuNameText, GpuNameText,
    FpsChart, CpuTempChart,
    CustomText, CustomImage, ColorBlock
}

#endregion

public sealed partial class GameOverlayPage : Page
{
    #region Fields

    private readonly List<DesignerWidget> _widgets = new();
    private DesignerWidget? _selectedWidget;
    private bool _overlayRunning;
    private DispatcherTimer? _pollTimer;
    private readonly List<GameWindowInfo> _gameWindows = new();
    private readonly List<string> _customWindowTitles = new();
    private IntPtr _targetHwnd;
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartX, _dragStartY;
    private bool _suppressEvents;
    private bool _widgetHasCapture;
    private const string SettingsPrefix = "GameOverlay_";

    private sealed class DesignerWidget
    {
        public OverlayWidgetType Type;
        public Border? Container;
        public TextBlock? TextElement;
        public Canvas? ChartElement;
        public double X, Y, Width = 140, Height = 32;
        public double FontSize = 14;
        public string Prefix = "";
        public string Label = "";
        public bool IsChart;
        // Custom content
        public string CustomText = "";
        public string ImagePath = "";
        public uint ColorArgb = 0xFF00A0FF;
        // Resize handle
        public Thumb? ResizeThumb;
    }

    private sealed class GameWindowInfo
    {
        public IntPtr Hwnd;
        public string Title = "";
        public string ProcessName = "";
        public uint Pid;
        public bool IsCustom;
    }

    #endregion

    #region Win32 P/Invoke for window enumeration

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    #endregion

    public GameOverlayPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        CheckPawnIOInstalled();
        InitPalette();
        LoadConfig();
        ScanGameWindows();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        StopPolling();
        GameOverlayWindow.CloseOverlay();
    }

    #region PawnIO Detection

    private void CheckPawnIOInstalled()
    {
        bool installed = false;
        try
        {
            // Check for PawnIO service via SCManager
            var scm = OpenSCManager(null, null, 0x0001); // SC_MANAGER_CONNECT
            if (scm != IntPtr.Zero)
            {
                var svc = OpenService(scm, "PawnIO", 0x0001); // SERVICE_QUERY_STATUS
                if (svc != IntPtr.Zero)
                {
                    installed = true;
                    CloseServiceHandle(svc);
                }
                CloseServiceHandle(scm);
            }
        }
        catch { }

        if (installed)
        {
            PawnIOOverlay.Visibility = Visibility.Collapsed;
            MainContent.Visibility = Visibility.Visible;
        }
        else
        {
            PawnIOOverlay.Visibility = Visibility.Visible;
            MainContent.Visibility = Visibility.Collapsed;
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    private async void InstallPawnIO_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TxtStatus.Text = "正在下载 PawnIO 安装程序...";
            var url = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
            var destDir = System.IO.Path.Combine(ConfigManager.GetDataDir(), "downloads");
            System.IO.Directory.CreateDirectory(destDir);
            var destFile = System.IO.Path.Combine(destDir, "PawnIO_setup.exe");

            await ToolDownloaderService.DownloadToFileAsync(url, destDir, "PawnIO_setup.exe",
                null, default);

            if (System.IO.File.Exists(destFile))
            {
                Process.Start(new ProcessStartInfo(destFile) { UseShellExecute = true });
                TxtStatus.Text = "PawnIO 安装程序已启动，请按提示完成安装后点击「重新检测」";
            }
            else
            {
                TxtStatus.Text = "下载失败，请手动前往 GitHub 下载";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"下载失败: {ex.Message}";
        }
    }

    private void RetryPawnIO_Click(object sender, RoutedEventArgs e)
    {
        CheckPawnIOInstalled();
    }

    #endregion

    #region Widget Palette

    private static readonly (OverlayWidgetType Type, string Label, string Icon, bool IsChart)[] PaletteItems =
    [
        (OverlayWidgetType.FpsText, "FPS", "\uE9F5", false),
        (OverlayWidgetType.FpsLow1Text, "FPS 1% Low", "\uE9F5", false),
        (OverlayWidgetType.FpsLow01Text, "FPS 0.1% Low", "\uE9F5", false),
        (OverlayWidgetType.CpuTempText, "CPU 温度", "\uE9B0", false),
        (OverlayWidgetType.CpuLoadText, "CPU 负载", "\uE9B0", false),
        (OverlayWidgetType.CpuClockText, "CPU 频率", "\uE9B0", false),
        (OverlayWidgetType.CpuPowerText, "CPU 功耗", "\uE9B0", false),
        (OverlayWidgetType.CpuNameText, "CPU 名称", "\uE9B0", false),
        (OverlayWidgetType.GpuTempText, "GPU 温度", "\uE9B0", false),
        (OverlayWidgetType.GpuLoadText, "GPU 负载", "\uE9B0", false),
        (OverlayWidgetType.GpuClockText, "GPU 频率", "\uE9B0", false),
        (OverlayWidgetType.GpuPowerText, "GPU 功耗", "\uE9B0", false),
        (OverlayWidgetType.GpuVramText, "显存使用", "\uE9B0", false),
        (OverlayWidgetType.GpuNameText, "GPU 名称", "\uE9B0", false),
        (OverlayWidgetType.MemLoadText, "内存负载", "\uE9B0", false),
        (OverlayWidgetType.MemUsedText, "内存使用", "\uE9B0", false),
        (OverlayWidgetType.DiskReadText, "磁盘读取", "\uE9B0", false),
        (OverlayWidgetType.DiskWriteText, "磁盘写入", "\uE9B0", false),
        (OverlayWidgetType.NetUpText, "网络上传", "\uE9B0", false),
        (OverlayWidgetType.NetDownText, "网络下载", "\uE9B0", false),
        (OverlayWidgetType.FpsChart, "FPS 图表", "\uE9F5", true),
        (OverlayWidgetType.CpuTempChart, "CPU温度 图表", "\uE9B0", true),
        (OverlayWidgetType.CustomText, "自定义文字", "\uE8E5", false),
        (OverlayWidgetType.CustomImage, "自定义图片", "\uEB9F", false),
        (OverlayWidgetType.ColorBlock, "自定义色块", "\uE790", false),
    ];

    private void InitPalette()
    {
        PalettePanel.Children.Clear();
        foreach (var (type, label, icon, isChart) in PaletteItems)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                Tag = type,
                IsHitTestVisible = true,
                AllowDrop = false,
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            sp.Children.Add(new FontIcon
            {
                Glyph = icon,
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = label + (isChart ? " 📊" : ""),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            card.Child = sp;

            // Click to add widget
            card.PointerPressed += PaletteItem_PointerPressed;

            // Hover effect
            card.PointerEntered += (_, _) =>
            {
                card.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
            };
            card.PointerExited += (_, _) =>
            {
                card.Background = new SolidColorBrush(Colors.Transparent);
            };

            PalettePanel.Children.Add(card);
        }
    }

    private void PaletteItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card && card.Tag is OverlayWidgetType type)
        {
            // Click to add widget at a staggered position on the canvas
            double offsetX = (_widgets.Count % 5) * 30 + 10;
            double offsetY = (_widgets.Count / 5) * 40 + 10;
            AddWidgetToCanvas(type, offsetX, offsetY);
        }
    }

    #endregion

    #region Canvas Drag-Drop

    private void Canvas_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "放置组件";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
    }

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        try
        {
            var def = e.GetDeferral();
            var pos = e.GetPosition(DesignCanvas);

            // Try to get the widget type from the data
            if (e.DataView.Contains("StandardText"))
            {
                var task = e.DataView.GetTextAsync().AsTask();
                task.Wait();
                var text = task.Result;
                if (Enum.TryParse<OverlayWidgetType>(text, out var type))
                {
                    AddWidgetToCanvas(type, pos.X, pos.Y);
                }
            }
            def.Complete();
        }
        catch { }
    }

    private void AddWidgetToCanvas(OverlayWidgetType type, double x, double y)
    {
        var info = PaletteItems.FirstOrDefault(p => p.Type == type);
        var widget = new DesignerWidget
        {
            Type = type,
            X = Math.Max(4, x),
            Y = Math.Max(4, y),
            Label = info.Label,
            IsChart = info.IsChart,
            Width = type switch
            {
                OverlayWidgetType.CustomImage => 120,
                OverlayWidgetType.ColorBlock => 48,
                OverlayWidgetType.CpuNameText or OverlayWidgetType.GpuNameText => 220,
                OverlayWidgetType.FpsLow1Text or OverlayWidgetType.FpsLow01Text => 120,
                _ when info.IsChart => 160,
                _ => 140
            },
            Height = type switch
            {
                OverlayWidgetType.CustomImage => 90,
                OverlayWidgetType.ColorBlock => 48,
                _ when info.IsChart => 60,
                _ => 32
            },
            FontSize = type == OverlayWidgetType.CpuNameText || type == OverlayWidgetType.GpuNameText ? 13 : 14,
            CustomText = type == OverlayWidgetType.CustomText ? "自定义文字" : "",
            ImagePath = type == OverlayWidgetType.CustomImage ? "" : "",
            ColorArgb = 0xFF00A0FF
        };

        CreateWidgetElement(widget);
        _widgets.Add(widget);
        SelectWidget(widget);
        UpdateStatus();
        SaveConfig();
    }

    private void CreateWidgetElement(DesignerWidget widget)
    {
        var container = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Width = widget.Width,
            Height = widget.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Tag = widget,
            IsHitTestVisible = true,
            AllowDrop = false,
        };

        if (widget.IsChart)
        {
            var chartCanvas = new Canvas { Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)) };
            // Add chart label preview
            var chartLabel = new TextBlock
            {
                Text = widget.Label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4, 2, 0, 0)
            };
            chartCanvas.Children.Add(chartLabel);
            widget.ChartElement = chartCanvas;
            container.Child = chartCanvas;
        }
        else if (widget.Type == OverlayWidgetType.CustomImage)
        {
            // Preview: show a placeholder or loaded image
            var img = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Source = LoadImageSource(widget.ImagePath, widget.Label)
            };
            widget.ChartElement = null;
            widget.TextElement = null;
            container.Child = img;
        }
        else if (widget.Type == OverlayWidgetType.ColorBlock)
        {
            // Preview: solid color block
            var c = FromArgb(widget.ColorArgb);
            container.Background = new SolidColorBrush(c);
            widget.TextElement = null;
            widget.ChartElement = null;
            container.Child = null;
            container.CornerRadius = new CornerRadius(4);
        }
        else
        {
            string preview = widget.Type == OverlayWidgetType.CustomText
                ? (string.IsNullOrEmpty(widget.CustomText) ? "自定义文字" : widget.CustomText)
                : $"{widget.Prefix}--";
            var tb = new TextBlock
            {
                Text = preview,
                FontSize = widget.FontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                TextTrimming = TextTrimming.Clip
            };
            widget.TextElement = tb;
            container.Child = tb;
        }

        widget.Container = container;

        // Mouse handlers for drag reposition on canvas
        container.PointerPressed += Widget_PointerPressed;
        container.PointerMoved += Widget_PointerMoved;
        container.PointerReleased += Widget_PointerReleased;

        Canvas.SetLeft(container, widget.X);
        Canvas.SetTop(container, widget.Y);
        DesignCanvas.Children.Add(container);

        // Resize thumb
        var thumb = new Thumb
        {
            Width = 8,
            Height = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(180, 100, 100, 255)),
        };
        thumb.DragDelta += ResizeThumb_DragDelta;
        widget.ResizeThumb = thumb;
        // Add resize thumb to canvas overlay
        Canvas.SetLeft(thumb, widget.X + widget.Width - 4);
        Canvas.SetTop(thumb, widget.Y + widget.Height - 4);
        Canvas.SetZIndex(thumb, 10);
        DesignCanvas.Children.Add(thumb);
    }

    private void Widget_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border container || container.Tag is not DesignerWidget widget) return;

        SelectWidget(widget);
        _isDragging = false;
        _widgetHasCapture = true;
        var pos = e.GetCurrentPoint(DesignCanvas).Position;
        _dragStartPoint = pos;
        _dragStartX = widget.X;
        _dragStartY = widget.Y;
        container.CapturePointer(e.Pointer);
    }

    private void Widget_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border container || container.Tag is not DesignerWidget widget) return;
        if (!_widgetHasCapture) return;

        var pos = e.GetCurrentPoint(DesignCanvas).Position;
        var dx = pos.X - _dragStartPoint.X;
        var dy = pos.Y - _dragStartPoint.Y;

        if (!_isDragging && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3))
            _isDragging = true;

        if (_isDragging)
        {
            widget.X = Math.Max(0, _dragStartX + dx);
            widget.Y = Math.Max(0, _dragStartY + dy);
            Canvas.SetLeft(widget.Container, widget.X);
            Canvas.SetTop(widget.Container, widget.Y);

            // Move resize thumb too
            if (widget.ResizeThumb != null)
            {
                Canvas.SetLeft(widget.ResizeThumb, widget.X + widget.Width - 4);
                Canvas.SetTop(widget.ResizeThumb, widget.Y + widget.Height - 4);
            }

            // Update properties panel
            _suppressEvents = true;
            if (PropX != null) PropX.Value = widget.X;
            if (PropY != null) PropY.Value = widget.Y;
            _suppressEvents = false;
        }
    }

    private void Widget_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border container)
        {
            _widgetHasCapture = false;
            container.ReleasePointerCapture(e.Pointer);
            if (_isDragging)
            {
                SaveConfig();
                _isDragging = false;
            }
        }
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb) return;
        var widget = _widgets.FirstOrDefault(w => w.ResizeThumb == thumb);
        if (widget == null) return;

        widget.Width = Math.Max(30, widget.Width + e.HorizontalChange);
        widget.Height = Math.Max(16, widget.Height + e.VerticalChange);

        if (widget.Container != null)
        {
            widget.Container.Width = widget.Width;
            widget.Container.Height = widget.Height;
        }

        Canvas.SetLeft(thumb, widget.X + widget.Width - 4);
        Canvas.SetTop(thumb, widget.Y + widget.Height - 4);

        _suppressEvents = true;
        if (PropW != null) PropW.Value = widget.Width;
        if (PropH != null) PropH.Value = widget.Height;
        _suppressEvents = false;

        SaveConfig();
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Click on empty canvas area — deselect
        if (e.OriginalSource is Canvas canvas && canvas == DesignCanvas)
        {
            SelectWidget(null);
        }
    }

    #endregion

    #region Widget Selection & Properties

    private void SelectWidget(DesignerWidget? widget)
    {
        // Deselect previous
        if (_selectedWidget?.Container != null)
        {
            _selectedWidget.Container.BorderBrush = new SolidColorBrush(Colors.Transparent);
            if (_selectedWidget.ResizeThumb != null)
                _selectedWidget.ResizeThumb.Visibility = Visibility.Collapsed;
        }

        _selectedWidget = widget;

        if (widget != null)
        {
            widget.Container!.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            if (widget.ResizeThumb != null)
                widget.ResizeThumb.Visibility = Visibility.Visible;

            CardProps.Visibility = Visibility.Visible;
            TxtCompLabel.Text = widget.Label;
            _suppressEvents = true;
            PropX.Value = widget.X;
            PropY.Value = widget.Y;
            PropW.Value = widget.Width;
            PropH.Value = widget.Height;
            PropFS.Value = widget.FontSize;
            PropPrefix.Text = widget.Prefix;
            PropFS.IsEnabled = !widget.IsChart;
            PropPrefix.IsEnabled = !widget.IsChart;

            // Custom content panels
            bool isCustomText = widget.Type == OverlayWidgetType.CustomText;
            bool isCustomImage = widget.Type == OverlayWidgetType.CustomImage;
            bool isColorBlock = widget.Type == OverlayWidgetType.ColorBlock;
            CardCustomText.Visibility = isCustomText ? Visibility.Visible : Visibility.Collapsed;
            CardCustomImage.Visibility = isCustomImage ? Visibility.Visible : Visibility.Collapsed;
            CardCustomColor.Visibility = isColorBlock ? Visibility.Visible : Visibility.Collapsed;

            if (isCustomText) PropCustomText.Text = widget.CustomText;
            if (isCustomImage) PropImagePath.Text = string.IsNullOrEmpty(widget.ImagePath) ? "未选择图片" : Path.GetFileName(widget.ImagePath);
            if (isColorBlock) ColorPreview.Background = new SolidColorBrush(FromArgb(widget.ColorArgb));
            _suppressEvents = false;
        }
        else
        {
            CardProps.Visibility = Visibility.Collapsed;
        }
    }

    private void PropPos_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents || _selectedWidget == null) return;
        _selectedWidget.X = PropX.Value;
        _selectedWidget.Y = PropY.Value;
        if (_selectedWidget.Container != null)
        {
            Canvas.SetLeft(_selectedWidget.Container, _selectedWidget.X);
            Canvas.SetTop(_selectedWidget.Container, _selectedWidget.Y);
        }
        if (_selectedWidget.ResizeThumb != null)
        {
            Canvas.SetLeft(_selectedWidget.ResizeThumb, _selectedWidget.X + _selectedWidget.Width - 4);
            Canvas.SetTop(_selectedWidget.ResizeThumb, _selectedWidget.Y + _selectedWidget.Height - 4);
        }
        SaveConfig();
    }

    private void PropSize_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents || _selectedWidget == null) return;
        _selectedWidget.Width = Math.Max(30, PropW.Value);
        _selectedWidget.Height = Math.Max(16, PropH.Value);
        if (_selectedWidget.Container != null)
        {
            _selectedWidget.Container.Width = _selectedWidget.Width;
            _selectedWidget.Container.Height = _selectedWidget.Height;
        }
        if (_selectedWidget.ResizeThumb != null)
        {
            Canvas.SetLeft(_selectedWidget.ResizeThumb, _selectedWidget.X + _selectedWidget.Width - 4);
            Canvas.SetTop(_selectedWidget.ResizeThumb, _selectedWidget.Y + _selectedWidget.Height - 4);
        }
        SaveConfig();
    }

    private void PropFS_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents || _selectedWidget == null || _selectedWidget.IsChart) return;
        _selectedWidget.FontSize = Math.Max(8, PropFS.Value);
        if (_selectedWidget.TextElement != null)
            _selectedWidget.TextElement.FontSize = _selectedWidget.FontSize;
        SaveConfig();
    }

    private void PropPrefix_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || _selectedWidget == null || _selectedWidget.IsChart) return;
        _selectedWidget.Prefix = PropPrefix.Text ?? "";
        SaveConfig();
    }

    private void PropCustomText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || _selectedWidget == null) return;
        _selectedWidget.CustomText = PropCustomText.Text ?? "";
        if (_selectedWidget.TextElement != null)
            _selectedWidget.TextElement.Text = string.IsNullOrEmpty(_selectedWidget.CustomText)
                ? "自定义文字" : _selectedWidget.CustomText;
        SaveConfig();
    }

    private async void PickImage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWidget == null) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".webp");
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

            var window = App.MainWindow;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                _selectedWidget.ImagePath = file.Path;
                PropImagePath.Text = Path.GetFileName(file.Path);
                // Refresh preview
                var img = _selectedWidget.Container?.Child as Image;
                if (img != null) img.Source = LoadImageSource(file.Path, _selectedWidget.Label);
                SaveConfig();
            }
        }
        catch { }
    }

    private async void PickColor_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWidget == null) return;

        // Simple preset color picker dialog
        var presetColors = new (string Name, uint Argb)[]
        {
            ("蓝色", 0xFF0080FF), ("青色", 0xFF00C8C8), ("绿色", 0xFF00C050),
            ("橙色", 0xFFFF8000), ("红色", 0xFFFF4040), ("黄色", 0xFFFFD700),
            ("紫色", 0xFF9040FF), ("粉色", 0xFFFF69B4), ("白色", 0xFFFFFFFF),
            ("灰色", 0xFF808080), ("黑色", 0xFF202020), ("透明黑", 0xEE000000),
        };

        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

        uint picked = _selectedWidget.ColorArgb;
        var tcs = new TaskCompletionSource<bool>();

        for (int i = 0; i < presetColors.Length; i++)
        {
            var (name, argb) = presetColors[i];
            var box = new Button
            {
                Width = 36,
                Height = 26,
                Background = new SolidColorBrush(FromArgb(argb)),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = argb
            };
            ToolTipService.SetToolTip(box, name);
            box.Click += (_, _) =>
            {
                picked = (uint)box.Tag;
                tcs.TrySetResult(true);
            };
            Grid.SetRow(box, i / 3);
            Grid.SetColumn(box, i % 3);
            grid.Children.Add(box);
        }

        var dialog = new ContentDialog
        {
            Title = "选择色块颜色",
            Content = new StackPanel { Spacing = 10, Children = { grid } },
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        bool ok = false;
        try { ok = (await dialog.ShowAsync() == ContentDialogResult.Primary); } catch { }
        if (tcs.Task.IsCompleted) ok = true;

        if (ok)
        {
            _selectedWidget.ColorArgb = picked;
            ColorPreview.Background = new SolidColorBrush(FromArgb(picked));
            if (_selectedWidget.Container != null)
                _selectedWidget.Container.Background = new SolidColorBrush(FromArgb(picked));
            SaveConfig();
        }
    }

    private void DeleteComp_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWidget == null) return;
        if (_selectedWidget.Container != null)
            DesignCanvas.Children.Remove(_selectedWidget.Container);
        if (_selectedWidget.ResizeThumb != null)
            DesignCanvas.Children.Remove(_selectedWidget.ResizeThumb);
        _widgets.Remove(_selectedWidget);
        SelectWidget(null);
        UpdateStatus();
        SaveConfig();
    }

    #endregion

    #region Canvas Size & Position

    /// <summary>
    /// Positions the resize thumb, size label and dashed border at the canvas's
    /// bottom-right corner so they always track the canvas dimensions.
    /// </summary>
    private void UpdateCanvasDecorations()
    {
        double w = DesignCanvas.Width;
        double h = DesignCanvas.Height;
        if (double.IsNaN(w) || w < 1) w = 600;
        if (double.IsNaN(h) || h < 1) h = 300;
        DesignCanvas.Width = w;
        DesignCanvas.Height = h;

        Canvas.SetLeft(CanvasResizeThumb, w - CanvasResizeThumb.Width);
        Canvas.SetTop(CanvasResizeThumb, h - CanvasResizeThumb.Height);

        Canvas.SetLeft(TxtCanvasSize, w - 90);
        Canvas.SetTop(TxtCanvasSize, h - 18);

        CanvasBorderRect.Width = w;
        CanvasBorderRect.Height = h;

        TxtCanvasSize.Text = $"{(int)w} × {(int)h}";
    }

    private void CanvasSize_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents) return;
        var w = NbCanvasW.Value;
        var h = NbCanvasH.Value;
        if (double.IsNaN(w) || w < 200) w = 600;
        if (double.IsNaN(h) || h < 100) h = 300;
        DesignCanvas.Width = w;
        DesignCanvas.Height = h;
        UpdateCanvasDecorations();
        SaveConfig();
    }

    private void Position_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveConfig();
        // If overlay is running, update position
        if (GameOverlayWindow.Instance != null)
        {
            GameOverlayWindow.Instance.SetPosition(GetSelectedPosition());
        }
    }

    private GameOverlayWindow.OverlayPosition GetSelectedPosition()
    {
        return CmbPosition.SelectedIndex switch
        {
            0 => GameOverlayWindow.OverlayPosition.TopLeft,
            1 => GameOverlayWindow.OverlayPosition.TopCenter,
            2 => GameOverlayWindow.OverlayPosition.TopRight,
            3 => GameOverlayWindow.OverlayPosition.MiddleLeft,
            4 => GameOverlayWindow.OverlayPosition.Center,
            5 => GameOverlayWindow.OverlayPosition.MiddleRight,
            6 => GameOverlayWindow.OverlayPosition.BottomLeft,
            7 => GameOverlayWindow.OverlayPosition.BottomCenter,
            8 => GameOverlayWindow.OverlayPosition.BottomRight,
            _ => GameOverlayWindow.OverlayPosition.TopLeft
        };
    }

    private void CanvasResize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double curW = double.IsNaN(DesignCanvas.Width) ? 600 : DesignCanvas.Width;
        double curH = double.IsNaN(DesignCanvas.Height) ? 300 : DesignCanvas.Height;
        var newW = Math.Clamp(curW + e.HorizontalChange, 200, 2000);
        var newH = Math.Clamp(curH + e.VerticalChange, 100, 1500);
        DesignCanvas.Width = newW;
        DesignCanvas.Height = newH;

        _suppressEvents = true;
        NbCanvasW.Value = newW;
        NbCanvasH.Value = newH;
        _suppressEvents = false;

        UpdateCanvasDecorations();
        SaveConfig();
    }

    #endregion

    #region Background Opacity

    private void BgOpacity_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var val = SliderBgOpacity.Value;
        TxtBgOpacity.Text = $"{val:F0}%";
        if (GameOverlayWindow.Instance != null)
            GameOverlayWindow.Instance.SetBackgroundOpacity((float)(val / 100));
        SaveConfig();
    }

    #endregion

    #region Game Window Scanning

    private void ScanWindows_Click(object sender, RoutedEventArgs e)
    {
        ScanGameWindows();
    }

    private void ScanGameWindows()
    {
        _gameWindows.Clear();

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            int titleLen = GetWindowTextLengthW(hwnd);
            if (titleLen == 0) return true;

            var sb = new StringBuilder(titleLen + 1);
            GetWindowTextW(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            GetWindowThreadProcessId(hwnd, out var pid);
            string procName = "";
            try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }

            // Filter out system/irrelevant windows
            if (IsSystemProcess(procName)) return true;

            _gameWindows.Add(new GameWindowInfo
            {
                Hwnd = hwnd,
                Title = title,
                ProcessName = procName,
                Pid = pid
            });
            return true;
        }, IntPtr.Zero);

        // Add custom windows
        foreach (var customTitle in _customWindowTitles)
        {
            if (_gameWindows.Any(w => w.Title == customTitle)) continue;
            var found = _gameWindows.FirstOrDefault(w => w.Title.Contains(customTitle, StringComparison.OrdinalIgnoreCase));
            if (found == null)
            {
                _gameWindows.Add(new GameWindowInfo
                {
                    Title = customTitle,
                    ProcessName = "(自定义)",
                    IsCustom = true
                });
            }
        }

        // Populate ComboBox
        CmbGameWindow.Items.Clear();
        foreach (var w in _gameWindows.OrderBy(w => w.Title))
        {
            var item = new ComboBoxItem
            {
                Content = $"{w.ProcessName} — {TruncateTitle(w.Title, 30)}",
                Tag = w
            };
            CmbGameWindow.Items.Add(item);
        }

        TxtWindowStatus.Text = $"已扫描到 {_gameWindows.Count} 个窗口";
    }

    private static string TruncateTitle(string title, int maxLen)
    {
        return title.Length > maxLen ? title[..maxLen] + "..." : title;
    }

    private static bool IsSystemProcess(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        ReadOnlySpan<string> excluded = [
            "dwm", "explorer", "svchost", "csrss", "smss", "lsass", "wininit",
            "services", "winlogon", "fontdrvhost", "dllhost", "conhost", "Taskmgr",
            "System", "Idle", "SearchHost", "ShellExperienceHost", "RuntimeBroker",
            "ApplicationFrameHost", "StartMenuExperienceHost", "sihost", "taskhostw",
            "ctfmon", "TubaWinUi3", "MSBuild", "devenv"
        ];
        foreach (var ex in excluded)
            if (name.Equals(ex, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void GameWindow_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CmbGameWindow.SelectedItem is ComboBoxItem item && item.Tag is GameWindowInfo info)
        {
            _targetHwnd = info.Hwnd;
            TxtWindowStatus.Text = $"已选择: {info.ProcessName}";
            BtnRemoveCustom.Visibility = info.IsCustom ? Visibility.Visible : Visibility.Collapsed;

            if (_overlayRunning && GameOverlayWindow.Instance != null)
            {
                GameOverlayWindow.Instance.SetTargetWindow(_targetHwnd);
            }
            SaveConfig();
        }
    }

    private void AddCustomWindow_Click(object sender, RoutedEventArgs e)
    {
        // Show a dialog to add a custom window by title
        ShowAddCustomWindowDialog();
    }

    private async void ShowAddCustomWindowDialog()
    {
        var inputBox = new TextBox { PlaceholderText = "输入窗口标题关键字...", Width = 300 };
        var dialog = new ContentDialog
        {
            Title = "添加自定义游戏窗口",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "输入游戏窗口标题（支持部分匹配）:", FontSize = 13 },
                    inputBox
                }
            },
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text))
        {
            var title = inputBox.Text.Trim();
            if (!_customWindowTitles.Contains(title, StringComparer.OrdinalIgnoreCase))
            {
                _customWindowTitles.Add(title);
                ScanGameWindows();
                SaveConfig();
            }
        }
    }

    private void RemoveCustomWindow_Click(object sender, RoutedEventArgs e)
    {
        if (CmbGameWindow.SelectedItem is ComboBoxItem item && item.Tag is GameWindowInfo info && info.IsCustom)
        {
            _customWindowTitles.Remove(info.Title);
            ScanGameWindows();
            SaveConfig();
        }
    }

    #endregion

    #region Overlay Toggle

    private void ToggleOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayRunning)
            StopOverlay();
        else
            StartOverlay();
    }

    private void StartOverlay()
    {
        if (_widgets.Count == 0)
        {
            TxtStatus.Text = "请先拖入至少一个组件";
            return;
        }

        var overlayWidgets = _widgets.Select(w => new GameOverlayWindow.WidgetInstance
        {
            Type = w.Type,
            X = (int)w.X,
            Y = (int)w.Y,
            Width = (int)w.Width,
            Height = (int)w.Height,
            FontSize = (int)w.FontSize,
            Prefix = w.Prefix,
            IsChart = w.IsChart,
            CustomText = w.CustomText,
            ImagePath = w.ImagePath,
            ColorArgb = w.ColorArgb
        }).ToList();

        int cw = (int)Math.Max(200, NbCanvasW.Value);
        int ch = (int)Math.Max(100, NbCanvasH.Value);

        GameOverlayWindow.ShowOverlay(
            _targetHwnd,
            overlayWidgets,
            (float)(SliderBgOpacity.Value / 100),
            GetSelectedPosition(),
            cw, ch
        );

        StartPolling();
        _overlayRunning = true;
        ToggleOverlayIcon.Glyph = "\uE71A"; // Stop icon
        ToggleOverlayText.Text = "停止覆盖层";
        TxtStatus.Text = "覆盖层运行中";
    }

    private void StopOverlay()
    {
        StopPolling();
        GameOverlayWindow.CloseOverlay();
        _overlayRunning = false;
        ToggleOverlayIcon.Glyph = "\uE768"; // Play icon
        ToggleOverlayText.Text = "启动覆盖层";
        TxtStatus.Text = "已停止";
    }

    #endregion

    #region Data Polling

    private void StartPolling()
    {
        StopPolling();
        var interval = Math.Max(200, (int)NbRefresh.Value);
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        if (_pollTimer != null)
        {
            _pollTimer.Tick -= OnPollTick;
            _pollTimer.Stop();
            _pollTimer = null;
        }
    }

    private void OnPollTick(object? sender, object e)
    {
        try
        {
            var sample = LiteMonitorService.Instance.Read(fpsEnabled: true);
            GameOverlayWindow.Instance?.UpdateData(sample);
        }
        catch { }
    }

    #endregion

    #region Config Persistence

    private void SaveConfig()
    {
        try
        {
            AppSettings.Set(SettingsPrefix + "CanvasW", NbCanvasW.Value);
            AppSettings.Set(SettingsPrefix + "CanvasH", NbCanvasH.Value);
            AppSettings.Set(SettingsPrefix + "Position", CmbPosition.SelectedIndex);
            AppSettings.Set(SettingsPrefix + "Refresh", NbRefresh.Value);
            AppSettings.Set(SettingsPrefix + "BgOpacity", SliderBgOpacity.Value);

            // Save widget layout as JSON
            var layout = _widgets.Select(w => new
            {
                type = (int)w.Type,
                x = w.X, y = w.Y,
                w = w.Width, h = w.Height,
                fs = w.FontSize,
                prefix = w.Prefix,
                text = w.CustomText,
                img = w.ImagePath,
                color = w.ColorArgb
            }).ToList();
            AppSettings.Set(SettingsPrefix + "Layout", JsonSerializer.Serialize(layout));

            // Save custom windows
            AppSettings.Set(SettingsPrefix + "CustomWindows", string.Join("|", _customWindowTitles));

            // Save selected game window
            if (CmbGameWindow.SelectedIndex >= 0)
                AppSettings.Set(SettingsPrefix + "SelectedWindow", CmbGameWindow.SelectedIndex);
        }
        catch { }
    }

    private void LoadConfig()
    {
        try
        {
            _suppressEvents = true;

            NbCanvasW.Value = AppSettings.GetDouble(SettingsPrefix + "CanvasW", 600);
            NbCanvasH.Value = AppSettings.GetDouble(SettingsPrefix + "CanvasH", 300);
            if (double.IsNaN(NbCanvasW.Value) || NbCanvasW.Value < 200) NbCanvasW.Value = 600;
            if (double.IsNaN(NbCanvasH.Value) || NbCanvasH.Value < 100) NbCanvasH.Value = 300;
            DesignCanvas.Width = NbCanvasW.Value;
            DesignCanvas.Height = NbCanvasH.Value;
            UpdateCanvasDecorations();

            CmbPosition.SelectedIndex = AppSettings.GetInt(SettingsPrefix + "Position", 0);
            NbRefresh.Value = AppSettings.GetDouble(SettingsPrefix + "Refresh", 1000);
            SliderBgOpacity.Value = AppSettings.GetDouble(SettingsPrefix + "BgOpacity", 70);
            TxtBgOpacity.Text = $"{SliderBgOpacity.Value:F0}%";

            // Load custom windows
            _customWindowTitles.Clear();
            var customWindowsStr = AppSettings.Get(SettingsPrefix + "CustomWindows") ?? "";
            if (!string.IsNullOrEmpty(customWindowsStr))
            {
                foreach (var title in customWindowsStr.Split('|', StringSplitOptions.RemoveEmptyEntries))
                    _customWindowTitles.Add(title);
            }

            // Load widget layout
            var layoutJson = AppSettings.Get(SettingsPrefix + "Layout") ?? "";
            if (!string.IsNullOrEmpty(layoutJson))
            {
                using var doc = JsonDocument.Parse(layoutJson);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var type = (OverlayWidgetType)item.GetProperty("type").GetInt32();
                    var widget = new DesignerWidget
                    {
                        Type = type,
                        X = item.GetProperty("x").GetDouble(),
                        Y = item.GetProperty("y").GetDouble(),
                        Width = item.GetProperty("w").GetDouble(),
                        Height = item.GetProperty("h").GetDouble(),
                        FontSize = item.GetProperty("fs").GetDouble(),
                        Prefix = item.TryGetProperty("prefix", out var p) ? p.GetString() ?? "" : "",
                        CustomText = item.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                        ImagePath = item.TryGetProperty("img", out var im) ? im.GetString() ?? "" : "",
                        ColorArgb = item.TryGetProperty("color", out var cl) && cl.TryGetUInt32(out var cc) ? cc : 0xFF00A0FF,
                        Label = PaletteItems.FirstOrDefault(pi => pi.Type == type).Label ?? type.ToString(),
                        IsChart = type is OverlayWidgetType.FpsChart or OverlayWidgetType.CpuTempChart,
                    };
                    CreateWidgetElement(widget);
                    _widgets.Add(widget);
                }
            }

            _suppressEvents = false;
            UpdateStatus();
        }
        catch
        {
            _suppressEvents = false;
        }
    }

    #endregion

    #region Helpers

    private void UpdateStatus()
    {
        TxtCompCount.Text = $"组件: {_widgets.Count}";
    }

    private static Windows.UI.Color FromArgb(uint argb)
    {
        return Windows.UI.Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));
    }

    private static ImageSource LoadImageSource(string path, string fallbackLabel)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                bmp.SetSourceAsync(stream.AsRandomAccessStream()).GetAwaiter().GetResult();
                return bmp;
            }
        }
        catch { }
        // Fallback: colored placeholder text image
        return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
    }

    #endregion
}
