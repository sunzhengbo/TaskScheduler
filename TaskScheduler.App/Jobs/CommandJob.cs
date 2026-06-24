using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Quartz;

using TaskScheduler.App.Models;
using TaskScheduler.App.Services;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App.Jobs;

[DisallowConcurrentExecution]
public class CommandJob(
    ICommandExecutor commandExecutor,
    IExecutionLogService executionLogService,
    ILogger<CommandJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;
        string? commandJson = null;
        if (jobData.TryGetValue("Command", out var commandVal))
            commandJson = commandVal?.ToString();
        else if (jobData.TryGetValue("Commands", out var commandsVal))
            commandJson = commandsVal?.ToString();

        if (string.IsNullOrEmpty(commandJson)) return;

        var command = CommandModel.DeserializeFromJson(commandJson);
        if (command == null) return;

        var jobName = context.JobDetail.Key.Name;
        var jobGroup = context.JobDetail.Key.Group;
        var startTime = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var overallStatus = ExecutionStatus.Success;
        int? lastExitCode = null;
        string? output = null;
        string? error = null;

        try
        {
            var result = await commandExecutor.ExecuteCommandAsync(command.Content, command.Type, command.InterpreterPath, context.CancellationToken);
            lastExitCode = result.ExitCode;
            logger.LogInformation("命令 '{CommandName}' 执行完成 (退出码: {ExitCode})", command.Name, result.ExitCode);

            output = result.Output;
            error = result.Error;

            if (!string.IsNullOrEmpty(result.Output))
            {
                logger.LogDebug("命令输出: {Output}", result.Output);
            }

            if (result.ExitCode != 0)
            {
                overallStatus = ExecutionStatus.Failed;
                error = string.IsNullOrEmpty(result.Error)
                    ? $"退出码: {result.ExitCode}"
                    : $"{result.Error}\n退出码: {result.ExitCode}";
            }
        }
        catch (Exception ex)
        {
            overallStatus = ExecutionStatus.Failed;
            error = ex.Message;
            logger.LogError(ex, "命令 '{CommandName}' 执行失败", command.Name);
        }

        stopwatch.Stop();

        try
        {
            var log = new ExecutionLog
            {
                JobName = jobName,
                JobGroup = jobGroup,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ExitCode = lastExitCode,
                Status = overallStatus,
                Output = !string.IsNullOrEmpty(output) ? output : null,
                Error = !string.IsNullOrEmpty(error) ? error : null
            };
            await executionLogService.RecordExecutionAsync(log);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "记录执行日志失败");
        }
    }
}
