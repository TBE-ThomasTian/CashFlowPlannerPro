using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CashFlowPlannerPro.Converters
{
    [ValueConversion(typeof(string), typeof(SolidColorBrush))]
    public class ColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    return new SolidColorBrush(color);
                }
                catch
                {
                    return new SolidColorBrush(Colors.Transparent);
                }
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
            {
                var c = brush.Color;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return "#000000";
        }
    }
}
