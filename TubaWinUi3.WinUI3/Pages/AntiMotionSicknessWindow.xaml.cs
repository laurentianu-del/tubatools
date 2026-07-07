using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class AntiMotionSicknessWindow : Window
{
    private AntiMotionSicknessConfig _cfg;
    private bool _suppressEvents = true;

    public AntiMotionSicknessWindow()
    {
        InitializeComponent();

        AppWindow.Title = "游戏防晕3D";
        AppWindow.Resize(new SizeInt32(680, 760));

        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        }
        catch { }

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        try
        {
            var mainPos = App.MainWindow?.AppWindow.Position;
            if (mainPos is not null)
            {
                AppWindow.Move(new PointInt32(
                    mainPos.Value.X + 50,
                    mainPos.Value.Y + 50));
            }
        }
        catch { }

        _cfg = AntiMotionSicknessConfig.Load();
        LoadConfigToUI();
        UpdateOverlayStatus(AntiMotionSicknessOverlay.IsRunning);

        _suppressEvents = false;

        Closed += (_, _) =>
        {
            AntiMotionSicknessOverlay.CloseOverlay();
            SaveConfigFromUI();
        };
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
            StatusIcon.Foreground = new SolidColorBrush(Colors.Gray);
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

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
