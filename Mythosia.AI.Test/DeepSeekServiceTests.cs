using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.Azure;
using System.Net.Http;

namespace Mythosia.AI.Tests
{
    [TestClass]
    public class DeepSeekServiceTests : AIServiceTestBase
    {
        protected override AIService CreateAIService()
        {
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "deepseek-secret");
            string apiKey = secretFetcher.GetKeyValueAsync().Result;

            var service = new DeepSeekService(apiKey, new HttpClient());
            return service;
        }
    }
}
