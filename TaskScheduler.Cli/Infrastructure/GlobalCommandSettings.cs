using System.ComponentModel;
using Spectre.Console.Cli;

namespace TaskScheduler.Cli.Infrastructure;

/// <summary>
/// 所有 CLI 命令共享的全局选项基类
/// </summary>
public class GlobalCommandSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("以结构化 JSON 格式输出（适用于脚本与管道处理）")]
    public bool Json { get; set; }

    [CommandOption("--verbose")]
    [Description("显示详细的诊断与调试信息")]
    public bool Verbose { get; set; }
}
