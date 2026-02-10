using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Yellowcake.Converters;

public class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        if (value is bool?) return !(bool?)(value) ?? true;
        return Avalonia.Data.BindingNotification.Null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        if (value is bool?) return !(bool?)(value) ?? true;
        return Avalonia.Data.BindingNotification.Null;
    }
}