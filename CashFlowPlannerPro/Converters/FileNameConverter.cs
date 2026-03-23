using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace CashFlowPlannerPro.Converters;

[ValueConversion(typeof(string), typeof(string))]
public class FileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
            return Path.GetFileName(path);
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value;
}
