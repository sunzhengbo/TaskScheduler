using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

using TaskScheduler.Core.Detectors;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;
using TaskScheduler.Tests.Fixtures;

namespace TaskScheduler.Tests.Services;

[TestFixture]
public partial class CommandExecutorResolveTests
{
    private TestDatabaseFixture _fixture = null!;
    private ToolConfigService _toolService = null!;
    private string _tempDir = null!;
    private string _dummyExe = null!;

    [GeneratedRegex(@"^(.+?)\s*\((.+)\)$")]
    private static partial Regex SnapshotRegex();

    [SetUp]
    public void SetUp()
    {
        _fixture = new TestDatabaseFixture();
        _toolService = new ToolConfigService(_fixture.DatabaseProvider);

        _tempDir = Path.Combine(Path.GetTempPath(), "ts_resolve_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _dummyExe = Path.Combine(_tempDir, OperatingSystem.IsWindows() ? "dummy.exe" : "dummy");
        File.WriteAllText(_dummyExe, "dummy");
    }

    [TearDown]
    public void TearDown()
    {
        _fixture.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private class MockDetectionService : IInterpreterDetectionService
    {
        public string? PathReturn { get; set; }
        public string? ManagerReturn { get; set; }

        public string? SearchInPath(string commandType, string? customCandidate = null) => PathReturn;

        public ValueTask<string?> DetectAsync(string commandType, string? customCandidate = null, CancellationToken ct = default)
        {
            return ValueTask.FromResult(PathReturn ?? ManagerReturn);
        }
    }

    [Test]
    public async Task Resolve_ExplicitRawPath_TakesPrecedenceAndMergesEnv()
    {
        if (!OperatingSystem.IsWindows()) return;

        var psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(psExe)) return;

        // 工具库中配置该路径，附带环境变量 MY_TEST_ENV_VAR=RESOLVED_123
        await _toolService.AddToolAsync(new ToolConfig
        {
            ToolType = "PowerShell",
            VersionName = "custom_pwsh",
            ExecutablePath = psExe,
            EnvVariables = "MY_TEST_ENV_VAR=RESOLVED_123",
            IsDefault = false
        });

        // 同时工具库存在另一个同语言的默认工具（指向 dummy 文件）
        await _toolService.AddToolAsync(new ToolConfig
        {
            ToolType = "PowerShell",
            VersionName = "default_tool",
            ExecutablePath = _dummyExe,
            IsDefault = true
        });

        var executor = new CommandExecutor(NullLogger<CommandExecutor>.Instance, _toolService);

        // 用户显式指定了 psExe 裸物理路径
        // 验证 1：裸物理路径优先于 default_tool 生效（若走 default_tool 会执行 dummy 报错）
        // 验证 2：反查工具库合并对应环境变量成功
        var result = await executor.ExecuteCommandAsync(
            command: "Write-Output $env:MY_TEST_ENV_VAR",
            type: CommandTypes.PowerShell,
            interpreter: psExe);

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("RESOLVED_123"));
    }

    [Test]
    public async Task Resolve_ObsoleteSnapshotVersion_FallsBackToDefaultTool()
    {
        if (!OperatingSystem.IsWindows()) return;

        var psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(psExe)) return;

        await _toolService.AddToolAsync(new ToolConfig
        {
            ToolType = "PowerShell",
            VersionName = "pwsh_default",
            ExecutablePath = psExe,
            IsDefault = true
        });

        var executor = new CommandExecutor(NullLogger<CommandExecutor>.Instance, _toolService);

        // 输入快照格式：旧版本已在工具表中不存在，虽然旧物理路径 _dummyExe 在磁盘真实存在
        // 执行时应坚决自愈回退到 pwsh_default (powershell.exe)，而不是尝试运行 _dummyExe
        var result = await executor.ExecuteCommandAsync(
            command: "Write-Output 'fallback_ok'",
            type: CommandTypes.PowerShell,
            interpreter: $"obsolete_pwsh ({_dummyExe})");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("fallback_ok"));
    }

    [Test]
    public async Task Resolve_ToolTableMatchByName_TakesPrecedence()
    {
        if (!OperatingSystem.IsWindows()) return;

        var psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(psExe)) return;

        await _toolService.AddToolAsync(new ToolConfig
        {
            ToolType = "PowerShell",
            VersionName = "my_custom_tool",
            ExecutablePath = psExe,
            EnvVariables = "CUSTOM_TOOL_VAR=HELLO_NAMED",
            IsDefault = false
        });

        var executor = new CommandExecutor(NullLogger<CommandExecutor>.Instance, _toolService);

        var result = await executor.ExecuteCommandAsync(
            command: "Write-Output $env:CUSTOM_TOOL_VAR",
            type: CommandTypes.PowerShell,
            interpreter: "my_custom_tool");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Output, Does.Contain("HELLO_NAMED"));
    }

    [Test]
    public void Resolve_AllLevelsExhausted_ThrowsFileNotFoundExceptionWithDiagnostics()
    {
        // 构造一个所有探测全部返回 null 的检测服务
        var emptyDetection = new MockDetectionService();
        var executor = new CommandExecutor(NullLogger<CommandExecutor>.Instance, _toolService, emptyDetection);

        // 执行未安装的未知语言类型
        var ex = Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await executor.ExecuteCommandAsync("some_code", "UnknownNonExistentType_999");
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("未检测到「UnknownNonExistentType_999」可用解释器"));
        Assert.That(ex.Message, Does.Contain("排查诊断摘要"));
        Assert.That(ex.Message, Does.Contain("[第 1 级 工具库]"));
        Assert.That(ex.Message, Does.Contain("[第 2 级 系统 PATH]"));
        Assert.That(ex.Message, Does.Contain("[第 3 级 版本工具]"));
        Assert.That(ex.Message, Does.Contain("解决方案"));
    }

    [Test]
    public void SnapshotRegex_WithX86InPath_DoesNotTruncate()
    {
        var input = @"PowerShell (C:\Program Files (x86)\PowerShell\7\pwsh.exe)";
        var match = SnapshotRegex().Match(input);

        Assert.That(match.Success, Is.True);
        Assert.That(match.Groups[1].Value.Trim(), Is.EqualTo("PowerShell"));
        Assert.That(match.Groups[2].Value.Trim(), Is.EqualTo(@"C:\Program Files (x86)\PowerShell\7\pwsh.exe"));
    }
}
