using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI;
using Windows.Foundation;

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
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
        ActualThemeChanged += OnThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => DrawGraph();
    private void OnThemeChanged(FrameworkElement sender, object args) => DrawGraph();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => DrawGraph();

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SparklineGraph)d).DrawGraph();
    }

    private void DrawGraph()
    {
        var canvas = GraphCanvas;
        if (canvas == null || DataPoints == null || DataPoints.Count < 2) return;

        canvas.Children.Clear();

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;

        foreach (var p in DataPoints)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        double rangeX = maxX - minX;
        double rangeY = maxY - minY;
        if (rangeX == 0) rangeX = 1;
        if (rangeY == 0) rangeY = 1;

        var isDark = ActualTheme == ElementTheme.Dark;
        var lineColor = isDark ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
        var areaBase = isDark ? Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x90, 0x00, 0x00, 0x00);
        var areaZero = isDark ? Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x00, 0x00, 0x00, 0x00);

        var points = new List<Point>();
        foreach (var p in DataPoints)
        {
            double x = (p.X - minX) / rangeX * w;
            double y = h - (p.Y - minY) / rangeY * h;
            points.Add(new Point(x, y));
        }

        var areaPath = new XamlPath
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(0, 0),
                GradientStops =
                {
                    new GradientStop { Color = areaBase, Offset = 1 },
                    new GradientStop { Color = areaZero, Offset = 0 },
                }
            },
        };

        var areaFigure = new PathFigure { StartPoint = points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            areaFigure.Segments.Add(new LineSegment { Point = points[i] });
        }
        areaFigure.Segments.Add(new LineSegment { Point = new Point(points[^1].X, h) });
        areaFigure.Segments.Add(new LineSegment { Point = new Point(points[0].X, h) });
        areaFigure.Segments.Add(new LineSegment { Point = points[0] });

        var areaGeo = new PathGeometry();
        areaGeo.Figures.Add(areaFigure);
        areaPath.Data = areaGeo;
        canvas.Children.Add(areaPath);

        var linePath = new XamlPath
        {
            Stroke = new SolidColorBrush(lineColor),
            StrokeThickness = 1.5,
        };

        var lineFigure = new PathFigure { StartPoint = points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            lineFigure.Segments.Add(new LineSegment { Point = points[i] });
        }

        var lineGeo = new PathGeometry();
        lineGeo.Figures.Add(lineFigure);
        linePath.Data = lineGeo;
        canvas.Children.Add(linePath);
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
