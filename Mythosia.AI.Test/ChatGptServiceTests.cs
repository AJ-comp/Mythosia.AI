using Mythosia.Azure;

namespace Mythosia.AI.Tests
{
    [TestClass()]
    public class ChatGptServiceTests
    {
        [TestMethod()]
        public async Task ChatGptServiceTest()
        {
            try
            {
                SecretFetcher secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret");
                var chatGptService = new ChatGptService(await secretFetcher.GetKeyValueAsync(), new HttpClient());

                var result = await chatGptService.GetCompletionAsync("안녕하세요 당신에 대해 소개해주세요");
                await chatGptService.StreamCompletionAsync("안녕하세요 당신에 대해 소개해주세요", (message) => { Console.WriteLine(message); });
            }
            catch (Exception ex)
            {
                Assert.Fail();
            }
        }
    }
}