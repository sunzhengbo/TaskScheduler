using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TaskScheduler.App.Converters;

/// <summary>
/// 将 int 值与参数比较，相等时返回 SemiBold，否则返回 Normal
/// </summary>
public class IntEqualFontWeightConverter : IValueConverter
{
    public static readonly IntEqualFontWeightConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intVal && parameter is string paramStr && int.TryParse(paramStr, out var paramInt))
        {
            return intVal == paramInt ? FontWeight.SemiBold : FontWeight.Normal;
        }
        return FontWeight.Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
