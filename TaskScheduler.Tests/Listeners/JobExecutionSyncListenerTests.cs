using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using TaskScheduler.Core.Listeners;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Tests.Listeners;

[TestFixture]
public class JobExecutionSyncListenerTests
{
    private class FakeIpcSyncService : IIpcSyncService
    {
#pragma warning disable CS0067
        public event Action<SyncMessage>? MessageReceived;
#pragma warning restore CS0067
        public List<SyncMessage> Notifications { get; } = new();
        public TaskCompletionSource<SyncMessage> NotificationTcs { get; } = new();

        public void StartServer() { }

        public Task NotifyAsync(SyncEventType type, string? jobName = null, string? jobGroup = null, string? key = null, CancellationToken ct = default)
        {
            var msg = new SyncMessage { Type = type, JobName = jobName, JobGroup = jobGroup, Key = key };
            Notifications.Add(msg);
            NotificationTcs.TrySetResult(msg);
            return Task.CompletedTask;
        }

        public Task<bool> RequestTriggerJobNowAsync(string jobName, string jobGroup, CancellationToken ct = default) => Task.FromResult(true);

        public void Dispose() { }
    }

    public class SimpleTestJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task JobWasExecuted_BroadcastsTasksChangedNotification_AfterExecution()
    {
        var fakeIpc = new FakeIpcSyncService();
        var listener = new JobExecutionSyncListener(fakeIpc, NullLogger<JobExecutionSyncListener>.Instance);

        Assert.That(listener.Name, Is.EqualTo(JobExecutionSyncListener.ListenerName));

        var schedulerFactory = new StdSchedulerFactory();
        var scheduler = await schedulerFactory.GetScheduler();
        try
        {
            scheduler.ListenerManager.AddJobListener(listener, GroupMatcher<JobKey>.AnyGroup());

            var jobKey = new JobKey("TestSyncJob", "TEST_GROUP");
            var jobDetail = JobBuilder.Create<SimpleTestJob>()
                .WithIdentity(jobKey)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity("TestSyncTrigger", "TEST_GROUP")
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(jobDetail, trigger);
            await scheduler.Start();

            // 等待执行完成及后台 250ms 延时广播
            var completedTask = await Task.WhenAny(fakeIpc.NotificationTcs.Task, Task.Delay(2000));

            Assert.That(completedTask, Is.EqualTo(fakeIpc.NotificationTcs.Task), "超时未收到 TasksChanged 通知");
            var received = await fakeIpc.NotificationTcs.Task;

            Assert.That(received.Type, Is.EqualTo(SyncEventType.TasksChanged));
            Assert.That(received.JobName, Is.EqualTo("TestSyncJob"));
            Assert.That(received.JobGroup, Is.EqualTo("TEST_GROUP"));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }
}
