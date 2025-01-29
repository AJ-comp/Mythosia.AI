using Mythosia.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    [TestClass()]
    public class SonarServiceTests
    {
        [TestMethod()]
        public async Task BasisTest()
        {
            try
            {
                SecretFetcher secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "sonar-secret2");
                var aiService = new SonarService(await secretFetcher.GetKeyValueAsync(), new HttpClient());
                aiService.ActivateChat.SystemMessage = "반말로 말해주세요";

                // 질문 준비
                string prompt = "안녕하세요, Sonar. 인공지능의 발전이 인류에게 미칠 수 있는 긍정적인 영향에 대해 설명해 주시겠습니까?";

                // 질의 및 응답 받기
                string response = await aiService.GetCompletionAsync(prompt);

                // 스트림 질의 및 응답 받기
                await aiService.StreamCompletionAsync("이번엔 부정적인 영향에 대해 설명해줘", (message) => { Console.WriteLine(message); });

                var tokenCount1 = await aiService.GetInputTokenCountAsync();
                var tokenCount2 = await aiService.GetInputTokenCountAsync();

                response = await aiService.GetCompletionAsync("지금까지 말한 의견을 종합했을 때 넌 어떨거 같아?");
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


        [TestMethod()]
        public async Task WebSearchTest()
        {
            try
            {
                SecretFetcher secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "sonar-secret2");
                var aiService = new SonarService(await secretFetcher.GetKeyValueAsync(), new HttpClient());
                aiService.ActivateChat.ChangeModel(AIModel.PerplexitySonarPro);
                aiService.ActivateChat.SystemMessage = "웹페이지에서 반드시 오늘 날짜의 내용만 검색하세요 검색결과가 없으면 없다고만 말하시고 다른 내용은 적지마세요";

                // 질문 준비
                string prompt = "오늘 나온 물리학 논문 흥미로운거 하나 링크 (페이지 주소) 보여주고 내용을 요약해줘";

                // 질의 및 응답 받기
                string response = await aiService.GetCompletionAsync(prompt);

                // 스트림 질의 및 응답 받기
                string response2 = string.Empty;
                await aiService.StreamCompletionAsync("오늘 나온 생물학 논문 흥미로운거 하나 링크 걸어주고 요약해줘", (message) => { response2 += message; });

                var tokenCount1 = await aiService.GetInputTokenCountAsync();
                var tokenCount2 = await aiService.GetInputTokenCountAsync();

                response = await aiService.GetCompletionAsync("두 논문 중 너는 어떤 논문이 더 흥미로워?");
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
