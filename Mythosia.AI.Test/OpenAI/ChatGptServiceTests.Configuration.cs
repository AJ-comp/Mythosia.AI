using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Extensions;
using System;
using System.Threading.Tasks;
using Mythosia.AI.Services.OpenAI;

namespace Mythosia.AI.Tests
{
    public partial class ChatGptServiceTests
    {
        /// <summary>
        /// 고급 설정 테스트
        /// </summary>
        [TestMethod]
        public async Task AdvancedConfigurationTest()
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
                var preciseResponse = await gptService.GetCompletionAsync(
                    "What is 2 + 2?"
                );

                Assert.IsNotNull(preciseResponse);
                Assert.IsTrue(preciseResponse.Contains("4"));
                Console.WriteLine($"[Precise Response] {preciseResponse}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Advanced Config Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}