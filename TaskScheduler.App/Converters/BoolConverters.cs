using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TaskScheduler.App.Converters;

public static class BoolConverters
{
    public static readonly IValueConverter Not = new NotConverter();

    private sealed class NotConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool b ? !b : false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool b ? !b : false;
        }
    }
}
