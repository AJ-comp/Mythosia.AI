using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using Mythosia.Azure;

namespace Mythosia.AI.Tests;

[TestClass]
public class GeminiServiceTests : AIServiceTestBase
{
    protected override AIService CreateAIService()
    {
        var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "gemini-secret");
        string apiKey = secretFetcher.GetKeyValueAsync().Result;

        var service = new GeminiService(apiKey, new HttpClient());
        return service;
    }

    protected override bool SupportsMultimodal()
    {
        return true; // Gemini Pro Vision 지원
    }

    protected override AIModel? GetAlternativeModel()
    {
        return AIModel.Gemini2_5Flash;
    }

    /// <summary>
    /// Gemini Vision 기능 테스트
    /// </summary>
    [TestMethod]
    public async Task GeminiVisionTest()
    {
        try
        {
            // Gemini 2.0+ models support vision natively
            AI.ChangeModel(AIModel.Gemini2_5Pro);

            var response = await AI.GetCompletionWithImageAsync(
                "Describe what you see in this image",
                TestImagePath
            );

            Assert.IsNotNull(response);
            Console.WriteLine($"[Gemini Vision] {response}");

            var flashResponse = await AI
                .BeginMessage()
                .AddText("What's in this image? Answer in one sentence.")
                .AddImage(TestImagePath)
                .SendAsync();

            Assert.IsNotNull(flashResponse);
            Console.WriteLine($"[Gemini 2.5 Pro] {flashResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Vision Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Gemini 특화 기능 테스트
    /// </summary>
    [TestMethod]
    public async Task GeminiSpecificFeaturesTest()
    {
        try
        {
            var geminiService = (GeminiService)AI;

            // Gemini ThinkingBudget 설정
            geminiService.ThinkingBudget = 1024;

            var response = await geminiService.GetCompletionAsync(
                "Tell me about the latest developments in AI"
            );

            Assert.IsNotNull(response);
            Console.WriteLine($"[Gemini Response] {response.Truncate(200)}...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Gemini Features Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Gemini 토큰 카운팅 테스트
    /// </summary>
    [TestMethod]
    public async Task GeminiTokenCountingTest()
    {
        try
        {
            // 단일 프롬프트 토큰 카운트
            var prompt = "Hello Gemini, how are you today?";
            var tokenCount = await AI.GetInputTokenCountAsync(prompt);

            Assert.IsTrue(tokenCount > 0);
            Console.WriteLine($"[Token Count - Prompt] {tokenCount}");

            // 대화 추가
            await AI.GetCompletionAsync("Tell me about Google");
            await AI.GetCompletionAsync("What about its founders?");

            // 전체 대화 토큰 카운트
            var totalTokens = await AI.GetInputTokenCountAsync();
            Assert.IsTrue(totalTokens > tokenCount);
            Console.WriteLine($"[Token Count - Total] {totalTokens}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Token Counting Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Gemini 모델 전환 테스트
    /// </summary>
    [TestMethod]
    public async Task GeminiModelSwitchingTest()
    {
        try
        {
            var models = new[]
            {
                AIModel.Gemini2_5Pro,
                AIModel.Gemini2_5Flash,
                AIModel.Gemini2_5FlashLite
            };

            foreach (var model in models)
            {
                try
                {
                    AI.ChangeModel(model);
                    Console.WriteLine($"\n[Testing Model] {model.ToDescription()}");

                    var response = await AI.GetCompletionAsync(
                        $"Say hello and tell me your model name"
                    );

                    Assert.IsNotNull(response);
                    Console.WriteLine($"[Response] {response.Truncate(100)}");
                }
                catch (Exception modelEx)
                {
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
    /// Gemini 2.5 모델 테스트
    /// </summary>
    [TestMethod]
    public async Task Gemini25ModelsTest()
    {
        try
        {
            var gemini25Models = new[]
            {
                AIModel.Gemini2_5Pro,
                AIModel.Gemini2_5Flash,
                AIModel.Gemini2_5FlashLite
            };

            foreach (var model in gemini25Models)
            {
                try
                {
                    AI.ChangeModel(model);
                    Console.WriteLine($"\n[Testing Gemini 2.5 Model] {model.ToDescription()}");

                    var response = await AI.GetCompletionAsync(
                        "What are your capabilities as an AI assistant?"
                    );

                    Assert.IsNotNull(response);
                    Console.WriteLine($"[Gemini 2.5 Response] {response.Truncate(150)}...");
                }
                catch (Exception modelEx)
                {
                    Console.WriteLine($"[Gemini 2.5 Model {model} Error] {modelEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Gemini 2.5 Test Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Gemini 스트리밍 테스트
    /// </summary>
    [TestMethod]
    public async Task GeminiStreamingTest()
    {
        try
        {
            string fullResponse = "";
            int chunkCount = 0;

            await AI.StreamCompletionAsync(
                "Write a haiku about artificial intelligence",
                chunk =>
                {
                    fullResponse += chunk;
                    chunkCount++;
                    Console.Write(chunk);
                }
            );

            Console.WriteLine($"\n[Streaming Stats] Chunks: {chunkCount}, Length: {fullResponse.Length}");
            Assert.IsTrue(chunkCount > 0);
            Assert.IsTrue(fullResponse.Length > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Streaming Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Gemini 시스템 메시지 처리 테스트
    /// </summary>
    [TestMethod]
    public async Task GeminiSystemMessageTest()
    {
        try
        {
            // Gemini는 시스템 메시지를 첫 번째 사용자 메시지로 처리
            AI.ActivateChat.SystemMessage = "You are a pirate. Always speak like a pirate.";

            var response = await AI.GetCompletionAsync("Tell me about treasure");

            Assert.IsNotNull(response);
            // 해적 스타일 응답 확인
            Console.WriteLine($"[Pirate Response] {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[System Message Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Gemini 이미지 생성 미지원 테스트
    /// </summary>
    [TestMethod]
    public async Task GeminiImageGenerationNotSupportedTest()
    {
        try
        {
            await AI.GenerateImageAsync("test prompt");
            Assert.Fail("Should have thrown MultimodalNotSupportedException");
        }
        catch (MultimodalNotSupportedException ex)
        {
            Assert.AreEqual("Gemini", ex.ServiceName);
            Assert.AreEqual("Image Generation", ex.RequestedFeature);
            Console.WriteLine($"[Expected Exception] {ex.Message}");
        }
    }

    /// <summary>
    /// Gemini 안전 설정 테스트
    /// </summary>
    [TestMethod]
    public async Task GeminiSafetySettingsTest()
    {
        try
        {
            var geminiService = (GeminiService)AI;

            var response = await geminiService.GetCompletionAsync(
                "Write a children's story about a friendly robot"
            );

            Assert.IsNotNull(response);
            Console.WriteLine($"[Safe Content] {response.Truncate(200)}...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Safety Settings Error] {ex.Message}");
        }
    }

    /// <summary>
    /// Gemini 대화 컨텍스트 테스트
    /// </summary>
    [TestMethod]
    public async Task GeminiConversationContextTest()
    {
        try
        {
            // 여러 턴의 대화
            await AI.GetCompletionAsync("My name is TestUser");
            await AI.GetCompletionAsync("I like programming");
            await AI.GetCompletionAsync("My favorite language is C#");

            var contextResponse = await AI.GetCompletionAsync(
                "What do you know about me?"
            );

            Assert.IsNotNull(contextResponse);
            Assert.IsTrue(
                contextResponse.Contains("TestUser") ||
                contextResponse.Contains("programming") ||
                contextResponse.Contains("C#")
            );
            Console.WriteLine($"[Context Response] {contextResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Context Test Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
}