using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class AntiMotionSicknessWindow : Page
{
    private readonly Window _window;
    private AntiMotionSicknessConfig _cfg;
    private bool _suppressEvents = true;
    private List<MonitorInfo> _monitors = new();

    public AntiMotionSicknessWindow(Window window)
    {
        _window = window;
        InitializeComponent();

        _cfg = AntiMotionSicknessConfig.Load();
        LoadMonitors();
        LoadConfigToUI();
        UpdateOverlayStatus(AntiMotionSicknessOverlay.IsRunning);

        _suppressEvents = false;

        _window.Closed += (_, _) =>
        {
            AntiMotionSicknessOverlay.CloseOverlay();
            SaveConfigFromUI();
        };
    }

    private void LoadMonitors()
    {
        try
        {
            _monitors = AntiMotionSicknessOverlay.GetMonitors();
        }
        catch
        {
            _monitors = new List<MonitorInfo>();
        }

        MonitorCombo.Items.Clear();

        if (_monitors.Count == 0)
        {
            MonitorCombo.Items.Add(new ComboBoxItem { Content = "主显示器", Tag = 0 });
            MonitorCombo.SelectedIndex = 0;
            MonitorInfoText.Text = "使用主显示器";
            return;
        }

        for (var i = 0; i < _monitors.Count; i++)
        {
            var m = _monitors[i];
            MonitorCombo.Items.Add(new ComboBoxItem { Content = m.DisplayName, Tag = i });
        }

        var savedIndex = AppSettings.GetInt("AntiMotionSickness_MonitorIndex", 0);
        if (savedIndex >= 0 && savedIndex < _monitors.Count)
            MonitorCombo.SelectedIndex = savedIndex;
        else if (_monitors.Count > 0)
            MonitorCombo.SelectedIndex = 0;

        UpdateMonitorInfoText();
    }

    private void UpdateMonitorInfoText()
    {
        if (_monitors.Count == 0)
        {
            MonitorInfoText.Text = "未检测到显示器";
            return;
        }

        var idx = MonitorCombo.SelectedIndex;
        if (idx < 0 || idx >= _monitors.Count) return;

        var m = _monitors[idx];
        MonitorInfoText.Text = $"分辨率: {m.Width}×{m.Height}";
    }

    private void LoadConfigToUI()
    {
        OpacitySlider.Value = _cfg.Opacity;
        OpacityText.Text = $"{(int)_cfg.Opacity}%";

        ShowCenterToggle.IsOn = _cfg.ShowCenter;
        ShowTopToggle.IsOn = _cfg.ShowTop;
        ShowBottomToggle.IsOn = _cfg.ShowBottom;
        ShowLeftToggle.IsOn = _cfg.ShowLeft;
        ShowRightToggle.IsOn = _cfg.ShowRight;

        CenterColorPicker.Color = _cfg.CenterColor;
        CenterSizeSlider.Value = _cfg.CenterSize;
        CenterSizeText.Text = ((int)_cfg.CenterSize).ToString();
        CenterThicknessSlider.Value = _cfg.CenterThickness;
        CenterThicknessText.Text = ((int)_cfg.CenterThickness).ToString();
        CenterStyleCombo.SelectedIndex = (int)_cfg.CenterStyle;

        EdgeColorPicker.Color = _cfg.EdgeColor;
        EdgeSizeSlider.Value = _cfg.EdgeSize;
        EdgeSizeText.Text = ((int)_cfg.EdgeSize).ToString();
        EdgeShapeCombo.SelectedIndex = (int)_cfg.EdgeShape;

        ForceTopmostToggle.IsOn = AppSettings.GetBool("AntiMotionSickness_ForceTopmost", false);
    }

    private void SaveConfigFromUI()
    {
        try
        {
            _cfg.Opacity = OpacitySlider.Value;

            _cfg.ShowCenter = ShowCenterToggle.IsOn;
            _cfg.ShowTop = ShowTopToggle.IsOn;
            _cfg.ShowBottom = ShowBottomToggle.IsOn;
            _cfg.ShowLeft = ShowLeftToggle.IsOn;
            _cfg.ShowRight = ShowRightToggle.IsOn;

            _cfg.CenterColor = CenterColorPicker.Color;
            _cfg.CenterSize = CenterSizeSlider.Value;
            _cfg.CenterThickness = CenterThicknessSlider.Value;
            _cfg.CenterStyle = (CrosshairStyle)CenterStyleCombo.SelectedIndex;

            _cfg.EdgeColor = EdgeColorPicker.Color;
            _cfg.EdgeSize = EdgeSizeSlider.Value;
            _cfg.EdgeShape = (EdgeMarkerShape)EdgeShapeCombo.SelectedIndex;

            _cfg.Save();

            if (AntiMotionSicknessOverlay.IsRunning)
                AntiMotionSicknessOverlay.RefreshVisuals();
        }
        catch { }
    }

    private void Slider_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;

        OpacityText.Text = $"{(int)OpacitySlider.Value}%";
        CenterSizeText.Text = ((int)CenterSizeSlider.Value).ToString();
        CenterThicknessText.Text = ((int)CenterThicknessSlider.Value).ToString();
        EdgeSizeText.Text = ((int)EdgeSizeSlider.Value).ToString();

        SaveConfigFromUI();
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveConfigFromUI();
    }

    private void Color_Changed(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_suppressEvents) return;
        SaveConfigFromUI();
    }

    private void Combo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveConfigFromUI();
    }

    private void OverlayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        if (OverlayToggle.IsOn)
            StartOverlay();
        else
            StopOverlay();
    }

    private void ToggleOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (AntiMotionSicknessOverlay.IsRunning)
            StopOverlay();
        else
            StartOverlay();
    }

    private void StartOverlay()
    {
        SaveConfigFromUI();

        var savedIndex = AppSettings.GetInt("AntiMotionSickness_MonitorIndex", 0);
        AntiMotionSicknessOverlay.TargetMonitorIndex = savedIndex;

        var forceTopmost = AppSettings.GetBool("AntiMotionSickness_ForceTopmost", false);
        AntiMotionSicknessOverlay.ForceTopmostMode = forceTopmost;

        try
        {
            AntiMotionSicknessOverlay.ShowOverlay();
        }
        catch { }
        UpdateOverlayStatus(AntiMotionSicknessOverlay.IsRunning);
    }

    private void StopOverlay()
    {
        try
        {
            AntiMotionSicknessOverlay.CloseOverlay();
        }
        catch { }
        UpdateOverlayStatus(false);
    }

    private void UpdateOverlayStatus(bool running)
    {
        _suppressEvents = true;
        OverlayToggle.IsOn = running;
        _suppressEvents = false;

        if (running)
        {
            StatusIcon.Glyph = "\uE73E";
            StatusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 74, 222, 128));
            StatusTitle.Text = "辅助器运行中";
            StatusDesc.Text = "屏幕准星和标记已开启，不影响鼠标键盘操作";
            ToggleOverlayIcon.Glyph = "\uE71A";
            ToggleOverlayText.Text = "关闭防晕3D辅助";
        }
        else
        {
            StatusIcon.Glyph = "\uE894";
            StatusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
            StatusTitle.Text = "辅助器已关闭";
            StatusDesc.Text = "点击下方按钮开启屏幕准星辅助";
            ToggleOverlayIcon.Glyph = "\uE73E";
            ToggleOverlayText.Text = "开启防晕3D辅助";
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _cfg = new AntiMotionSicknessConfig();
        _cfg.Save();

        _suppressEvents = true;
        LoadConfigToUI();
        _suppressEvents = false;

        if (AntiMotionSicknessOverlay.IsRunning)
            AntiMotionSicknessOverlay.RefreshVisuals();
    }

    private void MonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || MonitorCombo.SelectedIndex < 0) return;

        var idx = MonitorCombo.SelectedIndex;
        AppSettings.Set("AntiMotionSickness_MonitorIndex", idx);
        AntiMotionSicknessOverlay.TargetMonitorIndex = idx;

        UpdateMonitorInfoText();

        if (AntiMotionSicknessOverlay.IsRunning)
            AntiMotionSicknessOverlay.MoveToMonitor(idx);
    }

    private void ForceTopmostToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        var forceTopmost = ForceTopmostToggle.IsOn;
        AppSettings.Set("AntiMotionSickness_ForceTopmost", forceTopmost);
        AntiMotionSicknessOverlay.ForceTopmostMode = forceTopmost;

        if (AntiMotionSicknessOverlay.IsRunning)
        {
            AntiMotionSicknessOverlay.CloseOverlay();
            AntiMotionSicknessOverlay.ShowOverlay();
        }
    }
}
