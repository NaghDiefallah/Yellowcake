using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Yellowcake.Converters;

public class GreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            double d => d > 0,
            int i => i > 0,
            float f => f > 0,
            long l => l > 0,
            decimal dec => dec > 0,
            _ => false
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}