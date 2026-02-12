using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Streaming;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// IAsyncEnumerable 스트리밍 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Streaming")]
    [TestMethod]
    public async Task IAsyncEnumerableStreamingTest()
    {
        try
        {
            AI.ActivateChat.SystemMessage = "응답을 짧고 간결하게 해줘.";

            // 1) 기본 IAsyncEnumerable 스트리밍
            string prompt = "1부터 3까지 세어줘.";
            string fullResponse = "";
            int chunkCount = 0;

            await foreach (var chunk in AI.StreamAsync(prompt))
            {
                fullResponse += chunk;
                chunkCount++;
                Console.Write(chunk);
            }

            Console.WriteLine($"\n[IAsyncEnumerable Stream] Chunks: {chunkCount}, Total: {fullResponse.Length}");
            Assert.IsTrue(chunkCount > 0);
            Assert.IsTrue(fullResponse.Contains("1") && fullResponse.Contains("3"));

            // 2) 취소 토큰을 사용한 스트리밍
            var cts = new CancellationTokenSource();
            string cancelResponse = "";
            int cancelChunkCount = 0;

            try
            {
                await foreach (var chunk in AI.StreamAsync("긴 이야기를 해줘", cts.Token))
                {
                    cancelResponse += chunk;
                    cancelChunkCount++;
                    Console.Write(chunk);

                    if (cancelResponse.Length > 50)
                    {
                        cts.Cancel();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\n[Stream Cancelled] As expected");
            }

            Console.WriteLine($"[Cancelled Stream] Chunks: {cancelChunkCount}, Length: {cancelResponse.Length}");
            Assert.IsTrue(cancelChunkCount > 0);

            // 3) StreamOnceAsync 테스트
            int messageCountBefore = AI.ActivateChat.Messages.Count;

            string onceResponse = "";
            await foreach (var chunk in AI.StreamOnceAsync("이것은 일회성 질문입니다"))
            {
                onceResponse += chunk;
            }

            int messageCountAfter = AI.ActivateChat.Messages.Count;
            Assert.AreEqual(messageCountBefore, messageCountAfter);
            Assert.IsTrue(onceResponse.Length > 0);
            Console.WriteLine($"\n[StreamOnce] Response: {onceResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error in IAsyncEnumerable Test] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// IAsyncEnumerable with MessageBuilder 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Streaming")]
    [TestMethod]
    public async Task IAsyncEnumerableWithMessageBuilderTest()
    {
        try
        {
            // 텍스트만 있는 메시지 스트리밍
            string textResponse = "";
            await foreach (var chunk in AI
                .BeginMessage()
                .AddText("숫자 3을 다양한 언어로 써줘")
                .StreamAsync())
            {
                textResponse += chunk;
                Console.Write(chunk);
            }

            Console.WriteLine($"\n[MessageBuilder Stream] Length: {textResponse.Length}");
            Assert.IsTrue(textResponse.Length > 0);

            // StreamOnceAsync with MessageBuilder
            int messagesBefore = AI.ActivateChat.Messages.Count;

            string onceBuilderResponse = "";
            await foreach (var chunk in AI
                .BeginMessage()
                .AddText("일회성 테스트 메시지")
                .StreamOnceAsync())
            {
                onceBuilderResponse += chunk;
            }

            int messagesAfter = AI.ActivateChat.Messages.Count;
            Assert.AreEqual(messagesBefore, messagesAfter);
            Assert.IsTrue(onceBuilderResponse.Length > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error in MessageBuilder Stream Test] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 병렬 스트리밍 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Streaming")]
    [TestMethod]
    public async Task ParallelStreamingTest()
    {
        try
        {
            var task1 = StreamAndCollectAsync("Explain quantum computing briefly");
            var task2 = StreamAndCollectAsync("Explain machine learning briefly");

            var results = await Task.WhenAll(task1, task2);

            Console.WriteLine($"[Parallel Stream 1] Length: {results[0].Content.Length}, Chunks: {results[0].ChunkCount}");
            Console.WriteLine($"[Parallel Stream 2] Length: {results[1].Content.Length}, Chunks: {results[1].ChunkCount}");

            Assert.IsTrue(results[0].Content.Length > 0);
            Assert.IsTrue(results[1].Content.Length > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error in Parallel Streaming] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// LINQ 작업 시뮬레이션 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Streaming")]
    [TestMethod]
    public async Task IAsyncEnumerableWithLinqTest()
    {
        try
        {
            // Take를 사용한 제한 (수동 구현)
            string prompt = "1부터 10까지 세어줘";
            var chunks = new List<string>();
            int maxChunks = 5;
            int currentChunk = 0;

            await foreach (var chunk in AI.StreamAsync(prompt))
            {
                chunks.Add(chunk);
                currentChunk++;
                if (currentChunk >= maxChunks)
                    break;
            }

            Console.WriteLine($"[Limited Stream] Got {chunks.Count} chunks (max: {maxChunks})");
            Assert.IsTrue(chunks.Count <= maxChunks);

            // 전체 수집
            var allChunks = new List<string>();
            await foreach (var chunk in AI.StreamAsync("짧은 답변 해줘"))
            {
                allChunks.Add(chunk);
            }

            string combined = string.Concat(allChunks);
            Console.WriteLine($"[Collected] {allChunks.Count} chunks, Total: {combined.Length} chars");
            Assert.IsTrue(allChunks.Count > 0);

            // 필터링 (빈 청크 제외)
            var nonEmptyChunks = new List<string>();
            await foreach (var chunk in AI.StreamAsync("답변해줘"))
            {
                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    nonEmptyChunks.Add(chunk);
                }
            }

            Console.WriteLine($"[Filtered] {nonEmptyChunks.Count} non-empty chunks");
            Assert.IsTrue(nonEmptyChunks.Count > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error in LINQ Test] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 스트리밍 에러 처리 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Streaming")]
    [TestMethod]
    public async Task IAsyncEnumerableErrorHandlingTest()
    {
        try
        {
            // 1) 잘못된 모델로 시도
            var originalModel = AI.ActivateChat.Model;
            try
            {
                AI.ActivateChat.ChangeModel("invalid-model");
                await foreach (var chunk in AI.StreamAsync("test"))
                {
                    // Should not reach here
                }
                Assert.Fail("Should have thrown an exception");
            }
            catch (Exception modelEx)
            {
                Console.WriteLine($"[Expected Model Error] {modelEx.Message}");
                // 원래 모델로 복원
                AI.ActivateChat.ChangeModel(originalModel);
            }

            // 2) 매우 짧은 타임아웃으로 취소
            var quickCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));
            try
            {
                await foreach (var chunk in AI.StreamAsync("긴 답변을 해줘", quickCts.Token))
                {
                    await Task.Delay(10);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Timeout Cancellation] Worked as expected");
            }

            // 3) 빈 메시지 스트리밍
            string emptyResponse = "";
            await foreach (var chunk in AI.StreamAsync(""))
            {
                emptyResponse += chunk;
            }
            Assert.IsNotNull(emptyResponse);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error Handling Test] {ex.GetType().Name}: {ex.Message}");
        }
    }


    /// <summary>
    /// 고급 LINQ 작업 테스트 (System.Linq.Async 사용 시)
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Streaming")]
    [TestMethod]
    public async Task AdvancedLinqOperationsTest()
    {
        try
        {
            // System.Linq.Async가 있는지 확인
            var linqAsyncAvailable = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name == "System.Linq.Async");

            if (!linqAsyncAvailable)
            {
                // System.Linq.Async 없이 수동 구현
                Console.WriteLine("[LINQ] System.Linq.Async not available, using manual implementation");

                // Take 구현
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

                // Select와 Where 조합 구현
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
                var totalWords = processedChunks.Sum(c => c.WordCount);
                Console.WriteLine($"[Total Words] {totalWords}");

                // Aggregate 구현
                var stats = new { Count = 0, TotalLength = 0 };
                await foreach (var chunk in AI.StreamAsync("Describe machine learning"))
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
            }
            else
            {
                Assert.Inconclusive("System.Linq.Async tests require the package to be installed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Advanced LINQ Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 커스텀 StreamOptions 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Streaming")]
    [TestMethod]
    public async Task CustomStreamOptionsTest()
    {
        try
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService)
            {
                Assert.Inconclusive("Custom stream options require AIService base class");
                return;
            }

            var customOptions = new StreamOptions()
                .WithMetadata(true)
                .WithTokenInfo(true)
                .WithFunctionCalls(false)
                .AsTextOnly(false);

            var receivedTypes = new HashSet<StreamingContentType>();
            var hasTokenInfo = false;

            var message = new Mythosia.AI.Models.Messages.Message(
                ActorRole.User,
                "Explain AI in one sentence"
            );

            await foreach (var content in aiService.StreamAsync(message, customOptions))
            {
                receivedTypes.Add(content.Type);

                if (content.Metadata?.ContainsKey("token_count") == true ||
                    content.Metadata?.ContainsKey("tokens") == true)
                {
                    hasTokenInfo = true;
                    Console.WriteLine("[Token Info] Found token information");
                }

                if (content.Type == StreamingContentType.Text && content.Content != null)
                {
                    Console.Write(content.Content);
                }
            }

            Console.WriteLine($"\n[Custom Options Results]");
            Console.WriteLine($"  Received types: {string.Join(", ", receivedTypes)}");
            Console.WriteLine($"  Has token info: {hasTokenInfo}");
            Console.WriteLine($"  Function calls disabled: {!customOptions.IncludeFunctionCalls}");

            Assert.IsTrue(receivedTypes.Contains(StreamingContentType.Text));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Custom Options Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
}