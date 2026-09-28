using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HyperTrunk.Converters
{
    // Visible quand la valeur booléenne est False (utilisé pour les bandeaux
    // d'avertissement "pas admin" / "Hyper-V désactivé").
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            return flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
