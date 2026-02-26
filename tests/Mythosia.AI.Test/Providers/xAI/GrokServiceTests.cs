using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.xAI;
using Mythosia.AI.Tests;
using Mythosia.Azure;

namespace Mythosia.AI.Tests.xAI;

// 1. Abstract base class
[TestClass]
public abstract class GrokServiceTestsBase : AIServiceTestBase
{
    private static string apiKey;
    protected abstract AIModel ModelToTest { get; }

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassInit(TestContext context)
    {
        if (apiKey == null)
        {
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "xai-secret");
            apiKey = await secretFetcher.GetKeyValueAsync();
            Console.WriteLine("[ClassInitialize] xAI API key loaded");
        }
    }

    protected override AIService CreateAIService()
    {
        var service = new GrokService(apiKey, new HttpClient());
        service.ChangeModel(ModelToTest);
        Console.WriteLine($"[Testing Model] {ModelToTest}");
        return service;
    }

    protected override bool SupportsMultimodal() =>
        ModelToTest == AIModel.Grok4 || ModelToTest == AIModel.Grok4_1Fast;
    protected override bool SupportsFunctionCalling() => true;
    protected override bool SupportsImageGeneration() => false;
    protected override bool SupportsAudio() => false;
    protected override bool SupportsWebSearch() => false;
    protected override AIModel? GetAlternativeModel() => AIModel.Grok3Mini;

    #region Grok-Specific Tests

    /// <summary>
    /// Grok 모델 전환 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task GrokModelSwitchTest()
    {
        try
        {
            var grokService = (GrokService)AI;

            // Mini 모델로 전환
            grokService.UseMiniModel();
            Assert.AreEqual(AIModel.Grok3Mini.ToDescription(), grokService.Model);

            var miniResponse = await grokService.GetCompletionAsync(
                "What is 2 + 2? Answer in one word."
            );
            Assert.IsNotNull(miniResponse);
            Console.WriteLine($"[Grok Mini] {miniResponse}");

            // Grok 4 모델로 전환
            grokService.UseGrok4Model();
            Assert.AreEqual(AIModel.Grok4.ToDescription(), grokService.Model);

            var g4Response = await grokService.GetCompletionAsync(
                "Explain quantum computing in one sentence."
            );
            Assert.IsNotNull(g4Response);
            Console.WriteLine($"[Grok 4] {g4Response}");

            // Grok 4.1 Fast 모델로 전환
            grokService.UseGrok4FastModel();
            Assert.AreEqual(AIModel.Grok4_1Fast.ToDescription(), grokService.Model);

            var fastResponse = await grokService.GetCompletionAsync(
                "What is the speed of light?"
            );
            Assert.IsNotNull(fastResponse);
            Console.WriteLine($"[Grok 4.1 Fast] {fastResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Model Switch Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Grok 코드 생성 모드 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task GrokCodeGenerationTest()
    {
        try
        {
            var grokService = (GrokService)AI;

            // Python 코드 생성 모드
            grokService.WithCodeGenerationMode("python");

            var codeResponse = await grokService.GetCompletionAsync(
                "Write a function to calculate fibonacci numbers"
            );

            Assert.IsNotNull(codeResponse);
            Assert.IsTrue(codeResponse.Contains("def") || codeResponse.Contains("fibonacci"));
            Console.WriteLine($"[Code Generation]\n{codeResponse}");

            // JavaScript 코드 생성
            grokService.WithCodeGenerationMode("javascript");

            var jsResponse = await grokService.GetCompletionAsync(
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
    /// Grok Chain of Thought 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task GrokChainOfThoughtTest()
    {
        try
        {
            var grokService = (GrokService)AI;

            var cotResponse = await grokService.GetCompletionWithCoTAsync(
                "If a train travels 120 km in 2 hours, and then 180 km in 3 hours, what is its average speed?"
            );

            Assert.IsNotNull(cotResponse);
            Assert.IsTrue(cotResponse.Contains("step") || cotResponse.Contains("Step") || cotResponse.Contains("60"));
            Console.WriteLine($"[CoT Response]\n{cotResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CoT Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Grok 스트리밍 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task GrokStreamingTest()
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
    /// Grok IAsyncEnumerable 스트리밍 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task GrokAsyncEnumerableStreamTest()
    {
        try
        {
            var (content, chunkCount) = await StreamAndCollectAsync("Tell me a short joke.");

            Console.WriteLine($"\n[AsyncEnum Stream] Chunks: {chunkCount}, Content: {content}");
            Assert.IsTrue(chunkCount > 0);
            Assert.IsTrue(content.Length > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AsyncEnum Stream Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Grok 이미지 생성 미지원 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task GrokImageGenerationNotSupportedTest()
    {
        try
        {
            await AI.GenerateImageAsync("A beautiful sunset");
            Assert.Fail("Should have thrown MultimodalNotSupportedException");
        }
        catch (MultimodalNotSupportedException)
        {
            Console.WriteLine("[Expected] Grok doesn't support image generation");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Unexpected Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Grok 에러 처리 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task GrokErrorHandlingTest()
    {
        try
        {
            // 매우 긴 입력으로 토큰 제한 테스트
            var longPrompt = new string('a', 10000);
            AI.MaxTokens = 10; // 매우 작은 출력 제한

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
    /// Grok 대화 관리 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task GrokConversationTest()
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

    #endregion
}

// 2. 모델별 구체 클래스

[TestClass]
public class xAI_Grok4_Tests : GrokServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Grok4;
}

[TestClass]
public class xAI_Grok4_1Fast_Tests : GrokServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Grok4_1Fast;
}

[TestClass]
public class xAI_Grok3_Tests : GrokServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Grok3;
    protected override AIModel? GetAlternativeModel() => AIModel.Grok3Mini;
}

[TestClass]
public class xAI_Grok3Mini_Tests : GrokServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Grok3Mini;
    protected override AIModel? GetAlternativeModel() => AIModel.Grok3;
}
