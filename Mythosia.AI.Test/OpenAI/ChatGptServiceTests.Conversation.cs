using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using System;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    public partial class ChatGptServiceTests
    {
        /// <summary>
        /// 대화 관리 기능 테스트
        /// </summary>
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
                AI.StartNewConversation(AIModel.Gpt4oLatest);
                var response = await AI.GetCompletionAsync("What number was I talking about?");
                Assert.IsFalse(response.Contains("42")); // 이전 대화를 기억하지 않아야 함
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Conversation Management Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}