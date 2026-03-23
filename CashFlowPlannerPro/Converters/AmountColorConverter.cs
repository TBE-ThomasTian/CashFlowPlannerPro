using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CashFlowPlannerPro.Converters
{
    [ValueConversion(typeof(double), typeof(SolidColorBrush))]
    public class AmountColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush GreenBrush = new((Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981"));
        private static readonly SolidColorBrush RedBrush = new((Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444"));

        static AmountColorConverter()
        {
            GreenBrush.Freeze();
            RedBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double amount = value switch
            {
                double d => d,
                decimal m => (double)m,
                int i => i,
                float f => f,
                _ => 0.0
            };
            return amount >= 0 ? GreenBrush : RedBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
