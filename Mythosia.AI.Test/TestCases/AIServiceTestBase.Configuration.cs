using Mythosia.AI.Extensions;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// 모델 변경 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Configuration")]
    [TestMethod]
    public async Task ModelSwitchingTest()
    {
        try
        {
            var originalModel = AI.ActivateChat.Model;
            Console.WriteLine($"[Original Model] {originalModel}");

            var alternativeModel = GetAlternativeModel();
            if (alternativeModel != null)
            {
                AI.ActivateChat.ChangeModel(alternativeModel.Value);
                Console.WriteLine($"[Changed Model] {AI.ActivateChat.Model}");

                string response = await AI.GetCompletionAsync("What model are you?");
                Assert.IsNotNull(response);
                Console.WriteLine($"[Model Test Response] {response}");

                AI.ActivateChat.ChangeModel(originalModel);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error in Model Switching Test] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 설정 체이닝 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Configuration")]
    [TestMethod]
    public async Task ConfigurationChainingTest()
    {
        try
        {
            // 체이닝 설정
            AI.WithSystemMessage("You are a creative writer")
              .WithTemperature(0.9f)
              .WithMaxTokens(150);

            var creativeResponse = await AI.GetCompletionAsync(
                "Write a creative one-line story"
            );

            Assert.IsNotNull(creativeResponse);
            Console.WriteLine($"[Creative Response] {creativeResponse}");

            // 낮은 temperature로 변경
            AI.WithTemperature(0.1f)
              .WithSystemMessage("You are a precise calculator");

            var preciseResponse = await AI.GetCompletionAsync("What is 2 + 2?");
            Assert.IsNotNull(preciseResponse);
            Assert.IsTrue(preciseResponse.Contains("4"));
            Console.WriteLine($"[Precise Response] {preciseResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Configuration Test Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
}