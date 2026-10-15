using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NetMonitor.UI.Models;

namespace NetMonitor.UI.Converters;

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Severity s) return Brushes.Transparent;
        return s switch
        {
            Severity.Critical => new SolidColorBrush(Color.FromRgb(192, 57, 43)),
            Severity.High     => new SolidColorBrush(Color.FromRgb(230, 126, 34)),
            Severity.Warning  => new SolidColorBrush(Color.FromRgb(241, 196, 15)),
            _                 => new SolidColorBrush(Color.FromRgb(52, 152, 219))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}