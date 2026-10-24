using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

public interface ICommandExecutor
{
    /// <summary>
    /// 根据命令模型执行命令（自动感知其内容、类型、解释器与执行模式）。
    /// </summary>
    Task<CommandResult> ExecuteCommandAsync(
        CommandModel model,
        CancellationToken ct = default,
        string? envVariables = null);

    /// <summary>
    /// 基础执行接口，支持指定执行模式（<see cref="ScriptModes.Inline"/> 或 <see cref="ScriptModes.File"/>）。
    /// </summary>
    Task<CommandResult> ExecuteCommandAsync(
        string command,
        string type,
        string? interpreter = null,
        CancellationToken ct = default,
        string? envVariables = null,
        string? scriptMode = null);
}
