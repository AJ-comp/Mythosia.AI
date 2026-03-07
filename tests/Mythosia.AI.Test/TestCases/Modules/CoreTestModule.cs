using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mythosia.AI.Tests.Modules;

/// <summary>
/// 기본 Completion, Stateless, Extension, MessageBuilder, Configuration, Conversation 테스트.
/// 원본: AIServiceTestBase.Basic.cs, .Configuration.cs, .Conversation.cs
/// </summary>
[TestClass]
public abstract class CoreTestModule : TestModuleBase
{
    #region Basic (from AIServiceTestBase.Basic.cs)

    [TestCategory("Core")]
    [TestMethod]
    public async Task BasicCompletionTest()
    {
        try
        {
            AI.ActivateChat.SystemMessage = "응답을 짧고 간결하게 해줘.";

            string prompt = "인공지능의 역사에 대해 한 문장으로 설명해줘.";
            string response = await AI.GetCompletionAsync(prompt);
            Assert.IsNotNull(response);
            Assert.IsTrue(response.Length > 0);
            Console.WriteLine($"[Completion] {response}");

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

    [TestCategory("Core")]
    [TestMethod]
    public async Task ExtensionMethodsTest()
    {
        try
        {
            string oneOffResponse = await AI.AskOnceAsync("1 더하기 1은?");
            Assert.IsNotNull(oneOffResponse);
            Console.WriteLine($"[AskOnce] {oneOffResponse}");
            Assert.AreEqual(0, AI.ActivateChat.Messages.Count);

            string fluentResponse = await AI
                .BeginMessage()
                .AddText("2 곱하기 3은?")
                .SendOnceAsync();
            Assert.IsNotNull(fluentResponse);
            Console.WriteLine($"[Fluent API] {fluentResponse}");

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

    [TestCategory("Core")]
    [TestMethod]
    public async Task MessageBuilderTest()
    {
        try
        {
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

    #endregion

    #region Configuration (from AIServiceTestBase.Configuration.cs)

    [TestCategory("Configuration")]
    [TestMethod]
    public async Task ConfigurationChainingTest()
    {
        try
        {
            AI.WithSystemMessage("You are a creative writer")
              .WithTemperature(0.9f)
              .WithMaxTokens(150);

            var creativeResponse = await AI.GetCompletionAsync(
                "Write a creative one-line story"
            );

            Assert.IsNotNull(creativeResponse);
            Console.WriteLine($"[Creative Response] {creativeResponse}");

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

    #endregion

    #region Conversation (from AIServiceTestBase.Conversation.cs)

    [TestCategory("Conversation")]
    [TestMethod]
    public async Task MultiTurnConversationTest()
    {
        try
        {
            AI.ActivateChat.SystemMessage = "당신은 친절한 대화 상대입니다.";

            string prompt1 = "안녕? 나는 테스트 중이야.";
            string resp1 = await AI.GetCompletionAsync(prompt1);
            Assert.IsNotNull(resp1);
            Console.WriteLine($"[Turn 1] User: {prompt1}");
            Console.WriteLine($"[Turn 1] AI: {resp1}");

            string prompt2 = "내가 뭘 하고 있다고 했지?";
            string resp2 = await AI.GetCompletionAsync(prompt2);
            Assert.IsNotNull(resp2);
            Console.WriteLine($"[Turn 2] User: {prompt2}");
            Console.WriteLine($"[Turn 2] AI: {resp2}");

            Assert.AreEqual(4, AI.ActivateChat.Messages.Count);

            uint tokens = await AI.GetInputTokenCountAsync();
            Console.WriteLine($"[Multi-turn token count] {tokens}");
            Assert.IsTrue(tokens > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error in Multi-turn Test] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    [TestCategory("Conversation")]
    [TestMethod]
    public async Task ConversationManagementTest()
    {
        try
        {
            await AI.GetCompletionAsync("Remember the number 42");
            await AI.GetCompletionAsync("What number did I ask you to remember?");

            var lastResponse = AI.GetLastAssistantResponse();
            Assert.IsNotNull(lastResponse);
            Assert.IsTrue(lastResponse.Contains("42"));
            Console.WriteLine($"[Last Response] {lastResponse}");

            var summary = AI.GetConversationSummary();
            Assert.IsNotNull(summary);
            Console.WriteLine($"[Summary]\n{summary}");

            AI.StartNewConversation();
            Assert.AreEqual(0, AI.ActivateChat.Messages.Count);

            var altModel = GetAlternativeModel();
            if (altModel != null)
            {
                AI.StartNewConversation(altModel.Value);
                var response = await AI.GetCompletionAsync("What number was I talking about?");
                Assert.IsFalse(response.Contains("42"));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error in Conversation Management] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    [TestCategory("Conversation")]
    [TestMethod]
    public async Task ContextManagementTest()
    {
        try
        {
            AI.MaxMessageCount = 10;

            for (int i = 1; i <= 5; i++)
            {
                await AI.GetCompletionAsync($"Remember number {i}");
            }

            var contextResponse = await AI.GetCompletionWithContextAsync(
                "What numbers did I mention?",
                contextMessages: 5
            );

            Assert.IsNotNull(contextResponse);
            Console.WriteLine($"[Context Response] {contextResponse}");

            var summary = AI.GetConversationSummary();
            Console.WriteLine($"[Conversation Summary]\n{summary}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Context Management Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    #endregion
}
