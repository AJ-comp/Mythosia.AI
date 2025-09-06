using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using Mythosia.Azure;

namespace Mythosia.AI.Tests;

// 1. 기존 클래스를 추상 클래스로 변경 (이름에 Base 추가)
[TestClass]
public abstract class ChatGptServiceTestsBase : AIServiceTestBase
{
    private static string openAiKey;
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
        var service = new ChatGptService(openAiKey, new HttpClient());
        service.ActivateChat.ChangeModel(ModelToTest);  // 변경: 추상 속성 사용
        service.ActivateChat.SystemMessage = "You are a helpful assistant for testing purposes.";
        Console.WriteLine($"[Testing Model] {ModelToTest}");  // 추가: 어떤 모델 테스트 중인지 로그
        return service;
    }

    protected override bool SupportsMultimodal()
    {
        bool result = false;
        var curModel = AI.ActivateChat.Model;
        if (curModel == AIModel.Gpt4o.ToDescription() ||
            curModel == AIModel.Gpt4oMini.ToDescription() ||
            curModel == AIModel.Gpt4o241120.ToDescription() ||
            curModel == AIModel.Gpt4o240806.ToDescription() ||
            curModel == AIModel.Gpt5.ToDescription())
            result = true;
        return result;
    }

    protected override bool SupportsFunctionCalling() => true;
    protected override bool SupportsAudio() => true;
    protected override bool SupportsImageGeneration() => true;
    protected override bool SupportsWebSearch() => false;
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

// 2. 구체 클래스들 추가 (각 모델별로)

[TestClass]
public class OpenAI_ChatGpt4oMiniTests : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt4oMini;
}

[TestClass]
public class OpenAI_ChatGpt4oTests : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt4o;
}

[TestClass]
public class OpenAI_o3MiniTests : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.o3_mini;
}

[TestClass]
public class OpenAI_Gpt4o240806_Tests : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt4o240806;
}

[TestClass]
public class OpenAI_Gpt4o241120_Tests : ChatGptServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Gpt4o241120;
}