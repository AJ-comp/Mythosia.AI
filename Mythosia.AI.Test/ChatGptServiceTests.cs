using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;
using Mythosia.Azure;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    [TestClass]
    public class ChatGptServiceTests : AIServiceTestBase
    {
        // 1) AIServiceTestBase에서 요구하는 인스턴스 생성 로직만 구현
        protected override AIService CreateAIService()
        {
            // SecretFetcher에서 API Key 가져오기
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret");
            string openAiKey = secretFetcher.GetKeyValueAsync().Result; // 동기 호출(테스트 시 용인)

            // ChatGptService 인스턴스 생성
            var service = new ChatGptService(openAiKey, new HttpClient());
            // 필요 시 기본 모델 지정
            service.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
            service.ActivateChat.SystemMessage = "귀여운 어투로 말해주세요";

            return service;
        }

        // 2) 필요하다면 ChatGptService만의 추가 테스트 작성
        [TestMethod]
        public async Task ImageGenerationTest()
        {
            try
            {
                var data = await AI.GenerateImageAsync("해변의 아름다운 풍경을 그려주세요");

                // base class의 AI 필드 이용
                var url = await AI.GenerateImageUrlAsync("해변의 아름다운 풍경을 그려주세요");
                Console.WriteLine("[Image URL] " + url);
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }

        [TestMethod]
        public async Task AdditionalChatGptTest()
        {
            try
            {
                // 특정 시나리오
                var result = await AI.GetCompletionAsync("안녕하세요 당신에 대해 소개해주세요");
                await AI.StreamCompletionAsync("당신의 취미에 대해 말해줘", msg => Console.WriteLine(msg));

                // 모델 변경
                AI.ActivateChat.ChangeModel(AIModel.Gpt4o240806);
                await AI.StreamCompletionAsync("어떤 취미를 더 좋아하나요?", msg => Console.WriteLine(msg));

                var tokenCount1 = await AI.GetInputTokenCountAsync();
                var tokenCount2 = await AI.GetInputTokenCountAsync();
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }
    }
}
