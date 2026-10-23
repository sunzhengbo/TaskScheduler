using System.ComponentModel;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;
using TaskScheduler.Infra.Helpers;

namespace TaskScheduler.Cli.Commands.Config;

public sealed class ConfigEngineSetCommand(ISettingsService settingsService) : AsyncCommand<ConfigEngineSetCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("--retention-days <DAYS>")]
        [Description("设置执行日志保留天数（>= 1）")]
        public int? RetentionDays { get; set; }

        [CommandOption("--max-threads <THREADS>")]
        [Description("设置最大并发线程数（>= 1）")]
        public int? MaxThreads { get; set; }

        [CommandOption("--startup <BOOL>")]
        [Description("设置是否开机自启（true / false）")]
        public bool? StartupEnabled { get; set; }

        [CommandOption("--minimize <BOOL>")]
        [Description("设置开机自启时是否最小化到托盘（true / false）")]
        public bool? StartupMinimize { get; set; }

        [CommandOption("--reschedule|--reschedule-on-startup <BOOL>")]
        [Description("设置启动时是否重新计算 SimpleTrigger 起始时间（true / false）")]
        public bool? RescheduleOnStartup { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var hasChanges = settings.RetentionDays.HasValue ||
                         settings.MaxThreads.HasValue ||
                         settings.StartupEnabled.HasValue ||
                         settings.StartupMinimize.HasValue ||
                         settings.RescheduleOnStartup.HasValue;

        var engineJson = await settingsService.GetValueAsync("engine_settings", cancellationToken);
        var engine = EngineSettings.FromJson(engineJson);

        if (!hasChanges)
        {
            if (settings.Json)
            {
                CliOutputFormatter.WriteJson(engine);
                return 0;
            }

            CliOutputFormatter.PrintInfo("未提供任何需修改的引擎配置参数。使用 --help 查看支持的选项。");
            return 0;
        }

        if (settings.RetentionDays.HasValue)
        {
            if (settings.RetentionDays.Value < 1)
            {
                CliOutputFormatter.PrintError("日志保留天数必须 >= 1", settings.Json);
                return 2;
            }
            engine.LogRetentionDays = settings.RetentionDays.Value;
        }

        if (settings.MaxThreads.HasValue)
        {
            if (settings.MaxThreads.Value < 1)
            {
                CliOutputFormatter.PrintError("最大并发线程数必须 >= 1", settings.Json);
                return 2;
            }
            engine.MaxThreads = settings.MaxThreads.Value;
        }

        if (settings.StartupEnabled.HasValue)
        {
            engine.StartupEnabled = settings.StartupEnabled.Value;
            if (!engine.StartupEnabled)
                engine.StartupMinimize = false;
        }

        if (settings.StartupMinimize.HasValue)
        {
            engine.StartupMinimize = engine.StartupEnabled && settings.StartupMinimize.Value;
        }

        if (settings.RescheduleOnStartup.HasValue)
        {
            engine.RescheduleOnStartup = settings.RescheduleOnStartup.Value;
        }

        await settingsService.SetValueAsync("engine_settings", engine.ToJson(), cancellationToken);

        if (settings.StartupEnabled.HasValue || settings.StartupMinimize.HasValue)
        {
            StartupHelper.SetStartupOnBoot(engine.StartupEnabled, engine.StartupMinimize);
        }

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(engine);
            return 0;
        }

        CliOutputFormatter.PrintSuccess("引擎设置已保存并同步");
        return 0;
    }
}
