using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// 기본 텍스트 Completion 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Core")]
    [TestMethod]
    public async Task BasicCompletionTest()
    {
        try
        {
            AI.ActivateChat.SystemMessage = "응답을 짧고 간결하게 해줘.";

            // 1) 일반 Completion
            string prompt = "인공지능의 역사에 대해 한 문장으로 설명해줘.";
            string response = await AI.GetCompletionAsync(prompt);

            Assert.IsNotNull(response);
            Assert.IsTrue(response.Length > 0);
            Console.WriteLine($"[Completion] {response}");

            // 2) 스트리밍 Completion
            string prompt2 = "AI의 장점을 한 가지만 말해줘.";
            string streamedResponse = string.Empty;

            await AI.StreamCompletionAsync(prompt2, chunk =>
            {
                streamedResponse += chunk;
                Console.Write(chunk);
            });

            Assert.IsNotNull(streamedResponse);
            Assert.IsTrue(streamedResponse.Length > 0);
            Console.WriteLine($"\n[Stream Complete] Total length: {streamedResponse.Length}");

            // 3) 토큰 카운트
            uint tokenCountAll = await AI.GetInputTokenCountAsync();
            Console.WriteLine($"[Token Count - All] {tokenCountAll}");
            Assert.IsTrue(tokenCountAll > 0);

            uint tokenCountPrompt = await AI.GetInputTokenCountAsync("테스트 프롬프트");
            Console.WriteLine($"[Token Count - Prompt] {tokenCountPrompt}");
            Assert.IsTrue(tokenCountPrompt > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error in {GetType().Name}] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Stateless 모드 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Core")]
    [TestMethod]
    public async Task StatelessModeTest()
    {
        try
        {
            AI.StatelessMode = true;

            string response1 = await AI.GetCompletionAsync("내 이름은 테스터야.");
            Assert.IsNotNull(response1);
            Console.WriteLine($"[Stateless 1] {response1}");

            string response2 = await AI.GetCompletionAsync("내 이름이 뭐라고 했지?");
            Assert.IsNotNull(response2);
            Console.WriteLine($"[Stateless 2] {response2}");

            Assert.AreEqual(0, AI.ActivateChat.Messages.Count);

            AI.StatelessMode = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error in Stateless Test] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Extension 메서드 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Core")]
    [TestMethod]
    public async Task ExtensionMethodsTest()
    {
        try
        {
            // AskOnce
            string oneOffResponse = await AI.AskOnceAsync("1 더하기 1은?");
            Assert.IsNotNull(oneOffResponse);
            Console.WriteLine($"[AskOnce] {oneOffResponse}");
            Assert.AreEqual(0, AI.ActivateChat.Messages.Count);

            // Fluent API
            string fluentResponse = await AI
                .BeginMessage()
                .AddText("2 곱하기 3은?")
                .SendOnceAsync();

            Assert.IsNotNull(fluentResponse);
            Console.WriteLine($"[Fluent API] {fluentResponse}");

            // Configuration chaining
            AI.WithSystemMessage("You are a math tutor")
              .WithTemperature(0.5f)
              .WithMaxTokens(100);

            string configuredResponse = await AI.GetCompletionAsync("What is calculus?");
            Assert.IsNotNull(configuredResponse);
            Console.WriteLine($"[Configured] {configuredResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error in Extension Methods Test] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// MessageBuilder 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Core")]
    [TestMethod]
    public async Task MessageBuilderTest()
    {
        try
        {
            // 텍스트만 있는 메시지
            var textMessage = MessageBuilder.Create()
                .WithRole(ActorRole.User)
                .AddText("이것은 테스트 메시지입니다.")
                .Build();

            string response = await AI.GetCompletionAsync(textMessage);
            Assert.IsNotNull(response);
            Console.WriteLine($"[MessageBuilder Text] {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error in MessageBuilder Test] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
}