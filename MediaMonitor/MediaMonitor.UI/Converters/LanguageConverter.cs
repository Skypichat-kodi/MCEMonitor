using System;
using System.Globalization;
using System.Windows.Data;
using MediaMonitor.Core.Language;

namespace MediaMonitor.UI.Converters
{
    public class LanguageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Le texte passé dans le XAML
            if (value is string key)
            {
                // Appelle directement ton LanguageManager.Get()
                string translated = LanguageManager.Get(key);

                // Si pas trouvé ? retourne le texte original
                return translated ?? key;
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Pas utilisé
            return value;
        }
    }
}

