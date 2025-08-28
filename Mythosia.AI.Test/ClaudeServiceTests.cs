using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.Azure;

namespace Mythosia.AI.Tests;

[TestClass]
public class ClaudeServiceTests : AIServiceTestBase
{
    protected override AIService CreateAIService()
    {
        var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-antropic-secret");
        string apiKey = secretFetcher.GetKeyValueAsync().Result;

        var service = new ClaudeService(apiKey, new HttpClient());
        service.ActivateChat.ChangeModel(AIModel.ClaudeOpus4_1_250805);
        return service;
    }

    protected override bool SupportsMultimodal() => true;
    protected override bool SupportsFunctionCalling() => true;
    protected override bool SupportsAudio() => false;
    protected override bool SupportsImageGeneration() => false;
    protected override bool SupportsWebSearch() => false;
    protected override AIModel? GetAlternativeModel() => AIModel.Claude3_5Sonnet241022;

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