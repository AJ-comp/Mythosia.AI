using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using Mythosia.Azure;

namespace Mythosia.AI.Tests;

[TestClass]
public class ChatGptServiceTests : AIServiceTestBase
{
    protected override AIService CreateAIService()
    {
        var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret");
        string openAiKey = secretFetcher.GetKeyValueAsync().Result;

        var service = new ChatGptService(openAiKey, new HttpClient());
        service.ActivateChat.ChangeModel(AIModel.Gpt4o240806);
        service.ActivateChat.SystemMessage = "You are a helpful assistant for testing purposes.";

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