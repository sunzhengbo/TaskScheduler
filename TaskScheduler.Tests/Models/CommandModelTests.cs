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
}
