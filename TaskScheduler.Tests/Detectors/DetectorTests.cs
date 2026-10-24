using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using TaskScheduler.Core.Detectors;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Tests.Detectors;

[TestFixture]
public class DetectorTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ts_detector_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // 忽略临时目录清理异常
        }
    }

    [Test]
    public async Task AsdfDetector_EmptyDirectory_ReturnsNull()
    {
        var detector = new AsdfDetector(rootDirOverride: _tempDir);
        var result = await detector.DetectAsync(CommandTypes.Python);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AsdfDetector_MultiplePythonVersions_PicksLatestVersion()
    {
        var py11 = Path.Combine(_tempDir, "installs", "python", "3.11.0", "bin");
        var py12 = Path.Combine(_tempDir, "installs", "python", "3.12.0", "bin");
        Directory.CreateDirectory(py11);
        Directory.CreateDirectory(py12);

        var exe11 = Path.Combine(py11, OperatingSystem.IsWindows() ? "python.exe" : "python");
        var exe12 = Path.Combine(py12, OperatingSystem.IsWindows() ? "python.exe" : "python");
        File.WriteAllText(exe11, "dummy");
        File.WriteAllText(exe12, "dummy");

        var detector = new AsdfDetector(rootDirOverride: _tempDir);
        var result = await detector.DetectAsync(CommandTypes.Python);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(exe12));
    }

    [Test]
    public async Task AsdfDetector_ShimsCandidate_ReturnsShimPath()
    {
        var shims = Path.Combine(_tempDir, "shims");
        Directory.CreateDirectory(shims);
        var shimExe = Path.Combine(shims, OperatingSystem.IsWindows() ? "python.exe" : "python");
        File.WriteAllText(shimExe, "dummy");

        var detector = new AsdfDetector(rootDirOverride: _tempDir);
        var result = await detector.DetectAsync(CommandTypes.Python);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(shimExe));
    }

    [Test]
    public async Task UvDetector_MultipleVersions_PicksLatestSemanticVersion()
    {
        var v1 = Path.Combine(_tempDir, "cpython-3.11.0-windows");
        var v2 = Path.Combine(_tempDir, "cpython-3.12.5-windows");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);

        var exe1 = Path.Combine(v1, OperatingSystem.IsWindows() ? "python.exe" : "python");
        var exe2 = Path.Combine(v2, OperatingSystem.IsWindows() ? "python.exe" : "python");
        File.WriteAllText(exe1, "dummy");
        File.WriteAllText(exe2, "dummy");

        var detector = new UvDetector(rootDirOverride: _tempDir);
        var result = await detector.DetectAsync(CommandTypes.Python);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(exe2));
    }

    [Test]
    public async Task UvDetector_SemanticVersionOrdering_Picks310Over39()
    {
        // 关键断言：验证语义版本排序避免了字典序错误（"3.9" 误排在 "3.10" 前）
        var v39 = Path.Combine(_tempDir, "cpython-3.9.0-windows-x86_64-none");
        var v310 = Path.Combine(_tempDir, "cpython-3.10.0-windows-x86_64-none");
        Directory.CreateDirectory(v39);
        Directory.CreateDirectory(v310);

        var exe39 = Path.Combine(v39, OperatingSystem.IsWindows() ? "python.exe" : "python");
        var exe310 = Path.Combine(v310, OperatingSystem.IsWindows() ? "python.exe" : "python");
        File.WriteAllText(exe39, "dummy");
        File.WriteAllText(exe310, "dummy");

        var detector = new UvDetector(rootDirOverride: _tempDir);
        var result = await detector.DetectAsync(CommandTypes.Python);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(exe310));
    }

    [Test]
    public void VersionOrderHelper_PrefixNumber_ExtractsDotVersionProperly()
    {
        var items = new[]
        {
            "win64-python-3.9.0",
            "win64-python-3.12.0",
            "python3-3.10.0"
        };

        var ordered = items.OrderByDescendingVersion().ToList();

        Assert.That(ordered[0], Is.EqualTo("win64-python-3.12.0"));
        Assert.That(ordered[1], Is.EqualTo("python3-3.10.0"));
        Assert.That(ordered[2], Is.EqualTo("win64-python-3.9.0"));
    }

    [Test]
    public async Task UvDetector_NonPythonType_ReturnsNull()
    {
        var detector = new UvDetector(rootDirOverride: _tempDir);
        var result = await detector.DetectAsync(CommandTypes.PowerShell);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task VoltaDetector_WithNodeInBin_ReturnsPath()
    {
        var binDir = Path.Combine(_tempDir, "bin");
        Directory.CreateDirectory(binDir);
        var nodeExe = Path.Combine(binDir, OperatingSystem.IsWindows() ? "node.exe" : "node");
        File.WriteAllText(nodeExe, "dummy");

        var detector = new VoltaDetector(rootDirOverride: _tempDir);
        var result = await detector.DetectAsync(CommandTypes.NodeJs);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(nodeExe));
    }

    [Test]
    public async Task ScoopDetector_WithAppCurrent_ReturnsPath()
    {
        var appCurrent = Path.Combine(_tempDir, "apps", "pwsh", "current");
        Directory.CreateDirectory(appCurrent);
        var pwshExe = Path.Combine(appCurrent, OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        File.WriteAllText(pwshExe, "dummy");

        var detector = new ScoopDetector(rootDirOverride: _tempDir);
        var result = await detector.DetectAsync(CommandTypes.PowerShell);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(pwshExe));
    }

    [Test]
    public async Task ScoopDetector_NodeJsLts_ReturnsPath()
    {
        var appCurrent = Path.Combine(_tempDir, "apps", "nodejs-lts", "current");
        Directory.CreateDirectory(appCurrent);
        var nodeExe = Path.Combine(appCurrent, OperatingSystem.IsWindows() ? "node.exe" : "node");
        File.WriteAllText(nodeExe, "dummy");

        var detector = new ScoopDetector(rootDirOverride: _tempDir);
        var result = await detector.DetectAsync(CommandTypes.NodeJs);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(nodeExe));
    }

    [Test]
    public async Task FnmDetector_MultishellsSubdirectories_FindsNode()
    {
        var sessionDir = Path.Combine(_tempDir, "4248_17000000");
        Directory.CreateDirectory(sessionDir);
        var nodeExe = Path.Combine(sessionDir, OperatingSystem.IsWindows() ? "node.exe" : "node");
        File.WriteAllText(nodeExe, "dummy");

        var detector = new FnmDetector(rootDirOverride: _tempDir);
        var result = await detector.DetectAsync(CommandTypes.NodeJs);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(nodeExe));
    }

    [Test]
    public async Task MiseDetector_WithInstallsVersion_ReturnsPath()
    {
        var binDir = Path.Combine(_tempDir, "installs", "powershell", "7.4.0", "bin");
        Directory.CreateDirectory(binDir);
        var pwshExe = Path.Combine(binDir, OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        File.WriteAllText(pwshExe, "dummy");

        var detector = new MiseDetector(rootDirOverride: _tempDir);
        var result = await detector.DetectAsync(CommandTypes.PowerShell);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(pwshExe));
    }
}
