using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    public partial class ChatGptServiceTests
    {
        /// <summary>
        /// IAsyncEnumerable 병렬 스트리밍 테스트
        /// </summary>
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
        [TestMethod]
        public async Task MultiStreamMergeTest()
        {
            try
            {
                // 서로 다른 프롬프트의 스트림을 병합
                var stream1 = AI.StreamOnceAsync("First topic: AI").Select(chunk => $"[1] {chunk}");
                var stream2 = AI.StreamOnceAsync("Second topic: ML").Select(chunk => $"[2] {chunk}");

                var mergedChunks = new List<string>();

                // 수동 병합 (System.Linq.Async에 Merge가 없으므로)
                var task1 = ProcessStreamAsync(stream1, mergedChunks);
                var task2 = ProcessStreamAsync(stream2, mergedChunks);

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

        // Helper methods

        private async Task<(string Content, int ChunkCount)> StreamAndCollectAsync(string prompt)
        {
            var content = "";
            var chunkCount = 0;

            await foreach (var chunk in AI.StreamAsync(prompt))
            {
                content += chunk;
                chunkCount++;
            }

            return (content, chunkCount);
        }

        private async Task ProcessStreamAsync(IAsyncEnumerable<string> stream, List<string> collector)
        {
            await foreach (var chunk in stream)
            {
                lock (collector)
                {
                    collector.Add(chunk);
                }
            }
        }
    }
}