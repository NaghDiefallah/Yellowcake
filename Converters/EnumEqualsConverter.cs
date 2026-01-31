using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Yellowcake.Converters;

public class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.Equals(parameter) == true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? parameter : Avalonia.Data.BindingOperations.DoNothing;
    }
}
