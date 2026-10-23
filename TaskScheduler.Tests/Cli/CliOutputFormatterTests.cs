using System.Text.Json;
using NUnit.Framework;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Tests.Cli;

[TestFixture]
public class CliOutputFormatterTests
{
    private class TestPayload
    {
        public string TaskName { get; set; } = string.Empty;
        public ExecutionStatus Status { get; set; }
        public int RetryCount { get; set; }
    }

    [Test]
    public void JsonOptions_SerializesWithCamelCaseAndStringEnums()
    {
        var payload = new TestPayload
        {
            TaskName = "数据归档",
            Status = ExecutionStatus.Success,
            RetryCount = 3
        };

        var json = JsonSerializer.Serialize(payload, CliOutputFormatter.JsonOptions);

        Assert.That(json, Does.Contain("\"taskName\": \"数据归档\""));
        Assert.That(json, Does.Contain("\"status\": \"Success\""));
        Assert.That(json, Does.Contain("\"retryCount\": 3"));
    }

    [Test]
    public void SyncMessage_SerializationAndDeserialization_SupportsAllEventTypes()
    {
        var original = new SyncMessage
        {
            Type = SyncEventType.TriggerJobNow,
            JobName = "执行清理",
            JobGroup = "MAINTENANCE",
            Key = "ManualTrigger"
        };

        var json = JsonSerializer.Serialize(original, CliOutputFormatter.JsonOptions);
        var restored = JsonSerializer.Deserialize<SyncMessage>(json, CliOutputFormatter.JsonOptions);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Type, Is.EqualTo(SyncEventType.TriggerJobNow));
        Assert.That(restored.JobName, Is.EqualTo("执行清理"));
        Assert.That(restored.JobGroup, Is.EqualTo("MAINTENANCE"));
        Assert.That(restored.Key, Is.EqualTo("ManualTrigger"));
    }

    [Test]
    public void FormatTimeAgo_WithUtcAndLocalTime_HandlesCorrectly()
    {
        var nowUtc = DateTime.UtcNow;
        var nowLocal = DateTime.Now;

        var agoUtc = CliOutputFormatter.FormatTimeAgo(nowUtc);
        var agoLocal = CliOutputFormatter.FormatTimeAgo(nowLocal);

        Assert.That(agoUtc, Is.EqualTo("刚刚"));
        Assert.That(agoLocal, Is.EqualTo("刚刚"));

        var fiveMinsAgoUtc = DateTime.UtcNow.AddMinutes(-5);
        var fiveMinsAgoLocal = DateTime.Now.AddMinutes(-5);

        Assert.That(CliOutputFormatter.FormatTimeAgo(fiveMinsAgoUtc), Is.EqualTo("5 分钟前"));
        Assert.That(CliOutputFormatter.FormatTimeAgo(fiveMinsAgoLocal), Is.EqualTo("5 分钟前"));
    }
}
