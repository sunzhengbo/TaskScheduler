using NUnit.Framework;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;
using TaskScheduler.Tests.Fixtures;

namespace TaskScheduler.Tests.Services;

[TestFixture]
public class SettingsAndToolConfigServiceTests
{
    private TestDatabaseFixture _fixture = null!;
    private SettingsService _settingsService = null!;
    private ToolConfigService _toolService = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new TestDatabaseFixture();
        _settingsService = new SettingsService(_fixture.DatabaseProvider);
        _toolService = new ToolConfigService(_fixture.DatabaseProvider);
    }

    [TearDown]
    public void TearDown()
    {
        _fixture.Dispose();
    }

    [Test]
    public async Task SettingsService_CRUDOperations_WorkCorrectly()
    {
        await _settingsService.SetValueAsync("CustomKey", "CustomValue");

        var retrieved = await _settingsService.GetValueAsync("CustomKey");
        Assert.That(retrieved, Is.EqualTo("CustomValue"));

        var all = await _settingsService.GetAllAsync();
        Assert.That(all.ContainsKey("CustomKey"), Is.True);
        Assert.That(all["CustomKey"], Is.EqualTo("CustomValue"));

        await _settingsService.DeleteAsync("CustomKey");
        var afterDelete = await _settingsService.GetValueAsync("CustomKey");
        Assert.That(afterDelete, Is.Null);
    }

    [Test]
    public async Task SettingsService_EngineSettings_RoundtripSerializationAndPersistence()
    {
        var settings = new EngineSettings
        {
            LogRetentionDays = 60,
            MaxThreads = 16,
            StartupEnabled = true,
            StartupMinimize = true,
            RescheduleOnStartup = false
        };

        var json = settings.ToJson();
        await _settingsService.SetValueAsync("Engine", json);

        var loadedJson = await _settingsService.GetValueAsync("Engine");
        var loadedSettings = EngineSettings.FromJson(loadedJson);

        Assert.That(loadedSettings.LogRetentionDays, Is.EqualTo(60));
        Assert.That(loadedSettings.MaxThreads, Is.EqualTo(16));
        Assert.That(loadedSettings.StartupEnabled, Is.True);
        Assert.That(loadedSettings.StartupMinimize, Is.True);
        Assert.That(loadedSettings.RescheduleOnStartup, Is.False);
    }

    [Test]
    public async Task ToolConfigService_AddTool_SetDefault_And_DeleteTool_WorksCorrectly()
    {
        var tool1 = new ToolConfig
        {
            ToolType = "Python",
            VersionName = "Python 3.10",
            ExecutablePath = "C:\\Python310\\python.exe",
            IsDefault = true
        };

        var tool2 = new ToolConfig
        {
            ToolType = "Python",
            VersionName = "Python 3.12",
            ExecutablePath = "C:\\Python312\\python.exe",
            IsDefault = false
        };

        var created1 = await _toolService.AddToolAsync(tool1);
        var created2 = await _toolService.AddToolAsync(tool2);

        var pythonTools = await _toolService.GetToolsByTypeAsync("Python");
        Assert.That(pythonTools.Count, Is.EqualTo(2));

        // 切换默认版本为 Python 3.12
        await _toolService.SetDefaultVersionAsync(created2.Id);

        var updatedTool1 = await _toolService.GetToolByIdAsync(created1.Id);
        var updatedTool2 = await _toolService.GetToolByIdAsync(created2.Id);

        Assert.That(updatedTool1!.IsDefault, Is.False);
        Assert.That(updatedTool2!.IsDefault, Is.True);

        // 删除 tool1
        await _toolService.DeleteToolAsync(created1.Id);
        var remainingTools = await _toolService.GetToolsByTypeAsync("Python");
        Assert.That(remainingTools.Count, Is.EqualTo(1));
        Assert.That(remainingTools[0].Id, Is.EqualTo(created2.Id));
    }
}
