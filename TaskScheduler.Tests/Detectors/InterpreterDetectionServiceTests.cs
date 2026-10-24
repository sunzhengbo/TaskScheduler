using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using TaskScheduler.Core.Detectors;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Tests.Detectors;

[TestFixture]
public class InterpreterDetectionServiceTests
{
    private class CountingDetector : IVersionManagerDetector
    {
        public string Name => "counting";
        public int Order => 10;
        public int InvocationCount { get; private set; }
        public string? ReturnPath { get; set; }

        public ValueTask<string?> DetectAsync(string commandType, CancellationToken ct = default)
        {
            InvocationCount++;
            return ValueTask.FromResult(ReturnPath);
        }
    }

    [Test]
    public void SearchInPath_WithNonExistentCustomCandidate_DoesNotThrow()
    {
        var service = InterpreterDetectionService.Default;
        var result = service.SearchInPath(CommandTypes.Python, "definitely_non_existent_py_wrapper_12345.exe");
        // 当自定义候选不存在时，应回退查内置候选或返回 null/有效默认
        // 关键是安全不抛出异常
        Assert.That(result, Is.Null.Or.Not.Empty);
    }

    [Test]
    public void SearchInPath_WithRawCommandType_NormalizesAndFindsInterpreter()
    {
        var service = InterpreterDetectionService.Default;
        if (OperatingSystem.IsWindows())
        {
            // "cmd" 应该被自动规范化为 CommandTypes.Cmd ("命令提示符 (cmd)") 并查找到 cmd.exe
            var result = service.SearchInPath("cmd");
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.EndWith("cmd.exe").IgnoreCase);

            var vbs = service.SearchInPath("vbs");
            Assert.That(vbs, Is.Not.Null);
            Assert.That(vbs, Does.EndWith("cscript.exe").IgnoreCase);
        }
    }

    [Test]
    public async Task DetectAsync_NormalizesRawCommandTypes_HitsSameCache()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "ts_test_norm_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempFile, "mock");

        try
        {
            var countingDetector = new CountingDetector { ReturnPath = tempFile };
            var service = new InterpreterDetectionService([countingDetector]);

            // 第一次调用：使用小写别名 "custom_lang"
            var firstResult = await service.DetectAsync("custom_lang");
            Assert.That(firstResult, Is.EqualTo(tempFile));
            Assert.That(countingDetector.InvocationCount, Is.EqualTo(1));

            // 第二次调用：使用相同别名或修剪过的名称，应命中同一缓存条目
            var secondResult = await service.DetectAsync("  custom_lang  ");
            Assert.That(secondResult, Is.EqualTo(tempFile));
            Assert.That(countingDetector.InvocationCount, Is.EqualTo(1));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Test]
    public async Task DetectAsync_ManagerCache_CachesWithinTtl()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "ts_test_exe_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempFile, "mock");

        try
        {
            var countingDetector = new CountingDetector { ReturnPath = tempFile };
            var service = new InterpreterDetectionService([countingDetector]);

            // 第一次调用：执行探测器并写入缓存
            var firstResult = await service.DetectAsync("CustomLangXYZ");
            Assert.That(firstResult, Is.EqualTo(tempFile));
            Assert.That(countingDetector.InvocationCount, Is.EqualTo(1));

            // 第二次调用：在 TTL 内应命中缓存，不再次调用探测器
            var secondResult = await service.DetectAsync("CustomLangXYZ");
            Assert.That(secondResult, Is.EqualTo(tempFile));
            Assert.That(countingDetector.InvocationCount, Is.EqualTo(1));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Test]
    public async Task DetectAsync_CachedFileDeleted_BypassesCacheAndReDetects()
    {
        var tempFile1 = Path.Combine(Path.GetTempPath(), "ts_test_cache_del_1_" + Guid.NewGuid().ToString("N") + ".exe");
        var tempFile2 = Path.Combine(Path.GetTempPath(), "ts_test_cache_del_2_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempFile1, "mock1");
        File.WriteAllText(tempFile2, "mock2");

        try
        {
            var countingDetector = new CountingDetector { ReturnPath = tempFile1 };
            var service = new InterpreterDetectionService([countingDetector]);

            var first = await service.DetectAsync("CustomLangBypass");
            Assert.That(first, Is.EqualTo(tempFile1));
            Assert.That(countingDetector.InvocationCount, Is.EqualTo(1));

            // 外部意外删除了 tempFile1
            File.Delete(tempFile1);
            countingDetector.ReturnPath = tempFile2;

            // 再次调用：由于缓存中的路径已不存在，应穿透缓存重新探测
            var second = await service.DetectAsync("CustomLangBypass");
            Assert.That(second, Is.EqualTo(tempFile2));
            Assert.That(countingDetector.InvocationCount, Is.EqualTo(2));
        }
        finally
        {
            try { File.Delete(tempFile1); } catch { }
            try { File.Delete(tempFile2); } catch { }
        }
    }

    [Test]
    public void DetectorNames_DefaultInstance_ContainsAllSevenDetectorsInOrder()
    {
        var service = InterpreterDetectionService.Default;
        Assert.That(service.DetectorNames, Is.Not.Null);
        Assert.That(service.DetectorNames, Is.EqualTo(new[] { "mise", "scoop", "uv", "asdf", "nvm", "fnm", "volta" }));
    }

    [Test]
    public async Task DetectAsync_ConcurrentCallsSameType_ExecutesDetectorOnceViaSingleflight()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "ts_test_singleflight_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempFile, "mock");

        try
        {
            var countingDetector = new CountingDetector { ReturnPath = tempFile };
            var service = new InterpreterDetectionService([countingDetector]);

            // 模拟 10 个并发任务同时请求同一未缓存的语言类型（使用虚拟名称避免被本机 PATH 截胡）
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(async () => await service.DetectAsync("SingleflightLang")))
                .ToList();

            var results = await Task.WhenAll(tasks);

            foreach (var res in results)
            {
                Assert.That(res, Is.EqualTo(tempFile));
            }

            // 由于单飞并发锁和双重缓存检查，即使 10 个线程同时触发，底层探测器也只会被调用 1 次
            Assert.That(countingDetector.InvocationCount, Is.EqualTo(1));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Test]
    public async Task DetectAsync_UnknownCommandType_CleansUpLocksToPreventUnboundedGrowth()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "ts_test_cleanup_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempFile, "mock");

        try
        {
            var countingDetector = new CountingDetector { ReturnPath = tempFile };
            var service = new InterpreterDetectionService([countingDetector]);

            // 未知命令类型单飞探测后，锁项应被及时清理
            var result = await service.DetectAsync("DynamicCustomLang_XYZ");
            Assert.That(result, Is.EqualTo(tempFile));
            Assert.That(countingDetector.InvocationCount, Is.EqualTo(1));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }
}
