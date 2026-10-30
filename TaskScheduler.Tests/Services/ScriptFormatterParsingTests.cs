using NUnit.Framework;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Tests.Services;

/// <summary>
/// 脚本格式化中「纯逻辑」部分的回归测试：版本解析、行尾保持、语法兼容与超时区间校验。
/// 这些分支不依赖外部进程，是历史上最容易出错（且无编译期保护）的部分。
/// </summary>
[TestFixture]
public class ScriptFormatterParsingTests
{
    [TestCase("black, 24.4.2 (compiled: yes)", ExpectedResult = "24.4.2")]
    [TestCase("black 24.4.2", ExpectedResult = "24.4.2")]
    [TestCase("black, 24.4.2 (compiled: yes)\nPython (CPython) 3.12.3", ExpectedResult = "24.4.2")]
    [TestCase("black, 25.1", ExpectedResult = "25.1")]
    [TestCase("command not found", ExpectedResult = null)]
    [TestCase("", ExpectedResult = null)]
    public string? ParseBlackVersion_ShouldExtractVersionFromRealOutput(string output) =>
        ScriptFormatterService.ParseBlackVersion(output);

    [TestCase("3.9.6", ExpectedResult = true)]
    [TestCase("3.9.5", ExpectedResult = false)]
    [TestCase("3.10.0", ExpectedResult = true)]
    [TestCase("v3.9.6", ExpectedResult = true)]
    [TestCase("4.0.0-beta", ExpectedResult = false)]
    [TestCase(null, ExpectedResult = false)]
    [TestCase("unknown", ExpectedResult = false)]
    public bool IsSupportedPrettierVersion_ShouldCompareAgainstMinimum(string? version) =>
        ScriptFormatterService.IsSupportedPrettierVersion(version);

    [TestCase(5, 1, 0, ExpectedResult = true)]
    [TestCase(5, 0, 0, ExpectedResult = false)]
    [TestCase(7, 2, 11, ExpectedResult = true)]
    [TestCase(7, 2, 10, ExpectedResult = false)]
    [TestCase(7, 4, 0, ExpectedResult = true)]
    [TestCase(6, 2, 0, ExpectedResult = false)]
    public bool IsSupportedPowerShell_ShouldFollowDocumentedMinimum(int major, int minor, int build) =>
        ScriptFormatterService.IsSupportedPowerShell(new Version(major, minor, build));

    [Test]
    public void PreserveLineEndings_ShouldKeepCrlfWhenSourceUsesCrlf()
    {
        const string source = "a\r\nb\r\nc";
        const string formatted = "a\nb\nc";

        Assert.That(ScriptFormatterService.PreserveLineEndings(source, formatted), Is.EqualTo("a\r\nb\r\nc"));
    }

    [Test]
    public void PreserveLineEndings_ShouldUseLfWhenSourceUsesLf()
    {
        const string source = "a\nb\nc";
        const string formatted = "a\r\nb\r\nc";

        Assert.That(ScriptFormatterService.PreserveLineEndings(source, formatted), Is.EqualTo("a\nb\nc"));
    }

    [Test]
    public void FormatCSharp_ShouldKeepFileBasedAppDirectives()
    {
        const string source = "#:package Humanizer@2.14.1\nusing System;\nConsole.WriteLine(  \"hi\"  );";

        var formatted = ScriptFormatterService.FormatCSharp(source, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("#:package Humanizer@2.14.1"));
            Assert.That(formatted, Does.Not.Contain("__TS_DIRECTIVE_"));
        });
    }

    [Test]
    public void FormatCSharp_ShouldReportSyntaxErrorWithLineNumber()
    {
        const string source = "#:package Humanizer@2.14.1\nusing System;\nclass {";

        var ex = Assert.Throws<InvalidDataException>(
            () => ScriptFormatterService.FormatCSharp(source, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("第 3 行"));
    }

    [TestCase(0, ExpectedResult = false)]
    [TestCase(1, ExpectedResult = true)]
    [TestCase(120, ExpectedResult = true)]
    [TestCase(121, ExpectedResult = false)]
    public bool IsValidTimeout_ShouldEnforceSharedRange(int seconds) =>
        ScriptFormatterSettings.IsValidTimeout(seconds);

    [Test]
    public void FromJson_ShouldResetOutOfRangeTimeout()
    {
        var settings = ScriptFormatterSettings.FromJson("{\"timeoutSeconds\":9999}");

        Assert.That(settings.TimeoutSeconds, Is.EqualTo(ScriptFormatterDefaults.DefaultTimeoutSeconds));
    }

    /// <summary>
    /// CLI 的 --timeout-seconds 描述是编译期常量（Attribute 无法拼接 Min/Max），
    /// 一旦有人调整超时区间却忘了改文案，这条测试会直接失败。
    /// </summary>
    [Test]
    public void TimeoutOptionDescription_ShouldMatchSharedRange()
    {
        var description = ScriptFormatterDefaults.TimeoutOptionDescription;
        var expectedRange = $"{ScriptFormatterDefaults.MinTimeoutSeconds} 到 {ScriptFormatterDefaults.MaxTimeoutSeconds}";

        Assert.That(description, Does.Contain(expectedRange));
    }

    /// <summary>
    /// 空白路径必须在读取时就折叠为 null：否则 <c>config formatter show --json</c> 会输出 ""，
    /// 与「未配置（自动发现）」形成两种表示。
    /// </summary>
    [Test]
    public void FromJson_ShouldCollapseBlankPathsToNull()
    {
        var settings = ScriptFormatterSettings.FromJson(
            "{\"blackExecutablePath\":\"  \",\"shfmtExecutablePath\":\" C:\\\\tools\\\\shfmt.exe \"}");

        Assert.Multiple(() =>
        {
            Assert.That(settings.BlackExecutablePath, Is.Null);
            Assert.That(settings.ShfmtExecutablePath, Is.EqualTo(@"C:\tools\shfmt.exe"));
        });
    }

    [Test]
    public void ToJson_AfterNormalizePaths_ShouldEmitNullInsteadOfEmptyString()
    {
        var settings = new ScriptFormatterSettings { PrettierPackagePath = "   " };

        settings.NormalizePaths();

        Assert.That(settings.ToJson(), Does.Contain("\"prettierPackagePath\":null"));
    }
}
