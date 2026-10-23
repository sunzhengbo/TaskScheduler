using System.ComponentModel;
using Quartz;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Daemon;

public sealed class DaemonRunCommand(
    IServiceProvider serviceProvider,
    ISchedulerFactory schedulerFactory,
    IIpcSyncService ipcSyncService) : AsyncCommand<DaemonRunCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("--no-startup-tasks")]
        [Description("启动守护进程时不主动触发标记为开机启动 (OnStartup) 的任务")]
        public bool NoStartupTasks { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        using var releaseSignal = new ManualResetEventSlim(false);
        var acquiredTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Mutex 具有操作系统线程亲和性，使用专用后台线程在同一线程内完成获取、持有与释放，避免跨 await 切换线程导致遗弃或释放异常
        var mutexThread = new Thread(() =>
        {
            using var mutex = new Mutex(false, SchedulerBootstrapService.SchedulerMutexName);
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
            catch
            {
                acquired = false;
            }

            acquiredTcs.TrySetResult(acquired);
            if (!acquired)
                return;

            try
            {
                releaseSignal.Wait();
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch { }
            }
        })
        {
            IsBackground = true,
            Name = "DaemonMutexOwner"
        };

        mutexThread.Start();
        var isAcquired = await acquiredTcs.Task;

        if (!isAcquired)
        {
            CliOutputFormatter.PrintError("检测到已有调度引擎实例（桌面端 GUI 或另一个 daemon 进程）正在运行，无需重复启动。", settings.Json);
            return 1;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            if (!settings.Json)
            {
                AnsiConsole.MarkupLine("[bold cyan]正在启动 TaskScheduler 无头守护进程调度引擎...[/]");
            }

            ipcSyncService.StartServer();

            await SchedulerBootstrapService.StartSchedulerCoreAsync(
                serviceProvider,
                schedulerFactory,
                triggerStartupTasks: !settings.NoStartupTasks,
                ct: cts.Token);

            if (!settings.Json)
            {
                CliOutputFormatter.PrintSuccess("Quartz 调度引擎已就绪并开始常驻监听（按 Ctrl+C 优雅停止）");
            }
            else
            {
                CliOutputFormatter.WriteJson(new { status = "Running", message = "Scheduler daemon started" });
            }

            try
            {
                await System.Threading.Tasks.Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 收到退出信号
            }

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine("[yellow]收到停止信号，正在等待执行中的作业完成并关闭调度引擎...[/]");
            }

            var scheduler = await schedulerFactory.GetScheduler(CancellationToken.None);
            using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await scheduler.Shutdown(waitForJobsToComplete: true, shutdownCts.Token);

            if (!settings.Json)
            {
                CliOutputFormatter.PrintSuccess("调度引擎已安全关闭");
            }

            return 0;
        }
        finally
        {
            ipcSyncService.Dispose();
            releaseSignal.Set();
            mutexThread.Join(TimeSpan.FromSeconds(2));
        }
    }
}
