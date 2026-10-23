using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Config;

public sealed class ConfigShowCommand(ISettingsService settingsService) : AsyncCommand<ConfigShowCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("-s|--section <SECTION>")]
        [Description("显示的配置分区（all, engine, appearance，默认 all）")]
        [DefaultValue("all")]
        public string Section { get; set; } = "all";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var sec = settings.Section.Trim().ToLowerInvariant();
        if (sec is not ("all" or "engine" or "appearance"))
        {
            CliOutputFormatter.PrintError($"无效的配置分区: '{settings.Section}'。可选值: all, engine, appearance", settings.Json);
            return 2;
        }

        var engineJson = await settingsService.GetValueAsync("engine_settings", cancellationToken);
        var appearanceJson = await settingsService.GetValueAsync("appearance_settings", cancellationToken);

        var engine = EngineSettings.FromJson(engineJson);
        var appearance = AppearanceSettings.FromJson(appearanceJson);

        if (settings.Json)
        {
            if (sec == "engine")
                CliOutputFormatter.WriteJson(new { engine });
            else if (sec == "appearance")
                CliOutputFormatter.WriteJson(new { appearance });
            else
                CliOutputFormatter.WriteJson(new { engine, appearance });
            return 0;
        }

        if (sec is "all" or "engine")
        {
            var engTable = new Table()
                .Title("[bold cyan]引擎设置 (Engine Settings)[/]")
                .Border(TableBorder.Rounded)
                .AddColumn("配置项")
                .AddColumn("当前值")
                .AddColumn("说明");

            engTable.AddRow("StartupEnabled (--startup)", engine.StartupEnabled ? "[green]开启 (true)[/]" : "[grey]关闭 (false)[/]", "开机自动启动应用程序");
            engTable.AddRow("StartupMinimize (--minimize)", engine.StartupMinimize ? "[green]开启 (true)[/]" : "[grey]关闭 (false)[/]", "开机自启时最小化到系统托盘");
            engTable.AddRow("RescheduleOnStartup (--reschedule)", engine.RescheduleOnStartup ? "[green]开启 (true)[/]" : "[grey]关闭 (false)[/]", "启动时重新计算 SimpleTrigger 起始时间");
            engTable.AddRow("LogRetentionDays (--retention-days)", $"{engine.LogRetentionDays} 天", "执行历史日志保留天数");
            engTable.AddRow("MaxThreads (--max-threads)", $"{engine.MaxThreads}", "调度引擎最大并发工作线程数");
            engTable.AddRow("DatabaseType", Markup.Escape(engine.DatabaseType), "持久化存储数据库类型");

            AnsiConsole.Write(engTable);
            AnsiConsole.WriteLine();
        }

        if (sec is "all" or "appearance")
        {
            var appTable = new Table()
                .Title("[bold cyan]外观设置 (Appearance Settings)[/]")
                .Border(TableBorder.Rounded)
                .AddColumn("配置项")
                .AddColumn("当前值")
                .AddColumn("说明");

            appTable.AddRow("ThemeMode (--theme)", $"[cyan]{Markup.Escape(appearance.ThemeMode)}[/]", "界面主题模式 (System / Light / Dark)");
            appTable.AddRow("CompactMode (--compact)", appearance.CompactMode ? "[green]开启 (true)[/]" : "[grey]关闭 (false)[/]", "紧凑列表布局模式");

            AnsiConsole.Write(appTable);
        }

        return 0;
    }
}

public sealed class ConfigEngineShowCommand(ISettingsService settingsService) : AsyncCommand<ConfigShowCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ConfigShowCommand.Settings settings, CancellationToken cancellationToken)
    {
        settings.Section = "engine";
        return await new ConfigShowCommand(settingsService).ExecuteAsync(context, settings, cancellationToken);
    }
}

public sealed class ConfigAppearanceShowCommand(ISettingsService settingsService) : AsyncCommand<ConfigShowCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ConfigShowCommand.Settings settings, CancellationToken cancellationToken)
    {
        settings.Section = "appearance";
        return await new ConfigShowCommand(settingsService).ExecuteAsync(context, settings, cancellationToken);
    }
}
