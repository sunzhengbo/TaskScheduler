using System;
using System.Globalization;

using Avalonia.Data.Converters;

using TaskScheduler.Core.Models;

namespace TaskScheduler.App.Converters;

/// <summary>
/// 将 <see cref="TriggerState"/> 转换为中文显示文本
/// </summary>
public class TriggerStateToDisplayConverter : IValueConverter
{
    public static readonly TriggerStateToDisplayConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TriggerState state)
        {
            return state switch
            {
                TriggerState.Normal => "正常",
                TriggerState.Paused => "已暂停",
                TriggerState.Complete => "已完成",
                TriggerState.Blocked => "已阻塞",
                TriggerState.Error => "异常",
                TriggerState.None => "无",
                _ => state.ToString()
            };
        }

        return "全部";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
