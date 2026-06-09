using System;
using System.Globalization;

using Avalonia.Data.Converters;

using TaskScheduler.Core.Models;

namespace TaskScheduler.App.Converters;

/// <summary>
/// 将 <see cref="ExecutionStatus"/> 转换为中文显示文本
/// </summary>
public class ExecutionStatusToDisplayConverter : IValueConverter
{
    public static readonly ExecutionStatusToDisplayConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ExecutionStatus status)
        {
            return status switch
            {
                ExecutionStatus.Success => "成功",
                ExecutionStatus.Failed => "失败",
                ExecutionStatus.Timeout => "超时",
                ExecutionStatus.Cancelled => "已取消",
                _ => status.ToString()
            };
        }

        return "未知";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
