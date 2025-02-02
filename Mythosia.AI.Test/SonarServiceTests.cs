using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.Azure;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    [TestClass]
    public class SonarServiceTests : AIServiceTestBase
    {
        // 1) 공통 테스트에서 사용할 SonarService 인스턴스 생성
        protected override AIService CreateAIService()
        {
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "sonar-secret2");
            string sonarKey = secretFetcher.GetKeyValueAsync().Result; // 테스트이므로 동기 호출
            var service = new SonarService(sonarKey, new HttpClient());

            // 필요 시 초기 설정
            service.ActivateChat.SystemMessage = "반말로 말해주세요";
            return service;
        }

        // 2) SonarService만의 특별 기능 테스트 (ex. WebSearch)
        [TestMethod]
        public async Task WebSearchTest()
        {
            try
            {
                // SonarService 인스턴스
                var sonar = (SonarService)AI;

                // 모델 변경
                sonar.ActivateChat.ChangeModel(AIModel.PerplexitySonarPro);
                sonar.ActivateChat.SystemMessage = "웹페이지에서 반드시 오늘 날짜의 내용만 검색하세요...";

                // 질문
                string prompt = "오늘 나온 물리학 논문 중 하나 링크 보여주고 요약해줘";
                string response = await sonar.GetCompletionAsync(prompt);
                Console.WriteLine($"[WebSearchTest] {response}");

                // 스트리밍
                string response2 = string.Empty;
                await sonar.StreamCompletionAsync("오늘 나온 생물학 논문 링크도 알려줘", msg =>
                {
                    response2 += msg;
                });
                Console.WriteLine($"[WebSearch Stream] {response2}");

                // 토큰 카운트
                var tokenCount1 = await sonar.GetInputTokenCountAsync();
                var tokenCount2 = await sonar.GetInputTokenCountAsync();
                Console.WriteLine($"[Sonar Tokens] {tokenCount1}, {tokenCount2}");
            }
            catch (ArgumentException aex)
            {
                Console.WriteLine("모델 선택 오류: " + aex.Message);
                Assert.Fail();
            }
            catch (Exception ex)
            {
                Console.WriteLine("API 요청 오류: " + ex.Message);
                Assert.Fail();
            }
        }
    }
}
