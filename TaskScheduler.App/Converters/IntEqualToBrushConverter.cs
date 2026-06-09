using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TaskScheduler.App.Converters;

/// <summary>
/// 将 SelectedTab (int) 与参数比较，相等时返回选中态背景画刷，否则返回 Transparent
/// </summary>
public class IntEqualToBrushConverter : IValueConverter
{
    public static readonly IntEqualToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intVal && parameter is string paramStr && int.TryParse(paramStr, out var paramInt) && intVal == paramInt)
        {
            return Application.Current?.Resources["SystemControlBackgroundListLowBrush"] as IBrush
                ?? new SolidColorBrush(Colors.Gray, 0.12);
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
