using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;
using Mythosia.Azure; // SecretFetcher 등
using System.Net.Http;

namespace Mythosia.AI.Tests
{
    [TestClass]
    public class GeminiServiceTests : AIServiceTestBase
    {
        protected override AIService CreateAIService()
        {
            // 1) Key Vault 등에서 ApiKey 가져오기
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "gemini-secret");
            string apiKey = secretFetcher.GetKeyValueAsync().Result; // 동기 호출(테스트용)

            // 2) 인스턴스 생성
            var service = new GeminiService(apiKey, new HttpClient());
            return service;
        }
    }
}
