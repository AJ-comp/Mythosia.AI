using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.Azure;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Mythosia.AI.Services.OpenAI;

namespace Mythosia.AI.Tests
{
    [TestClass]
    public partial class ChatGptServiceTests : AIServiceTestBase
    {
        // 1) AIServiceTestBase에서 요구하는 인스턴스 생성 로직
        protected override AIService CreateAIService()
        {
            // SecretFetcher에서 API Key 가져오기
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret");
            string openAiKey = secretFetcher.GetKeyValueAsync().Result;

            // ChatGptService 인스턴스 생성
            var service = new ChatGptService(openAiKey, new HttpClient());
            // GPT-4o는 기본적으로 Vision을 지원합니다
            service.ActivateChat.ChangeModel(AIModel.o3_mini); // Vision 지원 모델
            service.ActivateChat.SystemMessage = "You are a helpful assistant for testing purposes.";

            return service;
        }

        protected override bool SupportsMultimodal()
        {
            return true; // GPT-4 Vision 지원
        }

        protected override AIModel? GetAlternativeModel()
        {
            return AIModel.Gpt5;
        }
    }
}