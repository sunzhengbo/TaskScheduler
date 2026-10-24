using NUnit.Framework;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Tests.Models;

[TestFixture]
public class CommandModelTests
{
    [TestCase("Python 3.12 (C:\\Python312\\python.exe)", "C:\\Python312\\python.exe")]
    [TestCase("Node.js v20 (D:\\nodejs\\node.exe)", "D:\\nodejs\\node.exe")]
    [TestCase("C:\\Windows\\System32\\cmd.exe", "C:\\Windows\\System32\\cmd.exe")]
    [TestCase("pwsh", "pwsh")]
    public void InterpreterPath_ExtractsExecutablePathFromVersionString(string input, string expected)
    {
        var model = new CommandModel
        {
            InterpreterVersion = input
        };

        Assert.That(model.InterpreterPath, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void InterpreterPath_NullOrEmpty_ReturnsNull(string? input)
    {
        var model = new CommandModel
        {
            InterpreterVersion = input
        };

        Assert.That(model.InterpreterPath, Is.Null);
    }

    [Test]
    public void DeserializeFromJson_SingleCommandJson_ParsesCorrectly()
    {
        var json = """
            {
                "Name": "单命令测试",
                "Type": "PowerShell",
                "Content": "Get-Process",
                "InterpreterVersion": "pwsh"
            }
            """;

        var result = CommandModel.DeserializeFromJson(json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("单命令测试"));
        Assert.That(result.Type, Is.EqualTo("PowerShell"));
        Assert.That(result.Content, Is.EqualTo("Get-Process"));
    }

    [Test]
    public void DeserializeFromJson_LegacyArrayJson_ExtractsFirstCommand()
    {
        var json = """
            [
                {
                    "Name": "首个命令",
                    "Type": "Cmd",
                    "Content": "echo first"
                },
                {
                    "Name": "次要命令",
                    "Type": "Cmd",
                    "Content": "echo second"
                }
            ]
            """;

        var result = CommandModel.DeserializeFromJson(json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("首个命令"));
        Assert.That(result.Content, Is.EqualTo("echo first"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("invalid_json")]
    public void DeserializeFromJson_InvalidOrEmpty_ReturnsNull(string json)
    {
        var result = CommandModel.DeserializeFromJson(json);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ScriptMode_DefaultsToNull_ForLegacyCompatibility()
    {
        var model = new CommandModel();
        Assert.That(model.ScriptMode, Is.Null);
    }

    [Test]
    public void DeserializeFromJson_LegacyWithoutScriptMode_LeavesScriptModeNull()
    {
        var json = """
            {
                "Name": "旧任务",
                "Type": "PowerShell",
                "Content": "Get-Date"
            }
            """;

        var result = CommandModel.DeserializeFromJson(json);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ScriptMode, Is.Null);
    }

    [Test]
    public void DeserializeFromJson_WithExplicitFileMode_ParsesFileMode()
    {
        var json = """
            {
                "Name": "文件任务",
                "Type": "C# (.NET)",
                "Content": "D:\\scripts\\demo.cs",
                "ScriptMode": "File"
            }
            """;

        var result = CommandModel.DeserializeFromJson(json);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ScriptMode, Is.EqualTo(ScriptModes.File));
    }

    [Test]
    public void ScriptModes_ValidationAndNormalization()
    {
        Assert.That(ScriptModes.IsValid("inline"), Is.True);
        Assert.That(ScriptModes.IsValid("File"), Is.True);
        Assert.That(ScriptModes.IsValid("invalid"), Is.False);
        Assert.That(ScriptModes.IsValid(null), Is.False);

        Assert.That(ScriptModes.Normalize("inline"), Is.EqualTo(ScriptModes.Inline));
        Assert.That(ScriptModes.Normalize("FILE"), Is.EqualTo(ScriptModes.File));
        Assert.Throws<ArgumentException>(() => ScriptModes.Normalize("flie"));
    }

    [Test]
    public void ScriptModes_InferLegacyMode_InfersCorrectly()
    {
        // 多行代码一律推断为 Inline
        Assert.That(ScriptModes.InferLegacyMode(CommandTypes.CSharp, "using System;\nConsole.WriteLine();"), Is.EqualTo(ScriptModes.Inline));

        // C# 且单行以 .cs 结尾，即使文件不存在也推断为 File
        Assert.That(ScriptModes.InferLegacyMode(CommandTypes.CSharp, "D:\\non_existent_path\\task.cs"), Is.EqualTo(ScriptModes.File));

        // 其他语言且文件不存在时推断为 Inline
        Assert.That(ScriptModes.InferLegacyMode(CommandTypes.PowerShell, "Get-Date"), Is.EqualTo(ScriptModes.Inline));

        // 真实存在的单行文件推断为 File
        var tempFile = Path.GetTempFileName();
        try
        {
            Assert.That(ScriptModes.InferLegacyMode(CommandTypes.PowerShell, tempFile), Is.EqualTo(ScriptModes.File));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Test]
    public void ScriptModes_NormalizeScriptCommandLine_PreservesArgumentsAndQuotes()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var input = $"{tempFile} arg1 \"arg 2\"";
            var normalized = ScriptModes.NormalizeScriptCommandLine(input);

            Assert.That(normalized, Does.StartWith(tempFile));
            Assert.That(normalized, Does.EndWith("arg1 \"arg 2\""));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }
}
