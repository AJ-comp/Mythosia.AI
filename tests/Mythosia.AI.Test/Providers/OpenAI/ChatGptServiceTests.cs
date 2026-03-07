using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Tests;
using Mythosia.Azure;

namespace Mythosia.AI.Tests.OpenAI;

// 1. 기존 클래스를 추상 클래스로 변경 (이름에 Base 추가)
[TestClass]
public abstract class ChatGptServiceTestsBase : AIServiceTestBase
{
    private static string? openAiKey;
    protected abstract AIModel ModelToTest { get; }  // 추가: 각 구체 클래스에서 모델 지정

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]  // 상속 동작 추가
    public static async Task ClassInit(TestContext context)
    {
        if (openAiKey == null)  // 중복 호출 방지
        {
            var secretFetcher = new SecretFetcher(
                "https://mythosia-key-vault.vault.azure.net/",
                "momedit-openai-secret"
            );
            openAiKey = await secretFetcher.GetKeyValueAsync();
            Console.WriteLine("[ClassInitialize] OpenAI API key loaded");
        }
    }

    protected override AIService CreateAIService()
    {
        var service = new ChatGptService(openAiKey!, new HttpClient());
        service.ChangeModel(ModelToTest);  // 변경: 추상 속성 사용
        service.ActivateChat.SystemMessage = "You are a helpful assistant for testing purposes.";
        Console.WriteLine($"[Testing Model] {ModelToTest}");  // 추가: 어떤 모델 테스트 중인지 로그
        return service;
    }

    protected override bool SupportsMultimodal()
    {
        var curModel = AI.Model;
        // All GPT-5 variants support multimodal (text + image input)
        if (curModel.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase))
            return true;
        if (curModel == AIModel.Gpt4o.ToDescription() ||
            curModel == AIModel.Gpt4oMini.ToDescription() ||
            curModel == AIModel.Gpt4o241120.ToDescription() ||
            curModel == AIModel.Gpt4o240806.ToDescription())
            return true;
        return false;
    }

    protected override bool SupportsFunctionCalling() => true;
    protected override bool SupportsAudio() => true;
    protected override bool SupportsImageGeneration() => true;
    protected override bool SupportsWebSearch() => false;
    protected override bool SupportsReasoning()
    {
        var curModel = AI.Model;
        return curModel.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
    }
    protected override AIModel? GetAlternativeModel() => AIModel.Gpt4oMini;

    #region GPT-Specific Tests
    /// <summary>
    /// OpenAI 특화 파라미터 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task GptSpecificParametersTest()
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
            var preciseResponse = await gptService.GetCompletionAsync("What is 2 + 2?");
            Assert.IsNotNull(preciseResponse);
            Assert.IsTrue(preciseResponse.Contains("4"));
            Console.WriteLine($"[Precise Response] {preciseResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GPT Parameters Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
    #endregion
}

[TestClass]
public class Gpt4o : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt4o;
}

[TestClass]
public class Gpt4oMini : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt4oMini;
    protected override AIModel? GetAlternativeModel() => AIModel.Gpt4o;
}

[TestClass]
public class Gpt4o240806 : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt4o240806;
}

[TestClass]
public class Gpt4o241120 : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt4o241120;
}

[TestClass]
public class Gpt5 : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt5;
    protected override void SetupReasoningEffort() => ((ChatGptService)AI).WithGpt5Parameters(reasoningEffort: Gpt5Reasoning.Low);

    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task Gpt5_ReasoningEffort_CanBeConfigured()
    {
        try
        {
            var gptService = (ChatGptService)AI;

            gptService.WithGpt5Parameters(reasoningEffort: Gpt5Reasoning.Minimal);
            var quickResponse = await gptService.GetCompletionAsync("What is 2+2?");
            Assert.IsNotNull(quickResponse);
            Console.WriteLine($"[Minimal Effort] {quickResponse}");

            gptService.WithGpt5Parameters(reasoningEffort: Gpt5Reasoning.High);
            var detailedResponse = await gptService.GetCompletionAsync(
                "Explain briefly why the sky is blue in one sentence."
            );
            Assert.IsNotNull(detailedResponse);
            Console.WriteLine($"[High Effort] {detailedResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GPT-5 Reasoning Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task Gpt5_ChainingParameters_WorksCorrectly()
    {
        try
        {
            var gptService = (ChatGptService)AI;
            gptService
                .WithGpt5Parameters(reasoningEffort: Gpt5Reasoning.Low)
                .WithSystemMessage("You are a concise assistant. Answer in one word if possible.")
                .WithMaxTokens(100);

            var response = await gptService.GetCompletionAsync(
                "What color is the sun? Answer in one word."
            );
            Assert.IsNotNull(response);
            Assert.IsTrue(response.Length < 200, "Response should be concise");
            Console.WriteLine($"[Chained GPT-5 Response] {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GPT-5 Chaining Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
}

[TestClass]
public class Gpt5Mini : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt5Mini;
}

[TestClass]
public class Gpt5Nano : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt5Nano;
}

[TestClass]
public class Gpt5_1 : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt5_1;
}

[TestClass]
public class Gpt5_2 : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt5_2;
}

[TestClass]
public class Gpt5_2Pro : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt5_2Pro;
}

[TestClass]
public class Gpt5_2Codex : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt5_2Codex;
}

[TestClass]
public class Gpt5_3Codex : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt5_3Codex;
    protected override void SetupReasoningEffort() => ((ChatGptService)AI).WithGpt5_3Parameters(reasoningEffort: Gpt5_3Reasoning.Low);

    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task Gpt5_3Codex_ReasoningEffort_CanBeConfigured()
    {
        try
        {
            var gptService = (ChatGptService)AI;

            gptService.WithGpt5_3Parameters(reasoningEffort: Gpt5_3Reasoning.Medium);
            var mediumResponse = await gptService.GetCompletionAsync("What is 2+2?");
            Assert.IsNotNull(mediumResponse);
            Console.WriteLine($"[Medium Effort] {mediumResponse}");

            gptService.WithGpt5_3Parameters(reasoningEffort: Gpt5_3Reasoning.High);
            var highResponse = await gptService.GetCompletionAsync(
                "Explain briefly why the sky is blue in one sentence."
            );
            Assert.IsNotNull(highResponse);
            Console.WriteLine($"[High Effort] {highResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GPT-5.3 Reasoning Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
}

[TestClass]
public class Gpt5_4 : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt5_4;
    protected override void SetupReasoningEffort() => ((ChatGptService)AI).WithGpt5_4Parameters(reasoningEffort: Gpt5_4Reasoning.Low);
}

[TestClass]
public class Gpt5_4Pro : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt5_4Pro;
    protected override void SetupReasoningEffort() => ((ChatGptService)AI).WithGpt5_4Parameters(reasoningEffort: Gpt5_4Reasoning.Medium);
}

[TestClass]
public class O3 : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.o3;
}