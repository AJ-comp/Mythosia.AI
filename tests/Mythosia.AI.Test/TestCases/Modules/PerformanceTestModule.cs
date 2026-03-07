using System.Diagnostics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mythosia.AI.Tests.Modules;

[TestClass]
public abstract class PerformanceTestModule : TestModuleBase
{
    [TestCategory("Performance")]
    [TestMethod]
    public async Task StreamingMemoryEfficiencyTest()
    {
        var before = GC.GetTotalMemory(true);
        var (content, chunkCount) = await StreamAndCollectAsync("Write a short paragraph about AI.");
        var after = GC.GetTotalMemory(false);
        var delta = after - before;

        Console.WriteLine($"[Memory] Before: {before:N0}, After: {after:N0}, Delta: {delta:N0}");
        Console.WriteLine($"[Memory] Chunks: {chunkCount}, Content length: {content.Length}");
        Assert.IsTrue(content.Length > 0);
    }

    [TestCategory("Performance")]
    [TestMethod]
    public async Task ResponseTimeBenchmarkTest()
    {
        var sw = Stopwatch.StartNew();
        var response = await AI.GetCompletionAsync("What is 1+1? Answer with just the number.");
        sw.Stop();

        Console.WriteLine($"[Benchmark] Response time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"[Benchmark] Response: {response}");
        Assert.IsNotNull(response);
        Assert.IsTrue(sw.ElapsedMilliseconds < 30000, "Response should be under 30s");
    }
}
