using System;
using System.Globalization;

using Avalonia.Data.Converters;

using TaskScheduler.Core.Models;

namespace TaskScheduler.App.Converters;

/// <summary>
/// 将 <see cref="TriggerState"/> 转换为 StatusBadge 的 Status 字符串
/// </summary>
public class TriggerStateToBadgeStatusConverter : IValueConverter
{
    public static readonly TriggerStateToBadgeStatusConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TriggerState state)
        {
            return state switch
            {
                TriggerState.Normal => "Success",
                TriggerState.Paused => "Warn",
                TriggerState.Error => "Danger",
                TriggerState.Complete => "Muted",
                TriggerState.Blocked => "Accent",
                _ => "Normal"
            };
        }

        return "Normal";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
