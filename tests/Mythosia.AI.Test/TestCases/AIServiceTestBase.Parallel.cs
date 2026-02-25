namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// IAsyncEnumerable 병렬 스트리밍 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Parallel")]
    [TestMethod]
    public async Task IAsyncEnumerableParallelStreamingTest()
    {
        try
        {
            // 두 개의 다른 프롬프트를 동시에 스트리밍
            var task1 = StreamAndCollectAsync("Explain quantum computing in simple terms");
            var task2 = StreamAndCollectAsync("Explain machine learning in simple terms");

            var results = await Task.WhenAll(task1, task2);

            Console.WriteLine($"[Parallel Stream 1] Length: {results[0].Content.Length}, Chunks: {results[0].ChunkCount}");
            Console.WriteLine($"[Parallel Stream 2] Length: {results[1].Content.Length}, Chunks: {results[1].ChunkCount}");

            Assert.IsTrue(results[0].Content.Length > 0);
            Assert.IsTrue(results[1].Content.Length > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Parallel Streaming Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 다중 스트림 병합 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Parallel")]
    [TestMethod]
    public async Task MultiStreamMergeTest()
    {
        try
        {
            // 서로 다른 프롬프트의 스트림을 병합
            var mergedChunks = new List<string>();

            // 수동 병합
            var task1 = ProcessStreamAsync(AI.StreamOnceAsync("First topic: AI"), mergedChunks, "[1]");
            var task2 = ProcessStreamAsync(AI.StreamOnceAsync("Second topic: ML"), mergedChunks, "[2]");

            await Task.WhenAll(task1, task2);

            Console.WriteLine($"[Merged Streams] Total chunks: {mergedChunks.Count}");

            var stream1Chunks = mergedChunks.Count(c => c.StartsWith("[1]"));
            var stream2Chunks = mergedChunks.Count(c => c.StartsWith("[2]"));

            Console.WriteLine($"  Stream 1: {stream1Chunks} chunks");
            Console.WriteLine($"  Stream 2: {stream2Chunks} chunks");

            Assert.IsTrue(stream1Chunks > 0);
            Assert.IsTrue(stream2Chunks > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Multi Stream Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    private async Task ProcessStreamAsync(IAsyncEnumerable<string> stream, List<string> collector, string prefix)
    {
        await foreach (var chunk in stream)
        {
            lock (collector)
            {
                collector.Add($"{prefix} {chunk}");
            }
        }
    }
}