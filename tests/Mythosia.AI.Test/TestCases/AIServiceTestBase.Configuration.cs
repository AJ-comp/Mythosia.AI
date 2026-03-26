using Mythosia.AI.Extensions;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// 설정 체이닝 테스트
    /// </summary>
    [TestCategory("Configuration")]
    [TestMethod]
    public async Task ConfigurationChainingTest()
    {
        try
        {
            // 체이닝 설정
            AI.WithSystemMessage("You are a creative writer")
              .WithTemperature(0.9f)
              .WithMaxTokens(2048);

            var creativeResponse = await AI.GetCompletionAsync(
                "Write a creative one-line story"
            );

            Assert.IsNotNull(creativeResponse);
            Assert.IsTrue(creativeResponse.Length > 0, "Creative response should not be empty");
            Console.WriteLine($"[Creative Response] {creativeResponse}");

            // 새 채팅으로 전환하여 이전 히스토리 간섭 방지
            AI.AddNewChat();

            // 낮은 temperature로 변경
            AI.WithTemperature(0.1f)
              .WithSystemMessage("You are a precise calculator. Always respond with the numeric answer only.");

            var preciseResponse = await AI.GetCompletionAsync("What is 2 + 2?");
            Assert.IsNotNull(preciseResponse);
            Assert.IsTrue(
                preciseResponse.Contains("4") || preciseResponse.Contains("four", StringComparison.OrdinalIgnoreCase),
                $"Expected response to contain '4' or 'four', but got: {preciseResponse}");
            Console.WriteLine($"[Precise Response] {preciseResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Configuration Test Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
}