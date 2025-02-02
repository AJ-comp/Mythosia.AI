using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.Azure;
using System.Net.Http;

namespace Mythosia.AI.Tests
{
    [TestClass]
    public class ClaudeServiceTests : AIServiceTestBase
    {
        protected override AIService CreateAIService()
        {
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-antropic-secret");
            string apiKey = secretFetcher.GetKeyValueAsync().Result;

            var service = new ClaudeService(apiKey, new HttpClient());
            return service;
        }
    }
}