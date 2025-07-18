using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.Azure;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    [TestClass]
    public class ClaudeServiceTests : AIServiceTestBase
    {
        protected override AIService CreateAIService()
        {
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-antropic-secret");
            string apiKey = secretFetcher.GetKeyValueAsync().Result;

            var service = new ClaudeService(apiKey, new HttpClient());
            // Claude 3.5 Haiku는 비용 효율적
            service.ActivateChat.ChangeModel(AIModel.Claude3_5Haiku241022);
            return service;
        }

        protected override bool SupportsMultimodal()
        {
            return true; // Claude 3 모델들은 Vision 지원
        }

        protected override AIModel? GetAlternativeModel()
        {
            return AIModel.Claude3_5Sonnet241022;
        }

        /// <summary>
        /// Claude Vision 기능 테스트
        /// </summary>
        [TestMethod]
        public async Task ClaudeVisionTest()
        {
            try
            {
                // Claude 3 모델은 기본적으로 vision 지원
                var response = await AI.GetCompletionWithImageAsync(
                    "What do you see in this image? Be brief.",
                    TestImagePath
                );

                Assert.IsNotNull(response);
                Console.WriteLine($"[Claude Vision] {response}");

                // 다중 이미지는 MessageBuilder 사용
                var multiImageResponse = await AI
                    .BeginMessage()
                    .AddText("Compare these two images:")
                    .AddImage(TestImagePath)
                    .AddImage(TestImagePath) // 테스트를 위해 같은 이미지 사용
                    .SendAsync();

                Assert.IsNotNull(multiImageResponse);
                Console.WriteLine($"[Claude Multi-Image] {multiImageResponse}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Claude Vision Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Claude 특화 기능 테스트
        /// </summary>
        [TestMethod]
        public async Task ClaudeSpecificFeaturesTest()
        {
            try
            {
                var claudeService = (ClaudeService)AI;

                // Claude 파라미터 설정
                claudeService
                    .WithClaudeParameters(topK: 10)
                    .WithConstitutionalAI(true);

                var response = await claudeService.GetCompletionAsync(
                    "Explain constitutional AI briefly."
                );

                Assert.IsNotNull(response);
                Console.WriteLine($"[Claude Features] {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Claude Features Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Claude는 이미지 생성을 지원하지 않음
        /// </summary>
        [TestMethod]
        public async Task ClaudeImageGenerationNotSupportedTest()
        {
            try
            {
                await AI.GenerateImageAsync("test prompt");
                Assert.Fail("Should have thrown MultimodalNotSupportedException");
            }
            catch (MultimodalNotSupportedException ex)
            {
                Assert.AreEqual("Claude", ex.ServiceName);
                Assert.AreEqual("Image Generation", ex.RequestedFeature);
                Console.WriteLine($"[Expected Exception] {ex.Message}");
            }
        }

        /// <summary>
        /// Claude 토큰 카운팅 API 테스트
        /// </summary>
        [TestMethod]
        public async Task ClaudeTokenCountingTest()
        {
            try
            {
                // 단순 프롬프트 토큰 카운트
                var simpleTokens = await AI.GetInputTokenCountAsync("Hello, Claude!");
                Assert.IsTrue(simpleTokens > 0);
                Console.WriteLine($"[Simple Token Count] {simpleTokens}");

                // 대화 추가 후 전체 토큰 카운트
                await AI.GetCompletionAsync("Tell me about Paris.");
                await AI.GetCompletionAsync("What about its population?");

                var totalTokens = await AI.GetInputTokenCountAsync();
                Assert.IsTrue(totalTokens > simpleTokens);
                Console.WriteLine($"[Total Token Count] {totalTokens}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Token Counting Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Claude 모델 전환 테스트
        /// </summary>
        [TestMethod]
        public async Task ClaudeModelSwitchingTest()
        {
            try
            {
                var models = new[]
                {
                    AIModel.Claude3_5Haiku241022,
                    AIModel.Claude3_5Sonnet241022,
                    AIModel.Claude3Opus240229
                };

                foreach (var model in models)
                {
                    try
                    {
                        AI.ActivateChat.ChangeModel(model);
                        Console.WriteLine($"\n[Testing Model] {model.ToDescription()}");

                        var response = await AI.GetCompletionAsync($"Hello from {model}!");
                        Assert.IsNotNull(response);
                        Console.WriteLine($"[Response] {response.Substring(0, Math.Min(100, response.Length))}...");
                    }
                    catch (Exception modelEx)
                    {
                        // Some models might not be available
                        Console.WriteLine($"[Model {model} Error] {modelEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Model Switching Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Claude 스트리밍 테스트
        /// </summary>
        [TestMethod]
        public async Task ClaudeStreamingTest()
        {
            try
            {
                int chunkCount = 0;
                string fullResponse = "";

                await AI.StreamCompletionAsync(
                    "Count from 1 to 5 slowly, with explanation for each number.",
                    chunk =>
                    {
                        chunkCount++;
                        fullResponse += chunk;
                        Console.Write(chunk);
                    }
                );

                Console.WriteLine($"\n[Stream Stats] Chunks: {chunkCount}, Total Length: {fullResponse.Length}");
                Assert.IsTrue(chunkCount > 1);
                Assert.IsTrue(fullResponse.Contains("1") && fullResponse.Contains("5"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Streaming Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Claude의 대화 컨텍스트 관리 테스트
        /// </summary>
        [TestMethod]
        public async Task ClaudeContextManagementTest()
        {
            try
            {
                // 긴 컨텍스트 설정
                AI.ActivateChat.MaxMessageCount = 10;

                // 여러 대화 추가
                for (int i = 1; i <= 5; i++)
                {
                    await AI.GetCompletionAsync($"Remember number {i}");
                }

                // 컨텍스트 확인 - Extension method 사용
                var contextResponse = await AI.GetCompletionWithContextAsync(
                    "What numbers did I mention?",
                    contextMessages: 5
                );

                Assert.IsNotNull(contextResponse);
                Console.WriteLine($"[Context Response] {contextResponse}");

                // 대화 요약
                var summary = AI.ActivateChat.GetConversationSummary();
                Console.WriteLine($"[Conversation Summary]\n{summary}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Context Management Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}