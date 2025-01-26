using Mythosia.Azure;

namespace Mythosia.AI.Tests
{
    [TestClass()]
    public class ClaudeServiceTests
    {
        [TestMethod()]
        public async Task ClaudeServiceTest()
        {
            try
            {
                SecretFetcher secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-antropic-secret");
                ClaudeService claudeService = new ClaudeService(await secretFetcher.GetKeyValueAsync(), new HttpClient());

                claudeService.ActivateChat.ChangeModel(AIModel.Claude3_5Haiku241022);
                claudeService.ActivateChat.SystemMessage = "반말로 말해주세요";

                // 질문 준비
                string prompt = "안녕하세요, Claude. 인공지능의 발전이 인류에게 미칠 수 있는 긍정적인 영향에 대해 설명해 주시겠습니까?";

                // Claude 3.5 Sonnet 모델을 사용하여 질의 및 응답 받기
                string response = await claudeService.GetCompletionAsync(prompt);
                await claudeService.StreamCompletionAsync("이번엔 부정적인 영향에 대해 설명해줘", (message) => { Console.WriteLine(message); });

                var tokenCount1 = await claudeService.GetInputTokenCountAsync();
                var tokenCount2 = await claudeService.GetInputTokenCountAsync();
                response = await claudeService.GetCompletionAsync("지금까지 말한 의견을 종합했을 때 넌 어떨거 같아?");
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