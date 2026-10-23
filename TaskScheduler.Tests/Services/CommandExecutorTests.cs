using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Tests.Services;

[TestFixture]
public class CommandExecutorTests
{
    private CommandExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _executor = new CommandExecutor(NullLogger<CommandExecutor>.Instance);
    }

    [Test]
    public async Task ExecuteCommandAsync_CmdEcho_ExecutesAndCapturesOutput()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = await _executor.ExecuteCommandAsync("echo test_output_123", CommandTypes.Cmd);

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("test_output_123"));
    }

    [Test]
    public async Task ExecuteCommandAsync_PowerShellWriteOutput_ExecutesSuccessfully()
    {
        var result = await _executor.ExecuteCommandAsync("Write-Output 'ps_hello'", CommandTypes.PowerShell);

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("ps_hello"));
    }

    [Test]
    public async Task ExecuteCommandAsync_NonZeroExitCode_CapturesFailure()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = await _executor.ExecuteCommandAsync("exit 42", CommandTypes.Cmd);

        Assert.That(result.ExitCode, Is.EqualTo(42));
    }
}
