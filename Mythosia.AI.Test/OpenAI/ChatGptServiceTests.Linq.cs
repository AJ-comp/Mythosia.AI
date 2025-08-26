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
        /// IAsyncEnumerable with System.Linq.Async 시뮬레이션 테스트
        /// </summary>
        [TestMethod]
        public async Task IAsyncEnumerableLinqSimulationTest()
        {
            try
            {
                // Take 시뮬레이션
                var firstFiveChunks = new List<string>();
                await foreach (var chunk in AI.StreamAsync("Count from 1 to 20 slowly"))
                {
                    firstFiveChunks.Add(chunk);
                    if (firstFiveChunks.Count >= 5)
                        break;
                }

                Console.WriteLine($"[Take(5)] Got {firstFiveChunks.Count} chunks");
                Assert.AreEqual(5, firstFiveChunks.Count);

                // Where 시뮬레이션 (긴 청크만 선택)
                var longChunks = new List<string>();
                await foreach (var chunk in AI.StreamAsync("Explain AI ethics with examples"))
                {
                    if (chunk.Length > 10)
                    {
                        longChunks.Add(chunk);
                    }
                }

                Console.WriteLine($"[Where(length > 10)] {longChunks.Count} long chunks out of total");
                if (longChunks.Count > 0)
                {
                    var avgLength = longChunks.Average(c => c.Length);
                    Console.WriteLine($"[Average Length] {avgLength:F1} chars");
                    Assert.IsTrue(avgLength > 10);
                }

                // Aggregate 시뮬레이션 (단어 수 계산)
                int totalWords = 0;
                await foreach (var chunk in AI.StreamAsync("Write a short paragraph about technology"))
                {
                    totalWords += chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                }

                Console.WriteLine($"[Word Count] Total: {totalWords} words");
                Assert.IsTrue(totalWords > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LINQ Simulation Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// System.Linq.Async를 사용한 고급 스트리밍 테스트
        /// </summary>
        [TestMethod]
        public async Task AdvancedLinqAsyncTest()
        {
            try
            {
                // 1. Take와 ToListAsync
                var firstThreeChunks = await AI
                    .StreamAsync("List 10 benefits of AI")
                    .Take(3)
                    .ToListAsync();

                Console.WriteLine($"[Take(3)] Got {firstThreeChunks.Count} chunks");
                Assert.AreEqual(3, firstThreeChunks.Count);

                // 2. Select와 Where 조합
                var processedChunks = await AI
                    .StreamAsync("Explain cloud computing")
                    .Where(chunk => !string.IsNullOrWhiteSpace(chunk))
                    .Select(chunk => new
                    {
                        Content = chunk,
                        Length = chunk.Length,
                        WordCount = chunk.Split(' ').Length
                    })
                    .ToListAsync();

                Console.WriteLine($"[Processed] {processedChunks.Count} chunks");
                var totalWords = processedChunks.Sum(c => c.WordCount);
                Console.WriteLine($"[Total Words] {totalWords}");

                // 3. Aggregate를 사용한 통계
                var stats = await AI
                    .StreamAsync("Describe machine learning algorithms")
                    .AggregateAsync(
                        new { Count = 0, TotalLength = 0 },
                        (acc, chunk) => new
                        {
                            Count = acc.Count + 1,
                            TotalLength = acc.TotalLength + chunk.Length
                        });

                Console.WriteLine($"[Stats] Chunks: {stats.Count}, Total Length: {stats.TotalLength}");
                Console.WriteLine($"[Stats] Average Chunk Size: {(double)stats.TotalLength / stats.Count:F1}");

                // 4. FirstOrDefaultAsync로 첫 번째 의미 있는 청크 찾기
                var firstMeaningfulChunk = await AI
                    .StreamAsync("Say hello and then explain quantum physics")
                    .Where(chunk => chunk.Length > 20)
                    .FirstOrDefaultAsync();

                if (firstMeaningfulChunk != null)
                {
                    Console.WriteLine($"[First Meaningful] {firstMeaningfulChunk.Substring(0, Math.Min(50, firstMeaningfulChunk.Length))}...");
                }
                else
                {
                    Console.WriteLine("[First Meaningful] No chunk found with length > 20");
                }

                // 5. CountAsync로 청크 수 계산
                var chunkCount = await AI
                    .StreamAsync("Count to 5")
                    .CountAsync();

                Console.WriteLine($"[Count] Total chunks: {chunkCount}");

                // 6. 복잡한 파이프라인
                var complexResult = await AI
                    .StreamAsync("List and explain 5 programming paradigms")
                    .Where(chunk => chunk.Length > 5)
                    .Select((chunk, index) => new { Index = index, Chunk = chunk })
                    .Take(20)
                    .GroupBy(x => x.Index / 5) // 5개씩 그룹
                    .Select(async group =>
                    {
                        var chunks = await group.ToListAsync();
                        return new
                        {
                            GroupId = group.Key,
                            ChunkCount = chunks.Count,
                            TotalLength = chunks.Sum(x => x.Chunk.Length)
                        };
                    })
                    .SelectAwait(async x => await x)
                    .ToListAsync();

                Console.WriteLine($"[Complex Pipeline] {complexResult.Count} groups");
                foreach (var group in complexResult)
                {
                    Console.WriteLine($"  Group {group.GroupId}: {group.ChunkCount} chunks, {group.TotalLength} chars");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Advanced LINQ Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}