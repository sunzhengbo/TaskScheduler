using NUnit.Framework;
using TaskScheduler.Infra.Helpers;

namespace TaskScheduler.Tests.Infra;

[TestFixture]
public class PathHelperTests
{
    [TestCase("test.tar.gz", ".tar.gz")]
    [TestCase("archive.tar.bz2", ".tar.bz2")]
    [TestCase("document.pdf", ".pdf")]
    [TestCase("image.png", ".png")]
    [TestCase("file_no_ext", "")]
    public void GetExtension_ReturnsExpectedExtension(string fileName, string expected)
    {
        var ext = PathHelper.GetExtension(fileName);
        Assert.That(ext, Is.EqualTo(expected));
    }

    [TestCase("document.pdf", "pdf")]
    [TestCase("archive.tar.gz", "tar.gz")]
    [TestCase("file", "")]
    public void GetFileExtensionWithoutDot_ReturnsExtensionWithoutLeadingDot(string fileName, string expected)
    {
        var ext = PathHelper.GetFileExtensionWithoutDot(fileName);
        Assert.That(ext, Is.EqualTo(expected));
    }

    [Test]
    public void GetOsDataDir_Windows_ReturnsPathInsideLocalApplicationData()
    {
        if (OperatingSystem.IsWindows())
        {
            var path = PathHelper.GetOsDataDir("TaskSchedulerApp");
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            Assert.That(path, Does.StartWith(localAppData));
            Assert.That(path, Does.EndWith("TaskSchedulerApp"));
        }
    }
}
