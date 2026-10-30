using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Commands.About;
using TaskScheduler.Cli.Commands.Config;
using TaskScheduler.Cli.Commands.Daemon;
using TaskScheduler.Cli.Commands.Dashboard;
using TaskScheduler.Cli.Commands.Format;
using TaskScheduler.Cli.Commands.Log;
using TaskScheduler.Cli.Commands.Task;
using TaskScheduler.Cli.Commands.Tool;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        if (Console.IsOutputRedirected)
        {
            AnsiConsole.Profile.Width = 140;
        }

        var services = new ServiceCollection();
        var configuration = BuildConfiguration();
        services.AddSingleton<IConfiguration>(configuration);

        var isVerbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);
        var isDaemon = args.FirstOrDefault(a => !a.StartsWith('-'))?.Equals("daemon", StringComparison.OrdinalIgnoreCase) == true;
        var isJsonMode = args.Contains("--json", StringComparer.OrdinalIgnoreCase);

        services.AddSerilog((_, loggerConfig) =>
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "Logs", "cli-log-.txt");
            loggerConfig
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30);

            if ((isVerbose || isDaemon) && !isJsonMode)
            {
                loggerConfig.WriteTo.Console(
                    outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
            }
            else if (isVerbose && isJsonMode)
            {
                loggerConfig.WriteTo.Console(
                    standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose,
                    outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
            }
        });

        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog();
        });

        services.AddTaskScheduler(configuration);

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("tscli");
            config.SetApplicationVersion(AppMetadata.Version);

            config.SetInterceptor(new StrictCommandInterceptor());

            config.SetExceptionHandler((ex, _) =>
            {
                var isJson = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
                CliOutputFormatter.PrintError(ex.Message, isJson);
                if (ex is CommandParseException or ArgumentException or FormatException)
                {
                    return 2;
                }
                return 1;
            });

            // 1. 仪表盘模块
            config.AddCommand<DashboardCommand>("dashboard")
                .WithAlias("status")
                .WithDescription("查看任务统计概览、即将执行的任务与最近活动记录")
                .WithExample("dashboard")
                .WithExample("dashboard", "--json");

            // 2. 任务管理模块
            config.AddBranch("task", task =>
            {
                task.SetDescription("定时任务管理（列表、查看、新建、编辑、运行、暂停、恢复、删除、测试、导入导出、统计）");

                task.AddCommand<TaskListCommand>("list")
                    .WithAlias("ls")
                    .WithDescription("列出所有定时任务，支持按分组、状态过滤与关键词搜索")
                    .WithExample("task", "list")
                    .WithExample("task", "list", "--state", "Normal", "--search", "备份");

                task.AddCommand<TaskViewCommand>("view")
                    .WithAlias("info")
                    .WithAlias("show")
                    .WithDescription("查看指定任务的完整配置、命令脚本、执行统计与最近日志")
                    .WithExample("task", "view", "每日备份", "-g", "DEFAULT");

                task.AddCommand<TaskCreateCommand>("create")
                    .WithAlias("add")
                    .WithDescription("新建定时任务（支持命令行参数快速创建或 -i 交互式向导）")
                    .WithExample("task", "create", "-n", "清理缓存", "--trigger-type", "Cron", "--cron", "0 0 2 * * ?", "--cmd-type", "PowerShell", "--cmd", "Write-Output 'Done'")
                    .WithExample("task", "create", "-i");

                task.AddCommand<TaskUpdateCommand>("update")
                    .WithAlias("edit")
                    .WithDescription("更新已有任务的属性、触发器规则或命令内容")
                    .WithExample("task", "update", "清理缓存", "--cron", "0 30 3 * * ?", "-p", "High");

                task.AddCommand<TaskRunCommand>("run")
                    .WithAlias("trigger")
                    .WithDescription("立即触发任务执行一次")
                    .WithExample("task", "run", "清理缓存")
                    .WithExample("task", "run", "清理缓存", "--wait");

                task.AddCommand<TaskPauseCommand>("pause")
                    .WithDescription("暂停指定任务")
                    .WithExample("task", "pause", "清理缓存");

                task.AddCommand<TaskResumeCommand>("resume")
                    .WithDescription("恢复已暂停的任务")
                    .WithExample("task", "resume", "清理缓存");

                task.AddCommand<TaskToggleCommand>("toggle")
                    .WithDescription("切换任务的启用/暂停状态")
                    .WithExample("task", "toggle", "清理缓存");

                task.AddCommand<TaskDeleteCommand>("delete")
                    .WithAlias("rm")
                    .WithDescription("删除指定任务及其关联触发器")
                    .WithExample("task", "delete", "清理缓存", "-y");

                task.AddCommand<TaskTestRunCommand>("test-run")
                    .WithAlias("test")
                    .WithDescription("测试执行脚本命令并输出退出码、标准输出与标准错误（不写入任务历史）")
                    .WithExample("task", "test-run", "--type", "PowerShell", "--cmd", "Get-Date")
                    .WithExample("task", "test-run", "--name", "清理缓存");

                task.AddCommand<TaskExportCommand>("export")
                    .WithDescription("导出任务为 JSON 文件、输出到终端或复制到系统剪贴板")
                    .WithExample("task", "export", "清理缓存", "-o", "backup.json")
                    .WithExample("task", "export", "清理缓存", "--clipboard");

                task.AddCommand<TaskImportCommand>("import")
                    .WithDescription("从 JSON 文件、JSON 字符串或系统剪贴板导入任务")
                    .WithExample("task", "import", "backup.json", "--overwrite")
                    .WithExample("task", "import", "--clipboard");

                task.AddCommand<TaskCopyCommandCommand>("copy-cmd")
                    .WithAlias("cp-cmd")
                    .WithAlias("copy-command")
                    .WithDescription("复制指定任务的命令脚本内容到系统剪贴板（与 UI 复制命令按钮功能一致）")
                    .WithExample("task", "copy-cmd", "清理缓存");

                task.AddCommand<TaskStatsCommand>("stats")
                    .WithDescription("查看指定任务的累计执行统计指标")
                    .WithExample("task", "stats", "清理缓存");

                task.AddCommand<TaskLogsCommand>("logs")
                    .WithDescription("查看指定任务的历史执行日志")
                    .WithExample("task", "logs", "清理缓存", "-n", "20");
            });

            // 3. 执行日志模块
            config.AddBranch("log", log =>
            {
                log.SetDescription("任务执行历史日志管理（分页查询、详情查看、最近记录、导出与复制）");

                log.AddCommand<LogListCommand>("list")
                    .WithAlias("ls")
                    .WithDescription("分页查询任务执行历史日志，支持按状态、任务名和关键词过滤")
                    .WithExample("log", "list", "--page", "1", "--size", "20", "--status", "Failed");

                log.AddCommand<LogViewCommand>("view")
                    .WithAlias("show")
                    .WithDescription("查看单条执行日志的完整标准输出与错误输出")
                    .WithExample("log", "view", "1");

                log.AddCommand<LogRecentCommand>("recent")
                    .WithDescription("快速查看最近 N 条任务执行日志")
                    .WithExample("log", "recent", "-n", "10");

                log.AddCommand<LogExportCommand>("export")
                    .WithDescription("导出指定执行日志到文件或复制到系统剪贴板")
                    .WithExample("log", "export", "1", "-o", "exec.log")
                    .WithExample("log", "export", "1", "--clipboard");
            });

            // 4. 工具配置模块
            config.AddBranch("tool", tool =>
            {
                tool.SetDescription("解释器工具环境管理（查看、添加、编辑、删除、设置默认版本）");

                tool.AddCommand<ToolListCommand>("list")
                    .WithAlias("ls")
                    .WithDescription("列出已配置的解释器工具版本")
                    .WithExample("tool", "list")
                    .WithExample("tool", "list", "--type", "Python");

                tool.AddCommand<ToolAddCommand>("add")
                    .WithDescription("添加新的解释器工具版本配置")
                    .WithExample("tool", "add", "-t", "Python", "-v", "Python 3.12", "-p", "C:\\Python312\\python.exe", "--default");

                tool.AddCommand<ToolUpdateCommand>("update")
                    .WithAlias("edit")
                    .WithDescription("更新指定 ID 的工具版本配置")
                    .WithExample("tool", "update", "1", "-v", "Python 3.12.8");

                tool.AddCommand<ToolDeleteCommand>("delete")
                    .WithAlias("rm")
                    .WithDescription("删除指定 ID 的工具配置")
                    .WithExample("tool", "delete", "1", "-y");

                tool.AddCommand<ToolSetDefaultCommand>("set-default")
                    .WithAlias("default")
                    .WithDescription("将指定 ID 的工具版本设为该类型的默认版本")
                    .WithExample("tool", "set-default", "1");
            });

            // 5. 全局设置模块
            config.AddBranch("config", cfg =>
            {
                cfg.SetDescription("引擎与外观全局设置管理（查看、修改引擎参数、修改外观设置、恢复默认）");

                cfg.AddCommand<ConfigShowCommand>("show")
                    .WithAlias("get")
                    .WithAlias("list")
                    .WithDescription("查看当前的引擎与外观设置")
                    .WithExample("config", "show");

                cfg.AddBranch("engine", eng =>
                {
                    eng.SetDescription("调度引擎设置管理（查看或修改）");
                    eng.AddCommand<ConfigEngineSetCommand>("set")
                        .WithDescription("修改调度引擎配置（日志保留天数、最大并发线程、开机自启、启动重调度等）")
                        .WithExample("config", "engine", "set", "--retention-days", "60", "--max-threads", "16", "--startup", "true");
                    eng.AddCommand<ConfigEngineShowCommand>("show")
                        .WithDescription("查看调度引擎配置")
                        .WithExample("config", "engine", "show");
                });

                cfg.AddCommand<ConfigEngineSetCommand>("engine-set")
                    .WithDescription("修改调度引擎配置（快捷命令）")
                    .WithExample("config", "engine-set", "--retention-days", "60");

                cfg.AddBranch("appearance", app =>
                {
                    app.SetDescription("外观主题设置管理（查看或修改）");
                    app.AddCommand<ConfigAppearanceSetCommand>("set")
                        .WithDescription("修改外观主题配置（ThemeMode 与 CompactMode）")
                        .WithExample("config", "appearance", "set", "--theme", "Dark", "--compact", "true");
                    app.AddCommand<ConfigAppearanceShowCommand>("show")
                        .WithDescription("查看外观主题配置")
                        .WithExample("config", "appearance", "show");
                });

                cfg.AddCommand<ConfigAppearanceSetCommand>("appearance-set")
                    .WithDescription("修改外观主题配置（快捷命令）")
                    .WithExample("config", "appearance-set", "--theme", "Dark");

                cfg.AddCommand<ConfigResetCommand>("reset")
                    .WithDescription("将引擎与外观设置恢复为系统默认值")
                    .WithExample("config", "reset", "-y");

                cfg.AddBranch("formatter", formatter =>
                {
                    formatter.SetDescription("脚本 formatter 路径与超时设置");
                    formatter.AddCommand<ConfigFormatterShowCommand>("show")
                        .WithDescription("显示 formatter 设置和环境可用性")
                        .WithExample("config", "formatter", "show", "--json");
                    formatter.AddCommand<ConfigFormatterSetCommand>("set")
                        .WithDescription("设置 formatter 路径或超时")
                        .WithExample("config", "formatter", "set", "--lang", "Bash", "--path", "C:\\tools\\shfmt.exe")
                        .WithExample("config", "formatter", "set", "--timeout-seconds", "20");
                    formatter.AddCommand<ConfigFormatterClearCommand>("clear")
                        .WithDescription("清除指定语言的 formatter 路径覆盖")
                        .WithExample("config", "formatter", "clear", "--lang", "Python");
                    formatter.AddCommand<ConfigFormatterTestCommand>("test")
                        .WithDescription("检测 formatter、运行时版本和可用状态")
                        .WithExample("config", "formatter", "test", "--lang", "Node.js", "--json");
                });
            });

            // 6. 无头守护进程模块
            config.AddBranch("daemon", daemon =>
            {
                daemon.SetDescription("无头常驻调度引擎守护进程管理");

                daemon.AddCommand<DaemonRunCommand>("run")
                    .WithAlias("start")
                    .WithDescription("在当前终端前台常驻运行 Quartz 调度引擎（与桌面端同等调度能力）")
                    .WithExample("daemon", "run");
            });

            // 7. 关于与版本信息
            config.AddCommand<AboutCommand>("about")
                .WithAlias("version")
                .WithDescription("查看版本、运行时环境、数据库路径及调度引擎状态")
                .WithExample("about")
                .WithExample("about", "--json");

            config.AddCommand<FormatCommand>("format")
                .WithDescription("使用本机可用的 formatter 格式化脚本文本")
                .WithExample("format", "--lang", "Python", "--file", "script.py")
                .WithExample("format", "--lang", "Bash", "--json");
        });

        try
        {
            return await app.RunAsync(args);
        }
        finally
        {
            await Serilog.Log.CloseAndFlushAsync();
        }
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var baseDir = AppContext.BaseDirectory;
        return new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }
}
