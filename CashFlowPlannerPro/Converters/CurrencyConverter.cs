using System;
using System.Globalization;
using System.Windows.Data;

namespace CashFlowPlannerPro.Converters
{
    [ValueConversion(typeof(double), typeof(string))]
    public class CurrencyConverter : IValueConverter
    {
        private static readonly CultureInfo DeCulture = new("de-DE");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double amount)
                return amount.ToString("N2", DeCulture) + " \u20AC";
            if (value is decimal decAmount)
                return ((double)decAmount).ToString("N2", DeCulture) + " \u20AC";
            return "0,00 \u20AC";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                str = str.Replace("\u20AC", "").Trim();
                if (double.TryParse(str, NumberStyles.Number, DeCulture, out double result))
                    return result;
            }
            return 0.0;
        }
    }
}
