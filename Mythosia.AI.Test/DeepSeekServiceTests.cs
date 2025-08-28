using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.Azure;

namespace Mythosia.AI.Tests;

[TestClass]
public class DeepSeekServiceTests : AIServiceTestBase
{
    protected override AIService CreateAIService()
    {
        var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "deepseek-secret");
        string apiKey = secretFetcher.GetKeyValueAsync().Result;

        var service = new DeepSeekService(apiKey, new HttpClient());
        return service;
    }

    protected override bool SupportsMultimodal()
    {
        return false; // DeepSeek은 현재 텍스트만 지원
    }

    protected override AIModel? GetAlternativeModel()
    {
        return AIModel.DeepSeekReasoner;
    }

    /// <summary>
    /// DeepSeek Reasoner 모델 테스트
    /// </summary>
    [TestMethod]
    public async Task DeepSeekReasonerTest()
    {
        try
        {
            var deepSeekService = (DeepSeekService)AI;

            // Reasoner 모델로 전환
            deepSeekService.UseReasonerModel();

            // 복잡한 추론 문제
            var response = await deepSeekService.GetCompletionAsync(
                "If all roses are flowers, and some flowers fade quickly, can we conclude that some roses fade quickly? Explain your reasoning."
            );

            Assert.IsNotNull(response);
            Console.WriteLine($"[Reasoner Response] {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Reasoner Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// DeepSeek 코드 생성 모드 테스트
    /// </summary>
    [TestMethod]
    public async Task DeepSeekCodeGenerationTest()
    {
        try
        {
            var deepSeekService = (DeepSeekService)AI;

            // Python 코드 생성 모드
            deepSeekService.WithCodeGenerationMode("python");

            var codeResponse = await deepSeekService.GetCompletionAsync(
                "Write a function to calculate fibonacci numbers"
            );

            Assert.IsNotNull(codeResponse);
            Assert.IsTrue(codeResponse.Contains("def") || codeResponse.Contains("fibonacci"));
            Console.WriteLine($"[Code Generation]\n{codeResponse}");

            // JavaScript 코드 생성
            deepSeekService.WithCodeGenerationMode("javascript");

            var jsResponse = await deepSeekService.GetCompletionAsync(
                "Write a function to reverse a string"
            );

            Assert.IsNotNull(jsResponse);
            Console.WriteLine($"[JS Code]\n{jsResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Code Generation Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// DeepSeek 수학 모드 테스트
    /// </summary>
    [TestMethod]
    public async Task DeepSeekMathModeTest()
    {
        try
        {
            var deepSeekService = (DeepSeekService)AI;

            // 수학 모드 활성화
            deepSeekService.WithMathMode();

            var mathResponse = await deepSeekService.GetCompletionAsync(
                "Solve the equation: 2x^2 + 5x - 3 = 0"
            );

            Assert.IsNotNull(mathResponse);
            Console.WriteLine($"[Math Solution]\n{mathResponse}");

            // Chain of Thought 프롬프팅
            var cotResponse = await deepSeekService.GetCompletionWithCoTAsync(
                "If a train travels 120 km in 2 hours, and then 180 km in 3 hours, what is its average speed?"
            );

            Assert.IsNotNull(cotResponse);
            Assert.IsTrue(cotResponse.Contains("step") || cotResponse.Contains("Step"));
            Console.WriteLine($"[CoT Response]\n{cotResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Math Mode Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// DeepSeek 멀티모달 미지원 테스트
    /// </summary>
    [TestMethod]
    public async Task DeepSeekMultimodalNotSupportedTest()
    {
        try
        {
            // 이미지와 함께 시도
            var response = await AI.GetCompletionWithImageAsync(
                "What's in this image?",
                TestImagePath
            );

            // DeepSeek은 이미지를 무시하고 텍스트만 처리
            Assert.IsNotNull(response);
            Console.WriteLine($"[Image Attempt] {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Multimodal Error] {ex.Message}");
        }
    }

    /// <summary>
    /// DeepSeek 스트리밍 테스트
    /// </summary>
    [TestMethod]
    public async Task DeepSeekStreamingTest()
    {
        try
        {
            string fullResponse = "";
            int chunkCount = 0;

            await AI.StreamCompletionAsync(
                "Explain the concept of recursion with a simple example",
                chunk =>
                {
                    fullResponse += chunk;
                    chunkCount++;
                    Console.Write(chunk);
                }
            );

            Console.WriteLine($"\n[Streaming Complete] Chunks: {chunkCount}");
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
    /// DeepSeek 에러 처리 테스트
    /// </summary>
    [TestMethod]
    public async Task DeepSeekErrorHandlingTest()
    {
        try
        {
            // 매우 긴 입력으로 토큰 제한 테스트
            var longPrompt = new string('a', 10000);
            AI.ActivateChat.MaxTokens = 10; // 매우 작은 출력 제한

            var response = await AI.GetCompletionAsync(longPrompt);
            Assert.IsNotNull(response);
            Console.WriteLine($"[Token Limit Response] Length: {response.Length}");
        }
        catch (RateLimitExceededException ex)
        {
            Console.WriteLine($"[Rate Limit] {ex.Message}");
            if (ex.RetryAfter.HasValue)
            {
                Console.WriteLine($"[Retry After] {ex.RetryAfter.Value.TotalSeconds} seconds");
            }
            Assert.Inconclusive("Rate limit reached");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error Handling] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// DeepSeek 대화 관리 테스트
    /// </summary>
    [TestMethod]
    public async Task DeepSeekConversationTest()
    {
        try
        {
            // 컨텍스트를 유지하는 대화
            await AI.GetCompletionAsync("My favorite color is blue.");
            await AI.GetCompletionAsync("My favorite number is 42.");

            var response = await AI.GetCompletionAsync("What are my favorite color and number?");

            Assert.IsNotNull(response);
            Assert.IsTrue(response.Contains("blue") || response.Contains("Blue"));
            Assert.IsTrue(response.Contains("42"));
            Console.WriteLine($"[Context Test] {response}");

            // 대화 기록 확인
            Assert.AreEqual(6, AI.ActivateChat.Messages.Count); // 3 user + 3 assistant
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Conversation Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
}