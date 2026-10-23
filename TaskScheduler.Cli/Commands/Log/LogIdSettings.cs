using System.ComponentModel;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Infrastructure;

namespace TaskScheduler.Cli.Commands.Log;

public class LogIdSettings : GlobalCommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("执行日志记录 ID")]
    public int Id { get; set; }
}
