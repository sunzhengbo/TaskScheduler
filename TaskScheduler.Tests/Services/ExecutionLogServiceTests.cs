using NUnit.Framework;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;
using TaskScheduler.Tests.Fixtures;

namespace TaskScheduler.Tests.Services;

[TestFixture]
public class ExecutionLogServiceTests
{
    private TestDatabaseFixture _fixture = null!;
    private ExecutionLogService _logService = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new TestDatabaseFixture();
        _logService = new ExecutionLogService(_fixture.DatabaseProvider);
    }

    [TearDown]
    public void TearDown()
    {
        _fixture.Dispose();
    }

    [Test]
    public async Task GetStatisticsAsync_WhenNoLogsExist_ReturnsZeroStatsWithoutNullReferenceOrOrdinalException()
    {
        // 关键缺陷回归测试：当数据库中无执行历史时，SUM(CASE...) 返回 NULL
        // 代码必须能够正确防御并返回 0，不得抛出 The data is NULL at ordinal 1 异常
        var stats = await _logService.GetStatisticsAsync("不存在的任务", "DEFAULT");

        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalExecutions, Is.EqualTo(0));
        Assert.That(stats.SuccessCount, Is.EqualTo(0));
        Assert.That(stats.FailCount, Is.EqualTo(0));
        Assert.That(stats.AverageDurationMs, Is.EqualTo(0));
        Assert.That(stats.SuccessRate, Is.EqualTo(0.0));
        Assert.That(stats.SuccessRateDisplay, Does.StartWith("0.0%"));
        Assert.That(stats.LastExecutionTime, Is.Null);
        Assert.That(stats.LastStatus, Is.Null);
    }

    [Test]
    public async Task RecordExecutionAsync_And_GetLogByIdAsync_StoresAndRetrievesLogAccurately()
    {
        var log = new ExecutionLog
        {
            JobName = "测试任务",
            JobGroup = "TEST_GROUP",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow.AddMinutes(-4),
            DurationMs = 60000,
            ExitCode = 0,
            Status = ExecutionStatus.Success,
            Output = "任务成功输出信息",
            Error = null,
            Remark = "手动触发"
        };

        await _logService.RecordExecutionAsync(log);

        var list = await _logService.GetLogsAsync("测试任务", "TEST_GROUP", null, 1, 10);
        Assert.That(list.Count, Is.EqualTo(1));

        var recorded = list[0];
        Assert.That(recorded.JobName, Is.EqualTo("测试任务"));
        Assert.That(recorded.JobGroup, Is.EqualTo("TEST_GROUP"));
        Assert.That(recorded.DurationMs, Is.EqualTo(60000));
        Assert.That(recorded.ExitCode, Is.EqualTo(0));
        Assert.That(recorded.Status, Is.EqualTo(ExecutionStatus.Success));
        Assert.That(recorded.Output, Is.EqualTo("任务成功输出信息"));

        var byId = await _logService.GetLogByIdAsync(recorded.Id);
        Assert.That(byId, Is.Not.Null);
        Assert.That(byId!.Id, Is.EqualTo(recorded.Id));
        Assert.That(byId.JobName, Is.EqualTo("测试任务"));
    }

    [Test]
    public async Task GetStatisticsAsync_WithMultipleExecutions_CalculatesMetricsAccurately()
    {
        var baseTime = DateTime.UtcNow.AddHours(-1);

        // 插入 2 次成功，1 次失败
        await _logService.RecordExecutionAsync(new ExecutionLog
        {
            JobName = "复杂统计任务",
            JobGroup = "DEFAULT",
            StartTime = baseTime,
            EndTime = baseTime.AddSeconds(1),
            DurationMs = 1000,
            ExitCode = 0,
            Status = ExecutionStatus.Success
        });

        await _logService.RecordExecutionAsync(new ExecutionLog
        {
            JobName = "复杂统计任务",
            JobGroup = "DEFAULT",
            StartTime = baseTime.AddMinutes(10),
            EndTime = baseTime.AddMinutes(10).AddSeconds(3),
            DurationMs = 3000,
            ExitCode = 0,
            Status = ExecutionStatus.Success
        });

        await _logService.RecordExecutionAsync(new ExecutionLog
        {
            JobName = "复杂统计任务",
            JobGroup = "DEFAULT",
            StartTime = baseTime.AddMinutes(20),
            EndTime = baseTime.AddMinutes(20).AddSeconds(2),
            DurationMs = 2000,
            ExitCode = 1,
            Status = ExecutionStatus.Failed,
            Error = "脚本执行出错"
        });

        var stats = await _logService.GetStatisticsAsync("复杂统计任务", "DEFAULT");

        Assert.That(stats.TotalExecutions, Is.EqualTo(3));
        Assert.That(stats.SuccessCount, Is.EqualTo(2));
        Assert.That(stats.FailCount, Is.EqualTo(1));
        Assert.That(stats.AverageDurationMs, Is.EqualTo(2000)); // (1000 + 3000 + 2000) / 3 = 2000
        Assert.That(stats.SuccessRate, Is.EqualTo(200.0 / 3.0).Within(0.01));
        Assert.That(stats.SuccessRateDisplay, Does.StartWith("66.7%"));
        Assert.That(stats.LastStatus, Is.EqualTo(ExecutionStatus.Failed));
    }

    [Test]
    public async Task GetLogsAsync_FilteringAndPagination_WorksAsExpected()
    {
        for (var i = 1; i <= 5; i++)
        {
            await _logService.RecordExecutionAsync(new ExecutionLog
            {
                JobName = "分页任务",
                JobGroup = "DEFAULT",
                StartTime = DateTime.UtcNow.AddMinutes(i),
                DurationMs = i * 100,
                ExitCode = i % 2 == 0 ? 1 : 0,
                Status = i % 2 == 0 ? ExecutionStatus.Failed : ExecutionStatus.Success
            });
        }

        // 分页获取前 2 条
        var page1 = await _logService.GetLogsAsync("分页任务", "DEFAULT", null, 1, 2);
        Assert.That(page1.Count, Is.EqualTo(2));

        // 按状态过滤 Failed
        var failedList = await _logService.GetLogsAsync("分页任务", "DEFAULT", ExecutionStatus.Failed.ToString(), 1, 10);
        Assert.That(failedList.Count, Is.EqualTo(2));
        Assert.That(failedList.All(x => x.Status == ExecutionStatus.Failed), Is.True);

        // 获取记录总数
        var count = await _logService.GetLogCountAsync("分页任务", "DEFAULT", null);
        Assert.That(count, Is.EqualTo(5));
    }

    [Test]
    public async Task GetRecentLogsAsync_And_GetDashboardStatsAsync_WorksAsExpected()
    {
        await _logService.RecordExecutionAsync(new ExecutionLog
        {
            JobName = "仪表盘测试任务",
            JobGroup = "DEFAULT",
            StartTime = DateTime.UtcNow.AddMinutes(-10),
            Status = ExecutionStatus.Success
        });

        await _logService.RecordExecutionAsync(new ExecutionLog
        {
            JobName = "仪表盘测试任务",
            JobGroup = "DEFAULT",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            Status = ExecutionStatus.Failed
        });

        var recent = await _logService.GetRecentLogsAsync(5);
        Assert.That(recent.Count, Is.EqualTo(2));

        var dashboard = await _logService.GetDashboardStatsAsync();
        Assert.That(dashboard, Is.Not.Null);
        Assert.That(dashboard.TotalCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(dashboard.FailedCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task GetLatestLogsForAllTasksAsync_ReturnsLatestLogPerTask()
    {
        // 任务 A 的两次执行
        await _logService.RecordExecutionAsync(new ExecutionLog
        {
            JobName = "TaskA",
            JobGroup = "GRP1",
            StartTime = DateTime.UtcNow.AddMinutes(-20),
            Status = ExecutionStatus.Success,
            DurationMs = 100
        });
        await _logService.RecordExecutionAsync(new ExecutionLog
        {
            JobName = "TaskA",
            JobGroup = "GRP1",
            StartTime = DateTime.UtcNow.AddMinutes(-10),
            Status = ExecutionStatus.Failed,
            Error = "TaskA error",
            DurationMs = 200
        });

        // 任务 B 的一次执行
        await _logService.RecordExecutionAsync(new ExecutionLog
        {
            JobName = "TaskB",
            JobGroup = "GRP2",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            Status = ExecutionStatus.Success,
            DurationMs = 300
        });

        var latestMap = await _logService.GetLatestLogsForAllTasksAsync();
        Assert.That(latestMap, Does.ContainKey("GRP1.TaskA"));
        Assert.That(latestMap, Does.ContainKey("GRP2.TaskB"));

        Assert.That(latestMap["GRP1.TaskA"].Status, Is.EqualTo(ExecutionStatus.Failed));
        Assert.That(latestMap["GRP1.TaskA"].Error, Is.EqualTo("TaskA error"));
        Assert.That(latestMap["GRP2.TaskB"].Status, Is.EqualTo(ExecutionStatus.Success));

        var singleLog = await _logService.GetLatestLogAsync("TaskA", "GRP1");
        Assert.That(singleLog, Is.Not.Null);
        Assert.That(singleLog!.Status, Is.EqualTo(ExecutionStatus.Failed));
    }
}
