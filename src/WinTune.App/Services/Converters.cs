using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WinTune.App.Services;

/// <summary>Converte uma série 0..100 em pontos de uma <c>Polyline</c> (gráfico leve, sem biblioteca).</summary>
public sealed class SeriesToPointsConverter : IValueConverter
{
    public double Width { get; set; } = 360;

    public double Height { get; set; } = 80;

    public int Capacity { get; set; } = 120;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var points = new PointCollection();
        if (value is not IReadOnlyList<double> series || series.Count == 0)
        {
            return points;
        }

        var step = Width / Math.Max(1, Capacity - 1);
        var offset = Capacity - series.Count;
        for (var i = 0; i < series.Count; i++)
        {
            var v = Math.Clamp(series[i], 0, 100);
            points.Add(new Point((offset + i) * step, Height - (v / 100.0 * Height)));
        }

        points.Freeze();
        return points;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

/// <summary>Inverte um booleano.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
}
