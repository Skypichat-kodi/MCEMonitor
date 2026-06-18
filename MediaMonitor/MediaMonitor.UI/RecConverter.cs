using System;
using System.Globalization;
using System.Windows.Data;

namespace MediaMonitor.UI
{
    public class RecConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
                return s.StartsWith("REC", StringComparison.OrdinalIgnoreCase);

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


