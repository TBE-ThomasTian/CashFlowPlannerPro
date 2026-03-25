using System;
using System.Globalization;
using System.Windows.Data;

namespace CashFlowPlannerPro.Converters;

/// <summary>
/// Converts date strings (yyyy-MM-dd or dd.MM.yyyy) to German display format (dd.MM.yyyy)
/// and back to ISO storage format (yyyy-MM-dd).
/// </summary>
public class DateDisplayConverter : IValueConverter
{
    private static readonly string[] ParseFormats = [
        "yyyy-MM-dd",
        "dd.MM.yyyy",
        "d.M.yyyy",
        "dd.MM.yy",
        "d.M.yy",
        "yyyy-MM-ddTHH:mm:ss",
    ];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            if (DateTime.TryParseExact(s, ParseFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.ToString("dd.MM.yyyy");
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            if (DateTime.TryParseExact(s, ParseFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.ToString("yyyy-MM-dd");
            // Try German culture as fallback (user typed in German format)
            if (DateTime.TryParse(s, new CultureInfo("de-DE"), DateTimeStyles.None, out var dt2))
                return dt2.ToString("yyyy-MM-dd");
        }
        return value;
    }
}
