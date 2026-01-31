using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Yellowcake.Converters;

public class EnabledToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool isEnabled && isEnabled ? 1.0 : 0.5;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}