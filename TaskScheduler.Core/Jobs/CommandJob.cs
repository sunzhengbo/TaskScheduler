using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Core.Jobs;

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

        var jobName = context.JobDetail.Key.Name;
        var jobGroup = context.JobDetail.Key.Group;
        var startTime = DateTime.UtcNow;

        if (string.IsNullOrEmpty(commandJson))
        {
            logger.LogWarning("作业 '{JobKey}' 未配置命令数据 (Command)，跳过执行", context.JobDetail.Key);
            await RecordLogAsync(jobName, jobGroup, startTime, 0, null, ExecutionStatus.Failed, null, "未配置命令数据 (Command)").ConfigureAwait(false);
            return;
        }

        var command = CommandModel.DeserializeFromJson(commandJson);
        if (command == null)
        {
            logger.LogWarning("作业 '{JobKey}' 命令数据格式非法，反序列化失败: {Json}", context.JobDetail.Key, commandJson);
            await RecordLogAsync(jobName, jobGroup, startTime, 0, null, ExecutionStatus.Failed, null, $"命令数据格式非法，反序列化失败: {commandJson}").ConfigureAwait(false);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var overallStatus = ExecutionStatus.Success;
        int? lastExitCode = null;
        string? output = null;
        string? error = null;

        try
        {
            var result = await commandExecutor.ExecuteCommandAsync(
                command.Content,
                command.Type,
                command.InterpreterPath,
                context.CancellationToken).ConfigureAwait(false);
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

        await RecordLogAsync(
            jobName,
            jobGroup,
            startTime,
            stopwatch.ElapsedMilliseconds,
            lastExitCode,
            overallStatus,
            output,
            error).ConfigureAwait(false);
    }

    private async Task RecordLogAsync(
        string jobName,
        string jobGroup,
        DateTime startTime,
        long durationMs,
        int? exitCode,
        ExecutionStatus status,
        string? output,
        string? error)
    {
        try
        {
            var log = new ExecutionLog
            {
                JobName = jobName,
                JobGroup = jobGroup,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                DurationMs = durationMs,
                ExitCode = exitCode,
                Status = status,
                Output = !string.IsNullOrEmpty(output) ? output : null,
                Error = !string.IsNullOrEmpty(error) ? error : null
            };
            await executionLogService.RecordExecutionAsync(log).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "记录执行日志失败");
        }
    }
}
