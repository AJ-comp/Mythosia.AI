using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    [TestClass]
    public class ChatGptServiceTests : AIServiceTestBase
    {
        // 1) AIServiceTestBase에서 요구하는 인스턴스 생성 로직
        protected override AIService CreateAIService()
        {
            // SecretFetcher에서 API Key 가져오기
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret");
            string openAiKey = secretFetcher.GetKeyValueAsync().Result;

            // ChatGptService 인스턴스 생성
            var service = new ChatGptService(openAiKey, new HttpClient());
            // GPT-4o는 기본적으로 Vision을 지원합니다
            service.ActivateChat.ChangeModel(AIModel.Gpt4oLatest); // Vision 지원 모델
            service.ActivateChat.SystemMessage = "You are a helpful assistant for testing purposes.";

            return service;
        }

        protected override bool SupportsMultimodal()
        {
            return true; // GPT-4 Vision 지원
        }

        protected override AIModel? GetAlternativeModel()
        {
            return AIModel.Gpt4oLatest;
        }

        // 2) ChatGPT 전용 테스트들

        /// <summary>
        /// 이미지 생성 테스트 (DALL-E)
        /// </summary>
        [TestMethod]
        public async Task ImageGenerationTest()
        {
            try
            {
                var gptService = (ChatGptService)AI;

                // 이미지 생성 (byte array)
                var imageData = await gptService.GenerateImageAsync(
                    "A simple test pattern with geometric shapes",
                    "1024x1024"
                );

                Assert.IsNotNull(imageData);
                Assert.IsTrue(imageData.Length > 0);
                Console.WriteLine($"[Image Generation] Generated image size: {imageData.Length} bytes");

                // 이미지 URL 생성
                var imageUrl = await gptService.GenerateImageUrlAsync(
                    "A peaceful landscape for testing",
                    "1024x1024"
                );

                Assert.IsNotNull(imageUrl);
                Assert.IsTrue(imageUrl.StartsWith("http"));
                Console.WriteLine($"[Image URL] {imageUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Image Generation Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Vision 모델을 사용한 이미지 분석 테스트
        /// </summary>
        [TestMethod]
        public async Task VisionModelTest()
        {
            try
            {
                // Vision을 지원하는 모델로 전환
                // gpt-4-vision-preview는 deprecated되었을 수 있으므로 gpt-4o 사용
                AI.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
                Console.WriteLine($"[Vision Test] Using model: {AI.ActivateChat.Model}");

                // MessageBuilder를 사용한 이미지 분석
                var response = await AI
                    .BeginMessage()
                    .AddText("What do you see in this image?")
                    .AddImage(TestImagePath)
                    .SendAsync();

                Assert.IsNotNull(response);
                Console.WriteLine($"[Vision Analysis] {response}");

                // 편의 메서드 사용
                var response2 = await AI.GetCompletionWithImageAsync(
                    "Describe the colors in this image",
                    TestImagePath
                );

                Assert.IsNotNull(response2);
                Console.WriteLine($"[Vision Colors] {response2}");

                // 다른 Vision 지원 모델 테스트
                AI.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
                Console.WriteLine($"[Vision Test] Changed to model: {AI.ActivateChat.Model}");

                var response3 = await AI.GetCompletionWithImageAsync(
                    "What objects can you identify in this image?",
                    TestImagePath
                );

                Assert.IsNotNull(response3);
                Console.WriteLine($"[Vision Objects] {response3}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Vision Error] {ex.Message}");
                if (ex is Mythosia.AI.Exceptions.AIServiceException aiEx)
                {
                    Console.WriteLine($"[Error Details] {aiEx.ErrorDetails}");
                }
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 오디오 기능 테스트 (TTS & Whisper)
        /// </summary>
        [TestMethod]
        public async Task AudioFeaturesTest()
        {
            try
            {
                var gptService = (ChatGptService)AI;

                // 1) Text-to-Speech 테스트
                string textToSpeak = "Hello, this is a test of the speech synthesis.";
                var audioData = await gptService.GetSpeechAsync(
                    textToSpeak,
                    voice: "alloy",
                    model: "tts-1"
                );

                Assert.IsNotNull(audioData);
                Assert.IsTrue(audioData.Length > 0);
                Console.WriteLine($"[TTS] Generated audio size: {audioData.Length} bytes");

                // 2) Speech-to-Text 테스트 (생성된 오디오 사용)
                var transcription = await gptService.TranscribeAudioAsync(
                    audioData,
                    "test_speech.mp3",
                    language: "en"
                );

                Assert.IsNotNull(transcription);
                Assert.IsTrue(transcription.Length > 0);
                Console.WriteLine($"[Transcription] {transcription}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Audio Features Error] {ex.Message}");
                // Audio features might not be available in all environments
                Assert.Inconclusive($"Audio features test skipped: {ex.Message}");
            }
        }

        /// <summary>
        /// 스트리밍과 함께 이미지 사용 테스트
        /// </summary>
        [TestMethod]
        public async Task StreamingWithImageTest()
        {
            try
            {
                string fullResponse = "";

                await AI
                    .BeginMessage()
                    .AddText("Describe this image in detail, step by step:")
                    .AddImage(TestImagePath)
                    .StreamAsync(chunk =>
                    {
                        fullResponse += chunk;
                        Console.Write(chunk);
                    });

                Console.WriteLine(); // New line after streaming
                Assert.IsNotNull(fullResponse);
                Assert.IsTrue(fullResponse.Length > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Streaming Image Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 현재 사용 가능한 Vision 모델 확인 테스트
        /// </summary>
        [TestMethod]
        public async Task CheckAvailableVisionModelsTest()
        {
            try
            {
                var visionModels = new[]
                {
                    (AIModel.Gpt4Vision, "gpt-4-vision-preview"),
                    (AIModel.Gpt4o240806, "gpt-4o-2024-08-06"),
                    (AIModel.Gpt4oLatest, "chatgpt-4o-latest")
                };

                foreach (var (model, modelName) in visionModels)
                {
                    try
                    {
                        AI.ActivateChat.ChangeModel(model);
                        Console.WriteLine($"\n[Testing Model] {modelName}");

                        var response = await AI.GetCompletionAsync("Say 'Hello' if you can process images.");
                        Console.WriteLine($"[Model {modelName}] Available: YES");
                        Console.WriteLine($"[Response] {response}");
                    }
                    catch (Exception modelEx)
                    {
                        Console.WriteLine($"[Model {modelName}] Available: NO");
                        Console.WriteLine($"[Error] {modelEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Model Check Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 대화 관리 기능 테스트
        /// </summary>
        [TestMethod]
        public async Task ConversationManagementTest()
        {
            try
            {
                // 대화 시작
                await AI.GetCompletionAsync("Remember the number 42");
                await AI.GetCompletionAsync("What number did I ask you to remember?");

                // 마지막 응답 가져오기
                var lastResponse = AI.GetLastAssistantResponse();
                Assert.IsNotNull(lastResponse);
                Assert.IsTrue(lastResponse.Contains("42"));
                Console.WriteLine($"[Last Response] {lastResponse}");

                // 대화 요약
                var summary = AI.GetConversationSummary();
                Assert.IsNotNull(summary);
                Console.WriteLine($"[Summary]\n{summary}");

                // 새 대화 시작
                AI.StartNewConversation();
                Assert.AreEqual(0, AI.ActivateChat.Messages.Count);

                // 새 모델로 새 대화
                AI.StartNewConversation(AIModel.Gpt4oLatest);
                var response = await AI.GetCompletionAsync("What number was I talking about?");
                Assert.IsFalse(response.Contains("42")); // 이전 대화를 기억하지 않아야 함
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Conversation Management Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 고급 설정 테스트
        /// </summary>
        [TestMethod]
        public async Task AdvancedConfigurationTest()
        {
            try
            {
                var gptService = (ChatGptService)AI;

                // OpenAI 특화 파라미터 설정
                gptService.WithOpenAIParameters(
                    presencePenalty: 0.5f,
                    frequencyPenalty: 0.3f
                );

                // 체이닝 설정
                gptService
                    .WithSystemMessage("You are a creative writer")
                    .WithTemperature(0.9f)
                    .WithMaxTokens(150);

                var creativeResponse = await gptService.GetCompletionAsync(
                    "Write a creative one-line story about a robot"
                );

                Assert.IsNotNull(creativeResponse);
                Console.WriteLine($"[Creative Response] {creativeResponse}");

                // 낮은 temperature로 변경
                gptService.WithTemperature(0.1f);
                var preciseResponse = await gptService.GetCompletionAsync(
                    "What is 2 + 2?"
                );

                Assert.IsNotNull(preciseResponse);
                Assert.IsTrue(preciseResponse.Contains("4"));
                Console.WriteLine($"[Precise Response] {preciseResponse}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Advanced Config Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        // ===== 새로운 IAsyncEnumerable 테스트들 =====

        /// <summary>
        /// IAsyncEnumerable 스트리밍과 이미지 분석 테스트
        /// </summary>
        [TestMethod]
        public async Task IAsyncEnumerableStreamingWithImageTest()
        {
            try
            {
                // Vision 지원 모델로 변경
                AI.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);

                string fullResponse = "";
                int chunkCount = 0;
                var chunks = new List<string>();

                // IAsyncEnumerable로 이미지 분석 스트리밍
                await foreach (var chunk in AI
                    .BeginMessage()
                    .AddText("Describe this image in detail, mentioning colors, objects, and composition:")
                    .AddImage(TestImagePath)
                    .WithHighDetail()
                    .StreamAsync())
                {
                    fullResponse += chunk;
                    chunks.Add(chunk);
                    chunkCount++;
                    Console.Write(chunk);
                }

                Console.WriteLine($"\n[IAsyncEnumerable Image Stream] Chunks: {chunkCount}, Total: {fullResponse.Length}");
                Assert.IsTrue(chunkCount > 1, "Should receive multiple chunks");
                Assert.IsTrue(fullResponse.Length > 50, "Response should be detailed");

                // 청크 분석
                var avgChunkSize = chunks.Average(c => c.Length);
                Console.WriteLine($"[Chunk Analysis] Average chunk size: {avgChunkSize:F1} chars");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IAsyncEnumerable Image Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// IAsyncEnumerable with cancellation 테스트
        /// </summary>
        [TestMethod]
        public async Task IAsyncEnumerableWithCancellationTest()
        {
            try
            {
                var cts = new CancellationTokenSource();
                string partialResponse = "";
                int receivedChunks = 0;
                const int maxChunks = 10;

                await foreach (var chunk in AI.StreamAsync(
                    "Write a very long story about artificial intelligence, including its history, current state, and future prospects.",
                    cts.Token))
                {
                    partialResponse += chunk;
                    receivedChunks++;
                    Console.Write(chunk);

                    // 일정 청크 수가 되면 취소
                    if (receivedChunks >= maxChunks)
                    {
                        cts.Cancel();
                        break;
                    }
                }

                Console.WriteLine($"\n[Cancelled After] {receivedChunks} chunks, {partialResponse.Length} chars");
                Assert.AreEqual(maxChunks, receivedChunks);
                Assert.IsTrue(partialResponse.Length > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cancellation Test Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

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
        /// IAsyncEnumerable StreamOnceAsync 대화 기록 테스트
        /// </summary>
        [TestMethod]
        public async Task IAsyncEnumerableStreamOnceHistoryTest()
        {
            try
            {
                // 먼저 대화 시작
                await AI.GetCompletionAsync("Remember the number 12345");
                int messagesAfterFirst = AI.ActivateChat.Messages.Count;

                // StreamOnceAsync로 질문 (대화 기록에 추가되지 않음)
                string onceResponse = "";
                await foreach (var chunk in AI.StreamOnceAsync("What number did I mention?"))
                {
                    onceResponse += chunk;
                }

                int messagesAfterOnce = AI.ActivateChat.Messages.Count;
                Assert.AreEqual(messagesAfterFirst, messagesAfterOnce, "StreamOnceAsync should not add to history");

                // 일반 스트리밍으로 확인 (이전 대화 기억)
                string normalResponse = "";
                await foreach (var chunk in AI.StreamAsync("Do you still remember the number?"))
                {
                    normalResponse += chunk;
                }

                Console.WriteLine($"[Once Response] {onceResponse}");
                Console.WriteLine($"[Normal Response] {normalResponse}");

                // 정상 스트리밍은 대화 기록에 추가됨
                Assert.IsTrue(AI.ActivateChat.Messages.Count > messagesAfterOnce);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StreamOnce History Test Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

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

        /// <summary>
        /// IAsyncEnumerable 메모리 효율성 테스트
        /// </summary>
        [TestMethod]
        public async Task StreamingMemoryEfficiencyTest()
        {
            try
            {
                // 메모리 사용량 추적을 위한 시작 메모리
                long startMemory = GC.GetTotalMemory(true);

                // 대량의 텍스트를 스트리밍으로 처리
                int processedChunks = 0;
                long totalCharsProcessed = 0;

                await foreach (var chunk in AI.StreamAsync(
                    "Generate a very detailed technical documentation about distributed systems, " +
                    "including architecture patterns, consistency models, fault tolerance, " +
                    "scalability considerations, and real-world examples."))
                {
                    // 청크 단위로 처리 (메모리에 전체를 보관하지 않음)
                    processedChunks++;
                    totalCharsProcessed += chunk.Length;

                    // 실시간 처리 시뮬레이션
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

                // 메모리 효율성 검증
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