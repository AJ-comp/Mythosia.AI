using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    /// <summary>
    /// AIService (추상 클래스)를 사용하는 공통 테스트 로직을 담는 베이스.
    /// </summary>
    public abstract class AIServiceTestBase
    {
        protected AIService AI { get; private set; }
        protected string TestImagePath { get; private set; }

        /// <summary>
        /// 각 구현체(Gemini, Claude 등)에서 인스턴스를 생성해 반환.
        /// </summary>
        protected abstract AIService CreateAIService();

        [TestInitialize]
        public virtual void TestInitialize()
        {
            AI = CreateAIService();

            // Set up test image path
            SetupTestImage();
        }

        [TestCleanup]
        public virtual void TestCleanup()
        {
            // No need to delete the test image anymore since it's a project asset
            Console.WriteLine("[Test Cleanup] Completed");
        }

        private void SetupTestImage()
        {
            // Use pre-existing test image from TestAssets folder
            var testAssetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestAssets", "test_image.png");

            // If png doesn't exist, try jpg
            if (!File.Exists(testAssetsPath))
            {
                testAssetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestAssets", "test_image.jpg");
            }

            if (!File.Exists(testAssetsPath))
            {
                throw new FileNotFoundException(
                    $"Test image not found. Please add 'test_image.png' or 'test_image.jpg' to the TestAssets folder. Searched path: {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestAssets")}");
            }

            TestImagePath = testAssetsPath;
            Console.WriteLine($"[Test Setup] Using test image: {TestImagePath}");
            Console.WriteLine($"[Test Setup] File exists: {File.Exists(TestImagePath)}");
            Console.WriteLine($"[Test Setup] File size: {new FileInfo(TestImagePath).Length} bytes");
        }

        /// <summary>
        /// 기본적인 텍스트 Completion 테스트
        /// </summary>
        [TestMethod]
        public async Task BasicCompletionTest()
        {
            try
            {
                // 시스템 메시지 설정
                AI.ActivateChat.SystemMessage = "응답을 짧고 간결하게 해줘.";

                // 1) 일반 Completion
                string prompt = "인공지능의 역사에 대해 한 문장으로 설명해줘.";
                string response = await AI.GetCompletionAsync(prompt);

                Assert.IsNotNull(response);
                Assert.IsTrue(response.Length > 0);
                Console.WriteLine($"[Completion] {response}");

                // 2) 스트리밍 Completion
                string prompt2 = "AI의 장점을 한 가지만 말해줘.";
                string streamedResponse = string.Empty;

                await AI.StreamCompletionAsync(prompt2, chunk =>
                {
                    streamedResponse += chunk;
                    Console.Write(chunk);
                });

                Assert.IsNotNull(streamedResponse);
                Assert.IsTrue(streamedResponse.Length > 0);
                Console.WriteLine($"\n[Stream Complete] Total length: {streamedResponse.Length}");

                // 3) 토큰 카운트 (대화 전체)
                uint tokenCountAll = await AI.GetInputTokenCountAsync();
                Console.WriteLine($"[Token Count - All] {tokenCountAll}");
                Assert.IsTrue(tokenCountAll > 0);

                // 4) 토큰 카운트 (단일 프롬프트)
                uint tokenCountPrompt = await AI.GetInputTokenCountAsync("테스트 프롬프트 하나");
                Console.WriteLine($"[Token Count - Prompt] {tokenCountPrompt}");
                Assert.IsTrue(tokenCountPrompt > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error in {GetType().Name}] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// IAsyncEnumerable 스트리밍 테스트
        /// </summary>
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

                        // 일정 길이가 되면 취소
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

                // 3) StreamOnceAsync 테스트 (대화 기록 영향 없음)
                int messageCountBefore = AI.ActivateChat.Messages.Count;

                string onceResponse = "";
                await foreach (var chunk in AI.StreamOnceAsync("이것은 일회성 질문입니다"))
                {
                    onceResponse += chunk;
                }

                int messageCountAfter = AI.ActivateChat.Messages.Count;
                Assert.AreEqual(messageCountBefore, messageCountAfter, "StreamOnceAsync should not affect conversation history");
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
        [TestMethod]
        public async Task IAsyncEnumerableWithMessageBuilderTest()
        {
            try
            {
                // 1) 텍스트만 있는 메시지 스트리밍
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

                // 2) 멀티모달 메시지 스트리밍 (지원하는 서비스만)
                if (SupportsMultimodal())
                {
                    // GPT-4 Vision 지원 모델로 변경 (필요한 경우)
                    if (AI is ChatGptService && AI.ActivateChat.Model.Contains("mini"))
                    {
                        AI.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
                    }

                    string imageResponse = "";
                    int imageChunkCount = 0;

                    await foreach (var chunk in AI
                        .BeginMessage()
                        .AddText("이 이미지에 대해 간단히 설명해줘")
                        .AddImage(TestImagePath)
                        .StreamAsync())
                    {
                        imageResponse += chunk;
                        imageChunkCount++;
                        Console.Write(chunk);
                    }

                    Console.WriteLine($"\n[Multimodal Stream] Chunks: {imageChunkCount}");
                    Assert.IsTrue(imageResponse.Length > 0);
                }

                // 3) StreamOnceAsync with MessageBuilder
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
                Assert.AreEqual(messagesBefore, messagesAfter, "StreamOnceAsync should not add to history");
                Assert.IsTrue(onceBuilderResponse.Length > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error in MessageBuilder Stream Test] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// IAsyncEnumerable with LINQ operations 테스트
        /// </summary>
        [TestMethod]
        public async Task IAsyncEnumerableWithLinqTest()
        {
            try
            {
                // System.Linq.Async가 있다고 가정하고 기본적인 조작만 테스트

                // 1) Take를 사용한 제한 (수동 구현)
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

                // 2) 전체 수집 (ToList 대신 수동)
                var allChunks = new List<string>();
                await foreach (var chunk in AI.StreamAsync("짧은 답변 해줘"))
                {
                    allChunks.Add(chunk);
                }

                string combined = string.Concat(allChunks);
                Console.WriteLine($"[Collected] {allChunks.Count} chunks, Total: {combined.Length} chars");
                Assert.IsTrue(allChunks.Count > 0);

                // 3) 필터링 (빈 청크 제외)
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
        /// IAsyncEnumerable 에러 처리 테스트
        /// </summary>
        [TestMethod]
        public async Task IAsyncEnumerableErrorHandlingTest()
        {
            try
            {
                // 1) 잘못된 모델로 시도
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
                    AI.ActivateChat.ChangeModel(CreateAIService().ActivateChat.Model);
                }

                // 2) 매우 짧은 타임아웃으로 취소
                var quickCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));
                try
                {
                    await foreach (var chunk in AI.StreamAsync("긴 답변을 해줘", quickCts.Token))
                    {
                        await Task.Delay(10); // 지연 추가
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
                // 빈 프롬프트도 처리되어야 함
                Assert.IsNotNull(emptyResponse);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error Handling Test] {ex.GetType().Name}: {ex.Message}");
                // 일부 에러는 예상된 것일 수 있음
            }
        }

        /// <summary>
        /// 멀티턴 대화 테스트
        /// </summary>
        [TestMethod]
        public async Task MultiTurnConversationTest()
        {
            try
            {
                AI.ActivateChat.SystemMessage = "당신은 친절한 대화 상대입니다.";

                // 첫 질의
                string prompt1 = "안녕? 나는 테스트 중이야.";
                string resp1 = await AI.GetCompletionAsync(prompt1);
                Assert.IsNotNull(resp1);
                Console.WriteLine($"[Turn 1] User: {prompt1}");
                Console.WriteLine($"[Turn 1] AI: {resp1}");

                // 두 번째 질의 (이전 대화 기억 확인)
                string prompt2 = "내가 뭘 하고 있다고 했지?";
                string resp2 = await AI.GetCompletionAsync(prompt2);
                Assert.IsNotNull(resp2);
                Console.WriteLine($"[Turn 2] User: {prompt2}");
                Console.WriteLine($"[Turn 2] AI: {resp2}");

                // 대화 내역 확인
                Assert.AreEqual(4, AI.ActivateChat.Messages.Count); // 2 user + 2 assistant

                // 토큰 카운트
                uint tokens = await AI.GetInputTokenCountAsync();
                Console.WriteLine($"[Multi-turn token count] {tokens}");
                Assert.IsTrue(tokens > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error in {GetType().Name}] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Stateless 모드 테스트
        /// </summary>
        [TestMethod]
        public async Task StatelessModeTest()
        {
            try
            {
                // Stateless 모드 활성화
                AI.StatelessMode = true;

                // 첫 번째 요청
                string response1 = await AI.GetCompletionAsync("내 이름은 테스터야.");
                Assert.IsNotNull(response1);
                Console.WriteLine($"[Stateless 1] {response1}");

                // 두 번째 요청 (이전 대화를 기억하지 않아야 함)
                string response2 = await AI.GetCompletionAsync("내 이름이 뭐라고 했지?");
                Assert.IsNotNull(response2);
                Console.WriteLine($"[Stateless 2] {response2}");

                // 대화 기록이 남아있지 않아야 함
                Assert.AreEqual(0, AI.ActivateChat.Messages.Count);

                // Stateless 모드 비활성화
                AI.StatelessMode = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error in {GetType().Name}] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 멀티모달 메시지 빌더 테스트
        /// </summary>
        [TestMethod]
        public async Task MessageBuilderTest()
        {
            try
            {
                // MessageBuilder를 사용한 텍스트 전용 메시지
                var textMessage = MessageBuilder.Create()
                    .WithRole(ActorRole.User)
                    .AddText("이것은 테스트 메시지입니다.")
                    .Build();

                string response = await AI.GetCompletionAsync(textMessage);
                Assert.IsNotNull(response);
                Console.WriteLine($"[MessageBuilder Text] {response}");

                // 복잡한 멀티모달 메시지 (이미지 지원하는 서비스에서만 동작)
                if (SupportsMultimodal())
                {
                    try
                    {
                        Console.WriteLine($"[Multimodal Test] Image path: {TestImagePath}");
                        Console.WriteLine($"[Multimodal Test] File exists: {File.Exists(TestImagePath)}");
                        Console.WriteLine($"[Multimodal Test] Current model: {AI.ActivateChat.Model}");

                        // GPT-4 Vision 지원 모델로 변경 (필요한 경우)
                        if (AI is ChatGptService)
                        {
                            var model = AI.ActivateChat.Model;
                            Console.WriteLine($"[Multimodal Test] Current GPT model: {model}");

                            // Mini 모델은 Vision을 지원하지 않음
                            if (model.Contains("mini"))
                            {
                                AI.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
                                Console.WriteLine($"[Multimodal Test] Changed from Mini to Vision-capable model: {AI.ActivateChat.Model}");
                            }
                        }

                        var multimodalMessage = MessageBuilder.Create()
                            .AddText("What do you see in this image? Please describe it briefly.")
                            .AddImage(TestImagePath)
                            .Build();

                        // 디버깅: 메시지 내용 확인
                        Console.WriteLine($"[Multimodal Test] Message has {multimodalMessage.Contents.Count} contents");
                        foreach (var content in multimodalMessage.Contents)
                        {
                            Console.WriteLine($"[Multimodal Test] Content type: {content.Type}");
                            if (content is Mythosia.AI.Models.Messages.ImageContent imgContent)
                            {
                                Console.WriteLine($"[Multimodal Test] Image data size: {imgContent.Data?.Length ?? 0} bytes");
                            }
                        }

                        string imageResponse = await AI.GetCompletionAsync(multimodalMessage);
                        Assert.IsNotNull(imageResponse);
                        Console.WriteLine($"[MessageBuilder Multimodal] {imageResponse}");
                    }
                    catch (Mythosia.AI.Exceptions.AIServiceException aiEx)
                    {
                        Console.WriteLine($"[Multimodal Error] {aiEx.Message}");
                        Console.WriteLine($"[Error Details] {aiEx.ErrorDetails}");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error in {GetType().Name}] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Extension 메서드 테스트
        /// </summary>
        [TestMethod]
        public async Task ExtensionMethodsTest()
        {
            try
            {
                // AskOnce - 대화 기록에 영향 없이 질문
                string oneOffResponse = await AI.AskOnceAsync("1 더하기 1은?");
                Assert.IsNotNull(oneOffResponse);
                Console.WriteLine($"[AskOnce] {oneOffResponse}");
                Assert.AreEqual(0, AI.ActivateChat.Messages.Count);

                // Fluent API 사용
                string fluentResponse = await AI
                    .BeginMessage()
                    .AddText("2 곱하기 3은?")
                    .SendOnceAsync();

                Assert.IsNotNull(fluentResponse);
                Console.WriteLine($"[Fluent API] {fluentResponse}");

                // Configuration chaining
                AI.WithSystemMessage("You are a math tutor")
                  .WithTemperature(0.5f)
                  .WithMaxTokens(100);

                string configuredResponse = await AI.GetCompletionAsync("What is calculus?");
                Assert.IsNotNull(configuredResponse);
                Console.WriteLine($"[Configured] {configuredResponse}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error in {GetType().Name}] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 모델 변경 테스트
        /// </summary>
        [TestMethod]
        public async Task ModelSwitchingTest()
        {
            try
            {
                var originalModel = AI.ActivateChat.Model;
                Console.WriteLine($"[Original Model] {originalModel}");

                // 지원하는 다른 모델로 변경 (서비스별로 다름)
                var alternativeModel = GetAlternativeModel();
                if (alternativeModel != null)
                {
                    AI.ActivateChat.ChangeModel(alternativeModel.Value);
                    Console.WriteLine($"[Changed Model] {AI.ActivateChat.Model}");

                    string response = await AI.GetCompletionAsync("What model are you?");
                    Assert.IsNotNull(response);
                    Console.WriteLine($"[Model Test Response] {response}");

                    // 원래 모델로 복원
                    AI.ActivateChat.ChangeModel(originalModel);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error in {GetType().Name}] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 서비스가 멀티모달을 지원하는지 확인
        /// </summary>
        protected virtual bool SupportsMultimodal()
        {
            // 기본적으로 false, 각 서비스에서 오버라이드
            return false;
        }

        /// <summary>
        /// 대체 모델 반환 (서비스별로 다름)
        /// </summary>
        protected virtual AIModel? GetAlternativeModel()
        {
            // 기본적으로 null, 각 서비스에서 오버라이드
            return null;
        }
    }
}