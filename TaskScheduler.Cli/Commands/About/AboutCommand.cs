using System.Runtime.InteropServices;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.About;

public sealed class AboutCommand(DatabaseProvider dbProvider) : Command<GlobalCommandSettings>
{
    public override int Execute(CommandContext context, GlobalCommandSettings settings, CancellationToken cancellationToken)
    {
        var schedulerRunning = SchedulerBootstrapService.IsBackgroundSchedulerRunning();

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(new
            {
                application = "TaskScheduler",
                version = AppMetadata.Version,
                releaseTime = AppMetadata.ReleaseTime,
                framework = AppMetadata.CliFramework,
                runtimeVersion = RuntimeInformation.FrameworkDescription,
                osDescription = RuntimeInformation.OSDescription,
                platforms = AppMetadata.Platforms,
                license = AppMetadata.License,
                databasePath = dbProvider.DatabasePath,
                schedulerActive = schedulerRunning
            });
            return 0;
        }

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(3))
            .AddColumn();

        grid.AddRow("[grey]应用名称:[/]", "[bold cyan]TaskScheduler (Desktop & CLI)[/]");
        grid.AddRow("[grey]版本编号:[/]", Markup.Escape(AppMetadata.Version));
        grid.AddRow("[grey]发布时间:[/]", Markup.Escape(AppMetadata.ReleaseTime));
        grid.AddRow("[grey]技术架构:[/]", Markup.Escape(AppMetadata.CliFramework));
        grid.AddRow("[grey]运行时环境:[/]", Markup.Escape($"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.OSDescription})"));
        grid.AddRow("[grey]支持平台:[/]", Markup.Escape(AppMetadata.Platforms));
        grid.AddRow("[grey]开源协议:[/]", Markup.Escape(AppMetadata.License));
        grid.AddRow("[grey]数据库路径:[/]", Markup.Escape(dbProvider.DatabasePath));
        grid.AddRow("[grey]调度引擎状态:[/]", schedulerRunning ? "[green]● 正在后台运行[/]" : "[yellow]○ 待机中 (Standby)[/]");

        var panel = new Panel(grid)
            .Header("[bold cyan]关于 TaskScheduler[/]")
            .Border(BoxBorder.Rounded);

        AnsiConsole.Write(panel);
        return 0;
    }
}
