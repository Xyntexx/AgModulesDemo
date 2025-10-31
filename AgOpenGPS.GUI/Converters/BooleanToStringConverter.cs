using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AgOpenGPS.GUI.Converters;

public class BooleanToStringConverter : IValueConverter
{
    public static readonly BooleanToStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "Engaged" : "Disengaged";
        }
        return "Unknown";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
