using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class FpsDetailPage : Page
{
    private readonly List<FpsSnapshot> _snapshots;
    private readonly MonitorSample _hwSample;
    private readonly string _report;

    private static readonly Color FpsAccent = Color.FromArgb(255, 0, 172, 238);

    public FpsDetailPage(List<FpsSnapshot> snapshots, MonitorSample hwSample, string report)
    {
        InitializeComponent();
        _snapshots = snapshots;
        _hwSample = hwSample;
        _report = report;
        Populate();
    }

    private void Populate()
    {
        var hwParts = new List<string>();
        if (!string.IsNullOrEmpty(_hwSample.CpuName)) hwParts.Add($"CPU: {_hwSample.CpuName}");
        if (!string.IsNullOrEmpty(_hwSample.GpuName)) hwParts.Add($"GPU: {_hwSample.GpuName}");
        if (_hwSample.MemTotalGB > 0) hwParts.Add($"内存: {_hwSample.MemTotalGB:F1} GB");
        HwInfoText.Text = hwParts.Count > 0 ? string.Join("  |  ", hwParts) : "未检测到硬件信息";

        var stParts = new List<string>();
        if (_hwSample.CpuLoad >= 0) stParts.Add($"CPU: {_hwSample.CpuLoad:0}%");
        if (_hwSample.CpuTemp >= 0) stParts.Add($"{_hwSample.CpuTemp:0}°C");
        if (_hwSample.CpuPower > 0) stParts.Add($"{_hwSample.CpuPower:0.0}W");
        if (_hwSample.GpuLoad >= 0) stParts.Add($"GPU: {_hwSample.GpuLoad:0}%");
        if (_hwSample.GpuTemp >= 0) stParts.Add($"{_hwSample.GpuTemp:0}°C");
        if (_hwSample.GpuPower > 0) stParts.Add($"{_hwSample.GpuPower:0.0}W");
        if (_hwSample.MemLoad >= 0) stParts.Add($"内存: {_hwSample.MemLoad:0}%");
        HwStatusText.Text = stParts.Count > 0 ? string.Join("  |  ", stParts) : "未检测到硬件状态";

        if (_snapshots.Count == 0)
        {
            EmptyPanel.Visibility = Visibility.Visible;
            AppsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var snap in _snapshots)
        {
            AppsPanel.Children.Add(BuildAppCard(snap));
        }
    }

    private Border BuildAppCard(FpsSnapshot snap)
    {
        var accentBrush = new SolidColorBrush(FpsAccent);
        var dimBrush = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150));

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftStack = new StackPanel { Spacing = 2 };
        leftStack.Children.Add(new TextBlock
        {
            Text = snap.ProcessName,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = accentBrush
        });
        leftStack.Children.Add(new TextBlock
        {
            Text = $"当前: {snap.CurrentFps:0} FPS",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        leftStack.Children.Add(new TextBlock
        {
            Text = $"平均: {snap.AvgFps:0} FPS",
            FontSize = 12,
            Foreground = dimBrush
        });
        leftStack.Children.Add(new TextBlock
        {
            Text = $"统计时长: {snap.TotalSeconds:F1}s  |  总帧数: {snap.TotalFrames}",
            FontSize = 11,
            Foreground = dimBrush
        });
        grid.Children.Add(leftStack);

        var midStack = new StackPanel { Spacing = 2 };
        midStack.Children.Add(new TextBlock { Text = "最低 / 最高", FontSize = 12, Foreground = dimBrush });
        midStack.Children.Add(new TextBlock
        {
            Text = $"{snap.MinFps:0} / {snap.MaxFps:0} FPS",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        Grid.SetColumn(midStack, 1);
        grid.Children.Add(midStack);

        var rightStack = new StackPanel { Spacing = 2 };
        rightStack.Children.Add(new TextBlock { Text = "1% Low / 0.1% Low", FontSize = 12, Foreground = dimBrush });
        rightStack.Children.Add(new TextBlock
        {
            Text = $"{snap.OnePercentLow:0} / {snap.PointOnePercentLow:0} FPS",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = snap.OnePercentLow > 0 && snap.OnePercentLow < 30 ? new SolidColorBrush(Color.FromArgb(255, 234, 67, 53)) : accentBrush
        });
        Grid.SetColumn(rightStack, 2);
        grid.Children.Add(rightStack);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, FpsAccent.R, FpsAccent.G, FpsAccent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = grid
        };
    }

    private async void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName = $"FPS_Report_{DateTime.Now:yyyyMMdd_HHmmss}";
            picker.FileTypeChoices.Add("文本文件", new List<string> { ".txt" });

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await Windows.Storage.FileIO.WriteTextAsync(file, _report);
                var dialog = new ContentDialog
                {
                    Title = "导出成功",
                    Content = $"报告已保存到:\n{file.Path}",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                await dialog.ShowAsync();
            }
        }
        catch { }
    }
}
