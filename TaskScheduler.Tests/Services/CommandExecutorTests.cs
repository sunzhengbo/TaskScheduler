using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using TaskScheduler.Cli.Commands.Task;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;
using TaskScheduler.Tests.Fixtures;

namespace TaskScheduler.Tests.Services;

[TestFixture]
public class CommandExecutorTests
{
    private TestDatabaseFixture _fixture = null!;
    private ToolConfigService _toolService = null!;
    private CommandExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new TestDatabaseFixture();
        _toolService = new ToolConfigService(_fixture.DatabaseProvider);
        _executor = new CommandExecutor(NullLogger<CommandExecutor>.Instance, _toolService);
    }

    [TearDown]
    public void TearDown()
    {
        _fixture.Dispose();
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

    [Test]
    public async Task ExecuteCommandAsync_WithInterpreterName_DynamicallyResolvesFromToolConfig()
    {
        // 配置工具 pwshCustom 指向系统当前实际可用的 cmd/powershell
        var actualPath = GetTestPowerShellExecutablePath();
        if (actualPath == null || !File.Exists(actualPath))
            Assert.Ignore("PowerShell executable not found.");

        await _toolService.AddToolAsync(new ToolConfig
        {
            ToolType = CommandTypes.PowerShell,
            VersionName = "pwshCustom",
            ExecutablePath = actualPath,
            IsDefault = false
        });

        // 仅传递解释器名称 "pwshCustom"
        var result = await _executor.ExecuteCommandAsync("Write-Output 'dynamic_resolved'", CommandTypes.PowerShell, interpreter: "pwshCustom");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("dynamic_resolved"));
    }

    [Test]
    public async Task ExecuteCommandAsync_WithObsoletePathSnapshot_SelfHealsByNameFromToolConfig()
    {
        var actualPath = GetTestPowerShellExecutablePath();
        if (actualPath == null || !File.Exists(actualPath))
            Assert.Ignore("PowerShell executable not found.");

        await _toolService.AddToolAsync(new ToolConfig
        {
            ToolType = CommandTypes.PowerShell,
            VersionName = "pwsh",
            ExecutablePath = actualPath,
            IsDefault = false
        });

        // 模拟历史任务中存储了已失效的旧路径 "pwsh (C:\invalid\non_existent\pwsh.exe)"
        var obsoleteSnapshot = @"pwsh (C:\invalid\non_existent\pwsh.exe)";
        var result = await _executor.ExecuteCommandAsync("Write-Output 'self_healed'", CommandTypes.PowerShell, interpreter: obsoleteSnapshot);

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("self_healed"));
    }

    [Test]
    public async Task ExecuteCommandAsync_WithObsoleteVersionNameInSnapshot_FallsBackToDefaultToolInsteadOfSnapshotPath()
    {
        var actualPath = GetTestPowerShellExecutablePath();
        if (actualPath == null || !File.Exists(actualPath))
            Assert.Ignore("PowerShell executable not found.");

        await _toolService.AddToolAsync(new ToolConfig
        {
            ToolType = CommandTypes.PowerShell,
            VersionName = "pwshDefault",
            ExecutablePath = actualPath,
            IsDefault = true
        });

        // 模拟存量任务配置了历史快照 "Powershell5.1 (C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe)"
        // 工具库中只有 pwshDefault（默认工具），无 Powershell5.1
        // 即使快照里的旧路径在磁盘上实际存在，也必须优先自愈回退到工具库默认工具 pwshDefault
        var obsoleteSnapshotWithExistingDiskPath = @"Powershell5.1 (C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe)";
        var result = await _executor.ExecuteCommandAsync(
            "Write-Output 'fallback_to_default_tool'",
            CommandTypes.PowerShell,
            interpreter: obsoleteSnapshotWithExistingDiskPath);

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("fallback_to_default_tool"));
    }

    [Test]
    public async Task ExecuteCommandAsync_WithInvalidPathAndNoToolConfig_FallsBackToSystemPath()
    {
        // 传递一个不存在的路径且库中无对应工具，验证容错回退到系统 PATH 解释器
        var nonExistent = @"C:\completely_bogus_dir\pwsh.exe";
        var result = await _executor.ExecuteCommandAsync("Write-Output 'path_fallback'", CommandTypes.PowerShell, interpreter: nonExistent);

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("path_fallback"));
    }

    [Test]
    public void CommandModel_InterpreterName_ExtractsCorrectly()
    {
        var modelWithSnapshot = new CommandModel
        {
            InterpreterVersion = @"pwsh (D:\data\app\scoop\apps\pwsh\current\pwsh.exe)"
        };
        Assert.That(modelWithSnapshot.InterpreterName, Is.EqualTo("pwsh"));
        Assert.That(modelWithSnapshot.InterpreterPath, Is.EqualTo(@"D:\data\app\scoop\apps\pwsh\current\pwsh.exe"));

        var modelWithNameOnly = new CommandModel
        {
            InterpreterVersion = "pwsh"
        };
        Assert.That(modelWithNameOnly.InterpreterName, Is.EqualTo("pwsh"));
        Assert.That(modelWithNameOnly.InterpreterPath, Is.EqualTo("pwsh"));

        var modelWithCustomPath = new CommandModel
        {
            InterpreterVersion = @"D:\bin\custom_python.exe"
        };
        Assert.That(modelWithCustomPath.InterpreterName, Is.Null);
        Assert.That(modelWithCustomPath.InterpreterPath, Is.EqualTo(@"D:\bin\custom_python.exe"));

        // Windows 包含 (x86) 路径场景：必须保留完整路径，不可误截断为 "C:\Program Files"
        var modelWithX86Path = new CommandModel
        {
            InterpreterVersion = @"C:\Program Files (x86)\Python312\python.exe"
        };
        Assert.That(modelWithX86Path.InterpreterName, Is.Null);
        Assert.That(modelWithX86Path.InterpreterPath, Is.EqualTo(@"C:\Program Files (x86)\Python312\python.exe"));

        // 包含 (x86) 的历史快照格式
        var modelWithX86Snapshot = new CommandModel
        {
            InterpreterVersion = @"Python312 (C:\Program Files (x86)\Python312\python.exe)"
        };
        Assert.That(modelWithX86Snapshot.InterpreterName, Is.EqualTo("Python312"));
        Assert.That(modelWithX86Snapshot.InterpreterPath, Is.EqualTo(@"C:\Program Files (x86)\Python312\python.exe"));
    }

    [Test]
    public async Task ExecuteCommandAsync_WithExplicitValidPathAndNullToolService_ExecutesDirectly()
    {
        var standaloneExecutor = new CommandExecutor(NullLogger<CommandExecutor>.Instance, null);
        var actualPath = GetTestPowerShellExecutablePath();
        if (actualPath == null || !File.Exists(actualPath))
            Assert.Ignore("PowerShell executable not found.");

        var result = await standaloneExecutor.ExecuteCommandAsync(
            "Write-Output 'standalone_ok'",
            CommandTypes.PowerShell,
            interpreter: actualPath);

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("standalone_ok"));
    }

    [Test]
    public void TaskCreateCommand_ResolveInterpreterVersion_WithProgramFilesX86_DoesNotTruncate()
    {
        // 传入包含 (x86) 的路径但未在工具库注册，不应被粗暴截断为 "C:\Program Files"
        var x86Path = @"C:\Program Files (x86)\Python312\python.exe";
        var result = TaskCreateCommand.ResolveInterpreterVersion(CommandTypes.Python, x86Path, []);
        Assert.That(result, Is.EqualTo(x86Path));

        // 传入包含 (x86) 的快照格式且工具库中存在，应正确匹配提取名称
        var tools = new List<ToolConfig>
        {
            new()
            {
                ToolType = CommandTypes.PowerShell,
                VersionName = "pwsh",
                ExecutablePath = @"C:\Program Files (x86)\PowerShell\7\pwsh.exe"
            }
        };
        var snapshot = @"pwsh (C:\Program Files (x86)\PowerShell\7\pwsh.exe)";
        var snapshotResult = TaskCreateCommand.ResolveInterpreterVersion(CommandTypes.PowerShell, snapshot, tools);
        Assert.That(snapshotResult, Is.EqualTo("pwsh"));
    }
    private static string? GetTestPowerShellExecutablePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var winPs = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
            if (File.Exists(winPs)) return winPs;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            var candidates = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };

            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var candidate in candidates)
                {
                    var full = Path.Combine(dir, candidate);
                    if (File.Exists(full)) return full;
                }
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            if (File.Exists("/usr/bin/pwsh")) return "/usr/bin/pwsh";
            if (File.Exists("/usr/local/bin/pwsh")) return "/usr/local/bin/pwsh";
            if (File.Exists("/opt/microsoft/powershell/7/pwsh")) return "/opt/microsoft/powershell/7/pwsh";
        }

        return null;
    }
}
