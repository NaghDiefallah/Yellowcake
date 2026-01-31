using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Yellowcake.Converters;

public class BoolToAccentBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isEnabled)
        {
            var resourceName = isEnabled ? "SystemAccentBrush" : "SubTextBrush";

            if (Application.Current?.Resources.TryGetResource(resourceName, null, out var brush) == true)
            {
                return brush;
            }
        }

        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}