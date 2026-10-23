using NUnit.Framework;
using Quartz;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Tests.Cron;

[TestFixture]
public class CronExpressionTests
{
    [TestCase("*/5 * * * *", "0 */5 * * * ?")]
    [TestCase("0 12 * * 1-5", "0 0 12 ? * 1-5")]
    [TestCase("30 8 1,15 * *", "0 30 8 1,15 * ?")]
    [TestCase("0 0 1 1 *", "0 0 0 1 1 ?")]
    [TestCase("  */10   *   *   *   *  ", "0 */10 * * * ?")]
    public void NormalizeCronExpression_FivePartUnixCron_ConvertsToValidQuartzCron(string input, string expected)
    {
        var result = TaskSchedulerService.NormalizeCronExpression(input);

        Assert.That(result, Is.EqualTo(expected));
        Assert.That(CronExpression.IsValidExpression(result), Is.True, $"Normalized expression '{result}' should be a valid Quartz Cron expression");
    }

    [TestCase("0 0 12 * * ?")]
    [TestCase("0 */5 * * * ?")]
    [TestCase("0 0 12 1 1 ? 2026")]
    public void NormalizeCronExpression_SixOrSevenPartQuartzCron_KeptUnchanged(string input)
    {
        var result = TaskSchedulerService.NormalizeCronExpression(input);

        Assert.That(result, Is.EqualTo(input));
        Assert.That(CronExpression.IsValidExpression(result), Is.True);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not_a_cron")]
    public void NormalizeCronExpression_InvalidOrEmpty_ReturnsAsIs(string input)
    {
        var result = TaskSchedulerService.NormalizeCronExpression(input);
        Assert.That(result, Is.EqualTo(input));
    }

    [Test]
    public void CronExpressionValidation_QuartzSpecificRules_AccuratelyDifferentiatesValidAndInvalid()
    {
        // Quartz 语法规定：day-of-month 与 day-of-week 不能同时为 *
        Assert.That(CronExpression.IsValidExpression("0 0 12 * * *"), Is.False);

        // 使用问号 ? 满足互斥规则
        Assert.That(CronExpression.IsValidExpression("0 0 12 * * ?"), Is.True);
        Assert.That(CronExpression.IsValidExpression("0 0 12 ? * MON-FRI"), Is.True);

        // 越界时间无效
        Assert.That(CronExpression.IsValidExpression("0 0 25 * * ?"), Is.False); // 小时超出 23
        Assert.That(CronExpression.IsValidExpression("0 60 12 * * ?"), Is.False); // 分钟超出 59
    }
}
