using System;
using System.Globalization;

using Avalonia.Data.Converters;

using TaskScheduler.Core.Models;

namespace TaskScheduler.App.Converters;

/// <summary>
/// 将 <see cref="ExecutionStatus"/> 转换为 StatusBadge 的 Status 字符串
/// </summary>
public class ExecutionStatusToBadgeStatusConverter : IValueConverter
{
    public static readonly ExecutionStatusToBadgeStatusConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ExecutionStatus status)
        {
            return status switch
            {
                ExecutionStatus.Success => "Success",
                ExecutionStatus.Failed => "Danger",
                ExecutionStatus.Timeout => "Warn",
                ExecutionStatus.Cancelled => "Muted",
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
