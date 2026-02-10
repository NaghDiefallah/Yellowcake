using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Yellowcake.Converters;

public class LogLevelToBrushConverter : IValueConverter
{
    public static readonly LogLevelToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string level)
        {
            var color = level.ToUpperInvariant() switch
            {
                "DEBUG" => Color.Parse("#6C757D"),
                "INFORMATION" or "INFO" => Color.Parse("#0D6EFD"),
                "WARNING" or "WARN" => Color.Parse("#FFC107"),
                "ERROR" => Color.Parse("#DC3545"),
                "FATAL" or "CRITICAL" => Color.Parse("#6F0000"),
                _ => Color.Parse("#6C757D")
            };

            return color;
        }

        return Color.Parse("#6C757D");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}