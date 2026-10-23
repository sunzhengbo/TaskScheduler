using NUnit.Framework;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Commands.Config;
using TaskScheduler.Cli.Commands.Task;
using TaskScheduler.Cli.Commands.Tool;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;
using TaskScheduler.Tests.Fixtures;

namespace TaskScheduler.Tests.Cli;

[TestFixture]
public class CliCommandsRegressionTests
{
    private TestDatabaseFixture _fixture = null!;
    private SettingsService _settingsService = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new TestDatabaseFixture();
        _settingsService = new SettingsService(_fixture.DatabaseProvider);
    }

    [TearDown]
    public void TearDown()
    {
        _fixture.Dispose();
    }

    [Test]
    public async Task ConfigShowCommand_WithInvalidSection_ReturnsCodeTwo()
    {
        var cmd = new ConfigShowCommand(_settingsService);
        var settings = new ConfigShowCommand.Settings
        {
            Section = "bogus_section",
            Json = true
        };

        var result = await cmd.ExecuteAsync(null!, settings, CancellationToken.None);
        Assert.That(result, Is.EqualTo(2));
    }

    [Test]
    public async Task ConfigShowCommand_WithValidEngineSection_ReturnsCodeZero()
    {
        var cmd = new ConfigShowCommand(_settingsService);
        var settings = new ConfigShowCommand.Settings
        {
            Section = "engine",
            Json = true
        };

        var result = await cmd.ExecuteAsync(null!, settings, CancellationToken.None);
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public async Task ConfigShowCommand_WithValidAppearanceSection_ReturnsCodeZero()
    {
        var cmd = new ConfigShowCommand(_settingsService);
        var settings = new ConfigShowCommand.Settings
        {
            Section = "appearance",
            Json = true
        };

        var result = await cmd.ExecuteAsync(null!, settings, CancellationToken.None);
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public async Task ConfigEngineShowCommand_ExecutesWithEngineSection()
    {
        var cmd = new ConfigEngineShowCommand(_settingsService);
        var settings = new ConfigShowCommand.Settings
        {
            Json = true
        };

        var result = await cmd.ExecuteAsync(null!, settings, CancellationToken.None);
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public async Task ConfigAppearanceShowCommand_ExecutesWithAppearanceSection()
    {
        var cmd = new ConfigAppearanceShowCommand(_settingsService);
        var settings = new ConfigShowCommand.Settings
        {
            Json = true
        };

        var result = await cmd.ExecuteAsync(null!, settings, CancellationToken.None);
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void CommandModel_DeserializeFromJson_MalformedJson_ReturnsNull()
    {
        var malformed = "{ \"Name\": \"Broken\", \"Content\": ";
        var result = CommandModel.DeserializeFromJson(malformed);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TaskExportModel_FromJson_MalformedJson_ReturnsNull()
    {
        var malformed = "{ \"name\": \"Broken\", \"command\": {";
        var result = TaskExportModel.FromJson(malformed);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ToolAddCommand_WithDuplicateName_ReturnsCodeTwo()
    {
        var toolService = new ToolConfigService(_fixture.DatabaseProvider);
        await toolService.AddToolAsync(new ToolConfig
        {
            ToolType = "Python",
            VersionName = "python3.12",
            ExecutablePath = "C:\\Python312\\python.exe"
        });

        var cmd = new ToolAddCommand(toolService);
        var settings = new ToolAddCommand.Settings
        {
            ToolType = "Python",
            VersionName = "python3.12",
            ExecutablePath = "C:\\Python312_other\\python.exe",
            Json = true
        };

        var result = await cmd.ExecuteAsync(null!, settings, CancellationToken.None);
        Assert.That(result, Is.EqualTo(2));
    }

    [Test]
    public async Task ToolAddCommand_WithUniqueName_ReturnsCodeZero()
    {
        var toolService = new ToolConfigService(_fixture.DatabaseProvider);
        var cmd = new ToolAddCommand(toolService);
        var settings = new ToolAddCommand.Settings
        {
            ToolType = "Python",
            VersionName = "python3.13",
            ExecutablePath = "C:\\Python313\\python.exe",
            Json = true
        };

        var result = await cmd.ExecuteAsync(null!, settings, CancellationToken.None);
        Assert.That(result, Is.EqualTo(0));

        var tools = await toolService.GetToolsByTypeAsync("Python");
        Assert.That(tools.Any(t => t.VersionName == "python3.13"), Is.True);
    }
}
