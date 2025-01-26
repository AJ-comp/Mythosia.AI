using Mythosia.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mythosia.AI.Test
{
    [TestClass()]
    public class DeepSeekServiceTests
    {
        [TestMethod()]
        public async Task DeepSeekServiceTest()
        {
            try
            {
                SecretFetcher secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "deepseek-secret");
                var aiService = new DeepSeekService(await secretFetcher.GetKeyValueAsync(), new HttpClient());
                aiService.ActivateChat.SystemMessage = "반말로 말해주세요";

                // 질문 준비
                string prompt = "안녕하세요, DeepSeek. 인공지능의 발전이 인류에게 미칠 수 있는 긍정적인 영향에 대해 설명해 주시겠습니까?";

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
    }
}
