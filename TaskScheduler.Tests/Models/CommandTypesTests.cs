using NUnit.Framework;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Tests.Models;

[TestFixture]
public class CommandTypesTests
{
    [Test]
    public void GetConfiguredOrAvailableCommandTypes_WithConfiguredTools_IncludesToolTypes()
    {
        var tools = new List<ToolConfig>
        {
            new() { ToolType = "Python", VersionName = "python312", ExecutablePath = "C:\\Python\\python.exe" }
        };

        var types = CommandTypes.GetConfiguredOrAvailableCommandTypes(tools);

        Assert.That(types, Does.Contain(CommandTypes.Python));
    }

    [Test]
    public void GetConfiguredOrAvailableCommandTypes_PreservesCurrentType()
    {
        // 传入一个极其特殊的非标准类型，验证编辑已有任务时不会丢失该类型
        var customType = "MyCustomLanguage";
        var types = CommandTypes.GetConfiguredOrAvailableCommandTypes(null, customType);

        Assert.That(types, Does.Contain(customType));
    }

    [Test]
    public void DetectSystemInterpreterPath_OnWindows_FindsCmd()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("仅在 Windows 平台测试");

        var cmdPath = CommandTypes.DetectSystemInterpreterPath(CommandTypes.Cmd);
        Assert.That(cmdPath, Is.Not.Null);
        Assert.That(File.Exists(cmdPath), Is.True);
    }

    [Test]
    public void DetectSystemInterpreterPath_FindsNodeJsOnThisMachine()
    {
        var nodePath = CommandTypes.DetectSystemInterpreterPath(CommandTypes.NodeJs);
        TestContext.Out.WriteLine($"Detected Node.js Path: {nodePath}");
        Assert.That(nodePath, Is.Not.Null);
        Assert.That(File.Exists(nodePath), Is.True);
    }

    [Test]
    public void Normalize_HandlesAliases()
    {
        Assert.That(CommandTypes.Normalize("pwsh"), Is.EqualTo(CommandTypes.PowerShell));
        Assert.That(CommandTypes.Normalize("python3"), Is.EqualTo(CommandTypes.Python));
        Assert.That(CommandTypes.Normalize("nodejs"), Is.EqualTo(CommandTypes.NodeJs));
        Assert.That(CommandTypes.Normalize("sh"), Is.EqualTo(CommandTypes.Shell));
        Assert.That(CommandTypes.Normalize("bat"), Is.EqualTo(CommandTypes.Cmd));
    }
}
