namespace TaskScheduler.Core.Models;

/// <summary>
/// 跨进程（CLI 与 GUI）同步事件类型
/// </summary>
public enum SyncEventType
{
    /// <summary>任务列表或触发器状态发生变更</summary>
    TasksChanged,

    /// <summary>执行历史日志发生变更</summary>
    LogsChanged,

    /// <summary>解释器工具配置发生变更</summary>
    ToolsChanged,

    /// <summary>引擎或外观全局设置发生变更</summary>
    SettingsChanged,

    /// <summary>请求正在运行的后台调度器立即触发指定任务</summary>
    TriggerJobNow
}
