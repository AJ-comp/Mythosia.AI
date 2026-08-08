using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.xAI;
using Mythosia.AI.Services;
using Mythosia.AI.Tests;

namespace Mythosia.AI.Tests.xAI;

// 1. Abstract base class
[TestClass]
[TestCategory("Live")]
[TestCategory("xAI")]
public abstract class XAIServiceTestsBase : AIServiceTestBase
{
    private static string? apiKey;
    protected abstract string ModelToTest { get; }

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassInit(TestContext context)
    {
        if (apiKey == null)
        {
            apiKey = await LiveTestSecrets.GetAsync("xai-secret");
            Console.WriteLine("[ClassInitialize] xAI API key loaded");
        }
    }

    protected override AIService CreateAIService()
    {
        var service = new XAIService(apiKey!, new HttpClient());
        service.ChangeModel(ModelToTest);
        Console.WriteLine($"[Testing Model] {ModelToTest}");
        return service;
    }

    protected override bool SupportsMultimodal() =>
        ModelToTest == AIModels.xAI.Grok4_5 ||
        ModelToTest == AIModels.xAI.Grok4_3 ||
        ModelToTest.Contains("grok-4.20") ||
        ModelToTest == AIModels.xAI.GrokBuild0_1;
    protected override bool SupportsFunctionCalling() => true;
    protected override bool SupportsArrayParameter() => true;
    protected override bool SupportsAudio() => false;
    protected override bool SupportsWebSearch() => false;
    protected override string? GetAlternativeModel() => AIModels.xAI.Grok4_5;

    protected override bool SupportsReasoning()
    {
        if (AI is not XAIService grokService)
            return false;

        var model = grokService.Model ?? string.Empty;
        return model.Equals(AIModels.xAI.Grok4_5, StringComparison.OrdinalIgnoreCase) ||
               model.Equals(AIModels.xAI.Grok4_3, StringComparison.OrdinalIgnoreCase) ||
               model.Equals(AIModels.xAI.Grok4_20Reasoning, StringComparison.OrdinalIgnoreCase) ||
               model.Equals(AIModels.xAI.GrokBuild0_1, StringComparison.OrdinalIgnoreCase);
    }

    protected override void SetupReasoningEffort()
    {
        if (AI is XAIService grokService &&
            (grokService.Model.Equals(AIModels.xAI.Grok4_5, StringComparison.OrdinalIgnoreCase) ||
             grokService.Model.Equals(AIModels.xAI.Grok4_3, StringComparison.OrdinalIgnoreCase)))
        {
            grokService.ReasoningEffort = GrokReasoning.High;
        }
    }

    protected override void ConfigureFunctionCallingStreamEventsTest()
    {
        ConfigureRequiredFunctionCall("test_function");
    }

    protected override void ConfigureRequiredFunctionCall(string functionName)
    {
        AI.ForceFunctionName = functionName;
    }

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
            var grokService = (XAIService)AI;

            // Grok 4.5 flagship 모델로 전환
            grokService.UseGrok4Model();
            Assert.AreEqual(AIModels.xAI.Grok4_5, grokService.Model);

            var g4Response = await grokService.GetCompletionAsync(
                "Explain quantum computing in one sentence."
            );
            Assert.IsNotNull(g4Response);
            Console.WriteLine($"[Grok 4.5] {g4Response}");

            // Fast helper maps to the lower-latency Grok 4.3 model.
            grokService.UseGrok4FastModel();
            Assert.AreEqual(AIModels.xAI.Grok4_3, grokService.Model);

            var fastResponse = await grokService.GetCompletionAsync(
                "What is the speed of light?"
            );
            Assert.IsNotNull(fastResponse);
            Console.WriteLine($"[Grok 4.3 via Fast helper] {fastResponse}");
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
            var grokService = (XAIService)AI;

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
            var grokService = (XAIService)AI;

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
    public void GrokImageGenerationNotSupportedTest()
    {
        Assert.IsFalse(AI is IImageGenerationService);
    }

    /// <summary>
    /// Grok 에러 처리 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task GrokErrorHandlingTest()
    {
        var assistantMessagesBefore = AI.ActivateChat.Messages.Count(
            message => message.Role == ActorRole.Assistant);

        try
        {
            // 매우 긴 입력으로 토큰 제한 테스트
            var longPrompt = new string('a', 10000);
            AI.MaxTokens = 10; // 매우 작은 출력 제한

            await AI.GetCompletionAsync(longPrompt);
            Assert.Fail("Expected xAI to stop at the configured output-token limit.");
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
        catch (AIServiceException ex)
        {
            StringAssert.Contains(ex.Message, "finish_reason=length");
            StringAssert.Contains(ex.Message, "partial response was not saved");
            Assert.AreEqual(
                assistantMessagesBefore,
                AI.ActivateChat.Messages.Count(message => message.Role == ActorRole.Assistant),
                "A length-truncated response must not be saved as an assistant message.");
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
public class xAI_Grok4_5_Tests : XAIServiceTestsBase
{
    protected override string ModelToTest => AIModels.xAI.Grok4_5;
}

[TestClass]
public class xAI_Grok4_3_Tests : XAIServiceTestsBase
{
    protected override string ModelToTest => AIModels.xAI.Grok4_3;
}

[TestClass]
public class xAI_Grok4_20Reasoning_Tests : XAIServiceTestsBase
{
    protected override string ModelToTest => AIModels.xAI.Grok4_20Reasoning;
}

[TestClass]
public class xAI_Grok4_20NonReasoning_Tests : XAIServiceTestsBase
{
    protected override string ModelToTest => AIModels.xAI.Grok4_20NonReasoning;
}

[TestClass]
public class xAI_GrokBuild0_1_Tests : XAIServiceTestsBase
{
    protected override string ModelToTest => AIModels.xAI.GrokBuild0_1;
}
