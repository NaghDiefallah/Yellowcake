using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Yellowcake.Converters;

public class BoolToStatusTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isInstalled)
        {
            return isInstalled ? "✓ Installed" : "Not Installed";
        }
        return "Unknown";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}