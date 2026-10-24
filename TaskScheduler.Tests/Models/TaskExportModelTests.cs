using NUnit.Framework;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Tests.Models;

[TestFixture]
public class TaskExportModelTests
{
    [Test]
    public void TaskExportModel_RoundtripSerialization_PreservesAllFieldsIncludingPriority()
    {
        var model = new TaskExportModel
        {
            Version = TaskExportModel.CurrentVersion,
            Name = "备份数据",
            Group = "DATABASE",
            Description = "每日凌晨自动备份数据库",
            TriggerType = TriggerType.Cron,
            CronExpression = "0 0 2 * * ?",
            UseBootTime = false,
            Priority = TaskPriority.High,
            Command = new CommandExportModel
            {
                Name = "执行备份脚本",
                Type = CommandTypes.PowerShell,
                Content = "Backup-SqlDatabase -ServerInstance localhost",
                InterpreterVersion = "pwsh (C:\\Program Files\\PowerShell\\7\\pwsh.exe)",
                Description = "PowerShell 备份脚本"
            }
        };

        var json = model.ToJson();
        Assert.That(json, Does.Contain("\"priority\": \"High\""));
        Assert.That(json, Does.Contain("\"version\": 2"));

        var deserialized = TaskExportModel.FromJson(json);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Name, Is.EqualTo(model.Name));
        Assert.That(deserialized.Group, Is.EqualTo(model.Group));
        Assert.That(deserialized.Description, Is.EqualTo(model.Description));
        Assert.That(deserialized.TriggerType, Is.EqualTo(model.TriggerType));
        Assert.That(deserialized.CronExpression, Is.EqualTo(model.CronExpression));
        Assert.That(deserialized.Priority, Is.EqualTo(TaskPriority.High));
        Assert.That(deserialized.Command.Content, Is.EqualTo(model.Command.Content));
    }

    [Test]
    public void TaskExportModel_FromJson_LegacyJsonWithoutPriority_DefaultsToNormal()
    {
        var legacyJson = """
            {
                "name": "旧版任务",
                "group": "DEFAULT",
                "description": "无 priority 字段的旧版导出文件",
                "triggerType": "Simple",
                "repeatCount": -1,
                "repeatInterval": "00:10:00",
                "cronExpression": "",
                "useBootTime": false,
                "command": {
                    "name": "测试命令",
                    "type": "Cmd",
                    "content": "echo test"
                }
            }
            """;

        var deserialized = TaskExportModel.FromJson(legacyJson);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Name, Is.EqualTo("旧版任务"));
        Assert.That(deserialized.Priority, Is.EqualTo(TaskPriority.Normal));
    }

    [Test]
    public void TaskExportModel_FromJson_InvalidJson_ReturnsNull()
    {
        var invalidJson = "{ corrupted json ...";
        var result = TaskExportModel.FromJson(invalidJson);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TaskExportModel_FromTask_ExtractsScheduledTaskDetailAndCommandProperly()
    {
        var scheduledTask = new ScheduledTaskDetail
        {
            Name = "清理临时文件",
            Group = "SYSTEM",
            Description = "清理系统缓存",
            Priority = TaskPriority.Low,
            UseBootTime = true,
            Triggers =
            {
                new TriggerInfo
                {
                    Type = TriggerType.Simple,
                    RepeatCount = -1,
                    RepeatInterval = TimeSpan.FromMinutes(30)
                }
            }
        };

        var command = new CommandModel
        {
            Name = "清理",
            Type = CommandTypes.Cmd,
            Content = "del /q /f C:\\temp\\*.*",
            InterpreterVersion = "cmd.exe"
        };
        var commandJson = System.Text.Json.JsonSerializer.Serialize(command);

        var exportModel = TaskExportModel.FromTask(scheduledTask, commandJson);

        Assert.That(exportModel.Name, Is.EqualTo("清理临时文件"));
        Assert.That(exportModel.Group, Is.EqualTo("SYSTEM"));
        Assert.That(exportModel.Priority, Is.EqualTo(TaskPriority.Low));
        Assert.That(exportModel.UseBootTime, Is.True);
        Assert.That(exportModel.TriggerType, Is.EqualTo(TriggerType.Simple));
        Assert.That(exportModel.RepeatInterval, Is.EqualTo("00:30:00"));
        Assert.That(exportModel.Command.Content, Is.EqualTo("del /q /f C:\\temp\\*.*"));
    }

    [Test]
    public void TaskExportModel_RoundtripSerialization_PreservesScriptMode()
    {
        var model = new TaskExportModel
        {
            Name = "脚本文件任务",
            Command = new CommandExportModel
            {
                Name = "运行脚本",
                Type = CommandTypes.CSharp,
                Content = "D:\\scripts\\backup.cs",
                ScriptMode = ScriptModes.File
            }
        };

        var json = model.ToJson();
        Assert.That(json, Does.Contain("\"scriptMode\": \"File\""));

        var deserialized = TaskExportModel.FromJson(json);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Command.ScriptMode, Is.EqualTo(ScriptModes.File));
    }
}
