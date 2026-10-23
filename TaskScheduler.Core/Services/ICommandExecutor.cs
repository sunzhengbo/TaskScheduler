using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

public interface ICommandExecutor
{
    Task<CommandResult> ExecuteCommandAsync(
        string command,
        string type,
        string? interpreterPath = null,
        CancellationToken ct = default,
        string? envVariables = null);
}
