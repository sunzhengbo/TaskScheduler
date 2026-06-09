using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TaskScheduler.App.Converters;

/// <summary>
/// Compares an integer value with a parameter and returns true if they are equal.
/// </summary>
public class IntEqualConverter : IValueConverter
{
    public static readonly IntEqualConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intVal && parameter is string paramStr && int.TryParse(paramStr, out var paramInt))
        {
            return intVal == paramInt;
        }
        if (value is int v && parameter is int p)
        {
            return v == p;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
