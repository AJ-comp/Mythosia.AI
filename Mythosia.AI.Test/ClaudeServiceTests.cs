using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.Azure;

namespace Mythosia.AI.Tests;

// 1. 기존 클래스를 추상 클래스로 변경 (이름에 Base 추가)
[TestClass]
public abstract class ClaudeServiceTestsBase : AIServiceTestBase
{
    private static string apiKey;
    protected abstract AIModel ModelToTest { get; }  // 추가: 각 구체 클래스에서 모델 지정

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]  // 상속 동작 추가
    public static async Task ClassInit(TestContext context)
    {
        if (apiKey == null)  // 중복 호출 방지
        {
            var secretFetcher = new SecretFetcher(
                "https://mythosia-key-vault.vault.azure.net/",
                "momedit-antropic-secret"
            );
            apiKey = await secretFetcher.GetKeyValueAsync();
            Console.WriteLine("[ClassInitialize] Claude API key loaded");
        }
    }

    protected override AIService CreateAIService()
    {
        var service = new ClaudeService(apiKey, new HttpClient());
        service.ActivateChat.ChangeModel(ModelToTest);  // 변경: 추상 속성 사용
        Console.WriteLine($"[Testing Model] {ModelToTest}");  // 추가: 어떤 모델 테스트 중인지 로그
        return service;
    }

    protected override bool SupportsMultimodal() => true;
    protected override bool SupportsFunctionCalling() => true;
    protected override bool SupportsArrayParameter() => true;
    protected override bool SupportsAudio() => false;
    protected override bool SupportsImageGeneration() => false;
    protected override bool SupportsWebSearch() => false;
    protected override AIModel? GetAlternativeModel() => AIModel.ClaudeSonnet4_250514;

    #region Claude-Specific Tests

    /// <summary>
    /// Claude 특화 기능 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task ClaudeSpecificFeaturesTest()
    {
        try
        {
            var claudeService = (ClaudeService)AI;

            // Claude 파라미터 설정
            claudeService
                .WithClaudeParameters(topK: 10)
                .WithConstitutionalAI(true)
                .WithTemperaturePreset(TemperaturePreset.Analytical);

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
    /// Claude 모델 전환 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task ClaudeModelVariantsTest()
    {
        try
        {
            var models = new[]
            {
               AIModel.Claude3_5Haiku241022,
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
                    Console.WriteLine($"[Model {model} Error] {modelEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Model Variants Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Claude의 이미지 URL 다운로드 테스트
    /// </summary>
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task ClaudeImageUrlHandlingTest()
    {
        try
        {
            var claudeService = (ClaudeService)AI;

            // Claude는 URL을 직접 지원하지 않으므로 다운로드 필요
            var testImageUrl = "https://example.com/test.jpg";

            try
            {
                var message = await claudeService.CreateMessageWithImageUrl(
                    "Describe this image",
                    testImageUrl
                );
                Assert.Fail("Should have thrown exception for invalid URL");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Expected Error] {ex.Message}");
                Assert.IsTrue(ex.Message.Contains("download"));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Image URL Test Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    #endregion
}


[TestClass]
public class Claude_Opus4_1_Tests : ClaudeServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.ClaudeOpus4_1_250805;
}

[TestClass]
public class Claude_Opus4_Tests : ClaudeServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.ClaudeOpus4_250514;
}

[TestClass]
public class Claude_Sonnet4_Tests : ClaudeServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.ClaudeSonnet4_250514;
}

[TestClass]
public class Claude_3_7SonnetLatest_Tests : ClaudeServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Claude3_7SonnetLatest;
}

[TestClass]
public class Claude_3_5Haiku_Tests : ClaudeServiceTestsBase
{
    protected override AIModel ModelToTest => AIModel.Claude3_5Haiku241022;
}