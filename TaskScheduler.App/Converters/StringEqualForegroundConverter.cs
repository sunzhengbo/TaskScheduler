using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TaskScheduler.App.Converters;

/// <summary>
/// 将 string 值与参数比较，相等时返回白色前景，否则返回默认前景
/// </summary>
public class StringEqualForegroundConverter : IValueConverter
{
    public static readonly StringEqualForegroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string strVal && parameter is string paramStr && strVal == paramStr)
        {
            return Brushes.White;
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
