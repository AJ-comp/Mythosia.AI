using System.Diagnostics;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// 메모리 효율성 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Performance")]
    [TestMethod]
    public async Task StreamingMemoryEfficiencyTest()
    {
        try
        {
            long startMemory = GC.GetTotalMemory(true);

            int processedChunks = 0;
            long totalCharsProcessed = 0;

            await foreach (var chunk in AI.StreamAsync(
                "Generate a detailed technical documentation about distributed systems"))
            {
                processedChunks++;
                totalCharsProcessed += chunk.Length;

                if (processedChunks % 10 == 0)
                {
                    long currentMemory = GC.GetTotalMemory(false);
                    Console.WriteLine($"[Memory] After {processedChunks} chunks: {(currentMemory - startMemory) / 1024}KB");
                }
            }

            long endMemory = GC.GetTotalMemory(true);
            long memoryUsed = (endMemory - startMemory) / 1024;

            Console.WriteLine($"[Memory Efficiency Test]");
            Console.WriteLine($"  Processed Chunks: {processedChunks}");
            Console.WriteLine($"  Total Characters: {totalCharsProcessed:N0}");
            Console.WriteLine($"  Memory Used: {memoryUsed}KB");
            Console.WriteLine($"  Bytes per Character: {(double)(endMemory - startMemory) / totalCharsProcessed:F2}");

            Assert.IsTrue(processedChunks > 0);
            Assert.IsTrue(totalCharsProcessed > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Memory Efficiency Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 응답 시간 벤치마크 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Performance")]
    [TestMethod]
    public async Task ResponseTimeBenchmarkTest()
    {
        try
        {
            var stopwatch = new Stopwatch();

            // 일반 completion 시간 측정
            stopwatch.Start();
            var response = await AI.GetCompletionAsync("Say hello");
            stopwatch.Stop();

            var completionTime = stopwatch.ElapsedMilliseconds;
            Console.WriteLine($"[Completion Time] {completionTime}ms");

            // 스트리밍 첫 번째 청크까지 시간 측정
            stopwatch.Restart();
            long firstChunkTime = 0;
            int chunkCount = 0;

            await foreach (var chunk in AI.StreamAsync("Say hello"))
            {
                if (firstChunkTime == 0)
                {
                    firstChunkTime = stopwatch.ElapsedMilliseconds;
                }
                chunkCount++;
            }
            stopwatch.Stop();

            Console.WriteLine($"[First Chunk Time] {firstChunkTime}ms");
            Console.WriteLine($"[Total Stream Time] {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"[Chunk Count] {chunkCount}");

            Assert.IsTrue(completionTime > 0);
            Assert.IsTrue(firstChunkTime > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Benchmark Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
}