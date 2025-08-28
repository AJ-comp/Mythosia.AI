using Mythosia.AI.Extensions;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// 멀티턴 대화 테스트
    /// </summary>
    [TestCategory("Common")]
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

    /// <summary>
    /// 대화 관리 기능 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Conversation")]
    [TestMethod]
    public async Task ConversationManagementTest()
    {
        try
        {
            // 대화 시작
            await AI.GetCompletionAsync("Remember the number 42");
            await AI.GetCompletionAsync("What number did I ask you to remember?");

            // 마지막 응답 가져오기
            var lastResponse = AI.GetLastAssistantResponse();
            Assert.IsNotNull(lastResponse);
            Assert.IsTrue(lastResponse.Contains("42"));
            Console.WriteLine($"[Last Response] {lastResponse}");

            // 대화 요약
            var summary = AI.GetConversationSummary();
            Assert.IsNotNull(summary);
            Console.WriteLine($"[Summary]\n{summary}");

            // 새 대화 시작
            AI.StartNewConversation();
            Assert.AreEqual(0, AI.ActivateChat.Messages.Count);

            // 새 모델로 새 대화
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

    /// <summary>
    /// 컨텍스트 관리 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Conversation")]
    [TestMethod]
    public async Task ContextManagementTest()
    {
        try
        {
            // 긴 컨텍스트 설정
            AI.ActivateChat.MaxMessageCount = 10;

            // 여러 대화 추가
            for (int i = 1; i <= 5; i++)
            {
                await AI.GetCompletionAsync($"Remember number {i}");
            }

            // 컨텍스트 확인
            var contextResponse = await AI.GetCompletionWithContextAsync(
                "What numbers did I mention?",
                contextMessages: 5
            );

            Assert.IsNotNull(contextResponse);
            Console.WriteLine($"[Context Response] {contextResponse}");

            // 대화 요약
            var summary = AI.ActivateChat.GetConversationSummary();
            Console.WriteLine($"[Conversation Summary]\n{summary}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Context Management Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
}