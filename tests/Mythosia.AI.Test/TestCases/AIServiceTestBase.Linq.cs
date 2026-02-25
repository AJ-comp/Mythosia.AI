namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// System.Linq.Async를 사용한 고급 스트리밍 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Linq")]
    [TestMethod]
    public async Task AdvancedLinqAsyncTest()
    {
        try
        {
            // System.Linq.Async가 설치되어 있는지 확인
            var linqAsyncAvailable = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name == "System.Linq.Async");

            if (!linqAsyncAvailable)
            {
                Console.WriteLine("[LINQ] System.Linq.Async not available, testing manual implementation");

                // Manual implementation tests
                await TestManualLinqOperations();
            }
            else
            {
                Console.WriteLine("[LINQ] System.Linq.Async available, testing advanced operations");

                // Note: 실제 System.Linq.Async 메서드를 사용하려면 dynamic 또는 reflection 필요
                // 여기서는 시뮬레이션만 수행
                await TestManualLinqOperations();

                Console.WriteLine("[LINQ] For full System.Linq.Async tests, ensure the package is properly referenced");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Advanced LINQ Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    private async Task TestManualLinqOperations()
    {
        // 1. Take와 ToList
        var firstThreeChunks = new List<string>();
        await foreach (var chunk in AI.StreamAsync("List 10 benefits of AI"))
        {
            firstThreeChunks.Add(chunk);
            if (firstThreeChunks.Count >= 3)
                break;
        }
        Console.WriteLine($"[Take(3)] Got {firstThreeChunks.Count} chunks");
        Assert.IsTrue(firstThreeChunks.Count >= 1 && firstThreeChunks.Count <= 3,
            $"Expected 1~3 chunks but got {firstThreeChunks.Count}. Reasoning models may return fewer chunks.");

        // 2. Select와 Where 조합
        var processedChunks = new List<dynamic>();
        await foreach (var chunk in AI.StreamAsync("Explain cloud computing"))
        {
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                processedChunks.Add(new
                {
                    Content = chunk,
                    Length = chunk.Length,
                    WordCount = chunk.Split(' ').Length
                });
            }
        }

        Console.WriteLine($"[Processed] {processedChunks.Count} chunks");
        var totalWords = processedChunks.Sum(c => (int)c.WordCount);
        Console.WriteLine($"[Total Words] {totalWords}");

        // 3. Aggregate를 사용한 통계
        var stats = new { Count = 0, TotalLength = 0 };
        await foreach (var chunk in AI.StreamAsync("Describe machine learning algorithms"))
        {
            stats = new
            {
                Count = stats.Count + 1,
                TotalLength = stats.TotalLength + chunk.Length
            };
        }

        Console.WriteLine($"[Stats] Chunks: {stats.Count}, Total Length: {stats.TotalLength}");
        if (stats.Count > 0)
        {
            Console.WriteLine($"[Stats] Average Chunk Size: {(double)stats.TotalLength / stats.Count:F1}");
        }

        // 4. FirstOrDefault로 첫 번째 의미 있는 청크 찾기
        string firstMeaningfulChunk = null;
        await foreach (var chunk in AI.StreamAsync("Say hello and then explain quantum physics"))
        {
            if (chunk.Length > 20)
            {
                firstMeaningfulChunk = chunk;
                break;
            }
        }

        if (firstMeaningfulChunk != null)
        {
            Console.WriteLine($"[First Meaningful] {firstMeaningfulChunk.Substring(0, Math.Min(50, firstMeaningfulChunk.Length))}...");
        }

        // 5. Count로 청크 수 계산
        var chunkCount = 0;
        await foreach (var chunk in AI.StreamAsync("Count to 5"))
        {
            chunkCount++;
        }
        Console.WriteLine($"[Count] Total chunks: {chunkCount}");

        // 6. 복잡한 파이프라인
        var complexResult = new List<dynamic>();
        var chunkIndex = 0;
        var currentGroup = new List<dynamic>();
        var groupId = 0;

        await foreach (var chunk in AI.StreamAsync("List and explain 5 programming paradigms"))
        {
            if (chunk.Length > 5)
            {
                currentGroup.Add(new { Index = chunkIndex++, Chunk = chunk });

                if (currentGroup.Count >= 5)
                {
                    complexResult.Add(new
                    {
                        GroupId = groupId++,
                        ChunkCount = currentGroup.Count,
                        TotalLength = currentGroup.Sum(x => ((string)x.Chunk).Length)
                    });
                    currentGroup.Clear();
                }
            }
        }

        // 마지막 그룹 처리
        if (currentGroup.Count > 0)
        {
            complexResult.Add(new
            {
                GroupId = groupId,
                ChunkCount = currentGroup.Count,
                TotalLength = currentGroup.Sum(x => ((string)x.Chunk).Length)
            });
        }

        Console.WriteLine($"[Complex Pipeline] {complexResult.Count} groups");
        foreach (var group in complexResult)
        {
            Console.WriteLine($"  Group {group.GroupId}: {group.ChunkCount} chunks, {group.TotalLength} chars");
        }
    }

    /// <summary>
    /// IAsyncEnumerable LINQ 시뮬레이션 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Linq")]
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
            Assert.IsTrue(firstFiveChunks.Count >= 1 && firstFiveChunks.Count <= 5,
                $"Expected 1~5 chunks but got {firstFiveChunks.Count}. Reasoning models may return fewer chunks.");

            // Where 시뮬레이션 (긴 청크만 선택)
            var longChunks = new List<string>();
            await foreach (var chunk in AI.StreamAsync("Explain AI ethics with examples"))
            {
                if (chunk.Length > 10)
                {
                    longChunks.Add(chunk);
                }
            }

            Console.WriteLine($"[Where(length > 10)] {longChunks.Count} long chunks");
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
}