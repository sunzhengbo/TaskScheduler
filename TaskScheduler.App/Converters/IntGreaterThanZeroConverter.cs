using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TaskScheduler.App.Converters;

/// <summary>
/// 判断整数值是否大于 0，返回 bool。
/// </summary>
public class IntGreaterThanZeroConverter : IValueConverter
{
    public static readonly IntGreaterThanZeroConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int intVal && intVal > 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
