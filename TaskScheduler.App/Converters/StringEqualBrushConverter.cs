using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TaskScheduler.App.Converters;

/// <summary>
/// 将 string 值与参数比较，相等时返回选中态背景画刷，否则返回 Transparent
/// </summary>
public class StringEqualBrushConverter : IValueConverter
{
    public static readonly StringEqualBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string strVal && parameter is string paramStr && strVal == paramStr)
        {
            return Application.Current?.Resources["SystemAccentColor"] as IBrush
                ?? new SolidColorBrush(Colors.DodgerBlue);
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
