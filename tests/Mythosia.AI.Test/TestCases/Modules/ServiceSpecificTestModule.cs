using Mythosia.AI.Extensions;
using Mythosia.AI.Services.OpenAI;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mythosia.AI.Tests.Modules;

/// <summary>
/// OpenAI 특화 파라미터 테스트.
/// 원본: ChatGptServiceTestsBase.GptSpecificParametersTest
/// </summary>
[TestClass]
public abstract class ServiceSpecificTestModule : TestModuleBase
{
    [TestCategory("ServiceSpecific")]
    [TestMethod]
    public async Task GptSpecificParametersTest()
    {
        try
        {
            var gptService = (ChatGptService)AI;
            gptService.WithOpenAIParameters(
                presencePenalty: 0.5f,
                frequencyPenalty: 0.3f
            );
            gptService
                .WithSystemMessage("You are a creative writer")
                .WithTemperature(0.9f)
                .WithMaxTokens(150);
            var creativeResponse = await gptService.GetCompletionAsync(
                "Write a creative one-line story about a robot"
            );
            Assert.IsNotNull(creativeResponse);
            Console.WriteLine($"[Creative Response] {creativeResponse}");

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
}
