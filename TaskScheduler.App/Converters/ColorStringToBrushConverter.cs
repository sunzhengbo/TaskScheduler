using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TaskScheduler.App.Converters;

/// <summary>
/// 将颜色字符串（如 "#3776ab"）转换为 SolidColorBrush，可指定透明度
/// </summary>
public class ColorStringToBrushConverter : IValueConverter
{
    public static readonly ColorStringToBrushConverter Instance = new();

    /// <summary>透明度 (0.0 ~ 1.0)</summary>
    public double Opacity { get; set; } = 1.0;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorStr)
        {
            if (double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity))
            {
                var color = Color.Parse(colorStr);
                return new SolidColorBrush(color, opacity);
            }
            else
            {
                var color = Color.Parse(colorStr);
                return new SolidColorBrush(color, Opacity);
            }
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
