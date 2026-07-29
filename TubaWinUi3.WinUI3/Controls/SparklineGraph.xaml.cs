using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace TubaWinUi3.Controls;

public sealed partial class SparklineGraph : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty DataPointsProperty = DependencyProperty.Register(
        nameof(DataPoints), typeof(IList<GraphDataPoint>), typeof(SparklineGraph),
        new PropertyMetadata(null, OnDataChanged));

    public IList<GraphDataPoint>? DataPoints
    {
        get => (IList<GraphDataPoint>?)GetValue(DataPointsProperty);
        set => SetValue(DataPointsProperty, value);
    }

    public SparklineGraph()
    {
        InitializeComponent();
        DataContext = this;
        ApplyThemeBrushes();
        ActualThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(FrameworkElement sender, object args)
    {
        ApplyThemeBrushes();
    }

    private void ApplyThemeBrushes()
    {
        var isDark = ActualTheme == ElementTheme.Dark;

        var lineBrush = new SolidColorBrush(isDark ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xFF, 0x00, 0x00, 0x00));
        LineSeries.Fill = lineBrush;

        var areaBrush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 1),
            EndPoint = new Windows.Foundation.Point(0, 0)
        };
        var areaBase = isDark ? Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x90, 0x00, 0x00, 0x00);
        var areaZero = isDark ? Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x00, 0x00, 0x00, 0x00);
        areaBrush.GradientStops.Add(new GradientStop { Color = areaBase, Offset = 1 });
        areaBrush.GradientStops.Add(new GradientStop { Color = areaZero, Offset = 0 });
        AreaSeries.Fill = areaBrush;
        AreaSeries.Stroke = lineBrush;
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SparklineGraph)d).UpdateChart();
    }

    private void UpdateChart()
    {
        if (DataPoints == null || DataPoints.Count < 2) return;
        LineSeries.ItemsSource = DataPoints;
        AreaSeries.ItemsSource = DataPoints;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public struct GraphDataPoint
{
    public double X { get; }
    public double Y { get; }

    public GraphDataPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}
