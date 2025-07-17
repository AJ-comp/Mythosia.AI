using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Services.Base;
using System;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    /// <summary>
    /// AIService (추상 클래스)를 사용하는 공통 테스트 로직을 담는 베이스.
    /// </summary>
    public abstract class AIServiceTestBase
    {
        protected AIService AI { get; private set; }

        /// <summary>
        /// 각 구현체(Gemini, Claude 등)에서 인스턴스를 생성해 반환.
        /// </summary>
        /// <returns></returns>
        protected abstract AIService CreateAIService();

        [TestInitialize]
        public virtual void TestInitialize()
        {
            AI = CreateAIService();
        }

        /// <summary>
        /// (예) 기본적인 Completion 테스트
        /// </summary>
        [TestMethod]
        public async Task BasicCompletionTest()
        {
            try
            {
                // 시스템 메시지(옵션)
                AI.ActivateChat.SystemMessage = "응답을 짧고 간결하게 해줘.";

                // 1) 일반 Completion
                string prompt = "인공지능의 역사에 대해 간단히 이야기해줘.";
                string response = await AI.GetCompletionAsync(prompt);
                Console.WriteLine($"[Completion] {response}");

                // 2) 스트리밍 Completion
                string prompt2 = "이제 장점과 단점에 대해 설명해봐.";
                string streamedResponse = string.Empty;
                await AI.StreamCompletionAsync(prompt2, chunk =>
                {
                    streamedResponse += chunk;
                });
                Console.WriteLine($"[Stream] {streamedResponse}");

                // 3) 토큰 카운트 (대화 전체)
                uint tokenCountAll = await AI.GetInputTokenCountAsync();
                Console.WriteLine($"[Token Count - All] {tokenCountAll}");

                // 4) 토큰 카운트 (단일 프롬프트)
                uint tokenCountPrompt = await AI.GetInputTokenCountAsync("테스트 프롬프트 하나");
                Console.WriteLine($"[Token Count - Prompt] {tokenCountPrompt}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error in {GetType().Name}] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// (예) 멀티턴 대화 테스트 (원한다면 추가)
        /// </summary>
        [TestMethod]
        public async Task MultiTurnConversationTest()
        {
            try
            {
                AI.ActivateChat.SystemMessage = "당신은 친절한 대화 상대입니다.";

                // 첫 질의
                string prompt1 = "안녕? 오늘 기분이 어때?";
                string resp1 = await AI.GetCompletionAsync(prompt1);
                Console.WriteLine($"[Resp1] {resp1}");

                // 두 번째 질의
                string prompt2 = "그렇구나. 그럼 재미있는 농담 하나 해줄래?";
                string resp2 = await AI.GetCompletionAsync(prompt2);
                Console.WriteLine($"[Resp2] {resp2}");

                // 토큰 카운트
                uint tokens = await AI.GetInputTokenCountAsync();
                Console.WriteLine($"[Multi-turn token count] {tokens}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error in {GetType().Name}] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}
