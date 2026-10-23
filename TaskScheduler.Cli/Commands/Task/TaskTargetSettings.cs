using System.ComponentModel;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Infrastructure;

namespace TaskScheduler.Cli.Commands.Task;

public class TaskTargetSettings : GlobalCommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("任务名称")]
    public string Name { get; set; } = string.Empty;

    [CommandOption("-g|--group <GROUP>")]
    [Description("任务所属分组（默认 DEFAULT）")]
    [DefaultValue("DEFAULT")]
    public string Group { get; set; } = "DEFAULT";
}
