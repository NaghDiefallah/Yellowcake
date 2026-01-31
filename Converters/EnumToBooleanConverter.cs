using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Yellowcake.Converters
{
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            var parameterString = parameter.ToString();

            if (string.IsNullOrWhiteSpace(parameterString))
                return false;

            if (!Enum.IsDefined(value.GetType(), parameterString))
                return false;

            var parameterValue = Enum.Parse(value.GetType(), parameterString);

            return value.Equals(parameterValue);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not bool isChecked || !isChecked)
                return BindingNotification.Null;

            if (parameter == null)
                return BindingNotification.Null;

            var parameterString = parameter.ToString();

            if (string.IsNullOrWhiteSpace(parameterString))
                return BindingNotification.Null;

            try
            {
                return Enum.Parse(targetType, parameterString);
            }
            catch
            {
                return BindingNotification.Null;
            }
        }
    }
}