using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Syncfusion.UI.Xaml.Gauges;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace TubaWinUi3.Controls;

public sealed partial class HealthGauge : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(HealthGauge),
        new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(HealthGauge),
        new PropertyMetadata("健康状态"));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set
        {
            SetValue(ValueProperty, value);
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(PointerBrush));
            OnPropertyChanged(nameof(Color1));
            OnPropertyChanged(nameof(Color2));
            OnPropertyChanged(nameof(Color3));
        }
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string StatusLabel
    {
        get
        {
            if (Value < 20) return "报废";
            if (Value < 60) return "较差";
            if (Value <= 90) return "良好";
            return "正常";
        }
    }

    public SolidColorBrush PointerBrush
    {
        get
        {
            if (Value < 20) return (SolidColorBrush)Resources["HG_RedBrush"];
            if (Value < 60) return (SolidColorBrush)Resources["HG_YellowBrush"];
            return (SolidColorBrush)Resources["HG_BlueBrush"];
        }
    }

    public Color Color1
    {
        get
        {
            if (Value < 20) return ((SolidColorBrush)Resources["HG_RedBrush"]).Color;
            if (Value < 60) return ((SolidColorBrush)Resources["HG_YellowBrush"]).Color;
            return ((SolidColorBrush)Resources["HG_BlueBrush"]).Color;
        }
    }

    public Color Color2
    {
        get
        {
            if (Value < 20) return ((SolidColorBrush)Resources["HG_RedLight"]).Color;
            if (Value < 60) return ((SolidColorBrush)Resources["HG_YellowLight"]).Color;
            return ((SolidColorBrush)Resources["HG_BlueLight"]).Color;
        }
    }

    public Color Color3
    {
        get
        {
            if (Value < 20) return ((SolidColorBrush)Resources["HG_RedLighter"]).Color;
            if (Value < 60) return ((SolidColorBrush)Resources["HG_YellowLighter"]).Color;
            return ((SolidColorBrush)Resources["HG_BlueLighter"]).Color;
        }
    }

    public HealthGauge()
    {
        InitializeComponent();
        DataContext = this;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (HealthGauge)d;
        g.OnPropertyChanged(nameof(StatusLabel));
        g.OnPropertyChanged(nameof(PointerBrush));
        g.OnPropertyChanged(nameof(Color1));
        g.OnPropertyChanged(nameof(Color2));
        g.OnPropertyChanged(nameof(Color3));
    }

    private void rangePointer_ValueChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.Value < 16)
        {
            gradient1.Value = e.Value / 2;
            gradient2.Value = e.Value;
        }
        else
        {
            gradient1.Value = e.Value - 16;
            gradient2.Value = e.Value;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum HealthGaugeLevel
{
    Critical,
    Warning,
    Good,
    Excellent
}
