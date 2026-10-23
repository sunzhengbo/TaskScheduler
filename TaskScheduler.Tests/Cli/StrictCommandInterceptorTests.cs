using NUnit.Framework;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Infrastructure;

namespace TaskScheduler.Tests.Cli;

[TestFixture]
public class StrictCommandInterceptorTests
{
    public sealed class TestSettings : CommandSettings
    {
        [CommandOption("-n|--name <NAME>")]
        [System.ComponentModel.Description("测试名称")]
        public string? Name { get; set; }

        [CommandOption("-f|--force")]
        public bool Force { get; set; }
    }

    public sealed class TestCommand : Command<TestSettings>
    {
        public override int Execute(CommandContext context, TestSettings settings, CancellationToken cancellationToken)
        {
            return 0;
        }
    }

    private ICommandApp CreateApp()
    {
        var app = new CommandApp<TestCommand>();
        app.Configure(config =>
        {
            config.SetInterceptor(new StrictCommandInterceptor());
            config.SetExceptionHandler((ex, _) =>
            {
                if (ex is ArgumentException or CommandParseException)
                {
                    return 2;
                }
                return 1;
            });
        });
        return app;
    }

    [Test]
    public void StrictCommandInterceptor_ValidOptions_ReturnsSuccessCodeZero()
    {
        var app = CreateApp();
        var exitCode = app.Run(new[] { "--name", "test-job", "--force" });

        Assert.That(exitCode, Is.EqualTo(0));
    }

    [TestCase("--unknown-flag")]
    [TestCase("--cronn")]
    [TestCase("-z")]
    public void StrictCommandInterceptor_UnrecognizedOption_InterceptsAndReturnsCodeTwo(string unrecognizedOption)
    {
        var app = CreateApp();
        var exitCode = app.Run(new[] { "--name", "test-job", unrecognizedOption });

        Assert.That(exitCode, Is.EqualTo(2));
    }

    [Test]
    public void StrictCommandInterceptor_ExtraPositionalArguments_InterceptsAndReturnsCodeTwo()
    {
        var app = CreateApp();
        var exitCode = app.Run(new[] { "--name", "test-job", "extra-arg1", "extra-arg2" });

        Assert.That(exitCode, Is.EqualTo(2));
    }
}
