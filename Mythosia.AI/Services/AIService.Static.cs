using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Services.xAI;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        #region Static Quick Methods

        public static async Task<string> QuickAskAsync(string apiKey, string prompt, AIModel model = AIModel.Gpt4oMini)
        {
            using var httpClient = new HttpClient();
            var service = CreateService(model, apiKey, httpClient);
            service.StatelessMode = true;
            return await service.GetCompletionAsync(prompt);
        }

        public static async Task<string> QuickAskWithImageAsync(
            string apiKey,
            string prompt,
            string imagePath,
            AIModel model = AIModel.Gpt4Vision)
        {
            using var httpClient = new HttpClient();
            var service = CreateService(model, apiKey, httpClient);
            service.StatelessMode = true;
            return await service.GetCompletionWithImageAsync(prompt, imagePath);
        }

        private static AIService CreateService(AIModel model, string apiKey, HttpClient httpClient)
        {
            var provider = GetProviderFromModel(model);
            return provider switch
            {
                AIProvider.OpenAI => new ChatGptService(apiKey, httpClient),
                AIProvider.Anthropic => new ClaudeService(apiKey, httpClient),
                AIProvider.Google => new GeminiService(apiKey, httpClient),
                AIProvider.DeepSeek => new DeepSeekService(apiKey, httpClient),
                AIProvider.xAI => new GrokService(apiKey, httpClient),
                AIProvider.Perplexity => new SonarService(apiKey, httpClient),
                _ => throw new NotSupportedException($"Provider {provider} not supported")
            };
        }

        private static AIProvider GetProviderFromModel(AIModel model)
        {
            var modelName = model.ToString();
            if (modelName.StartsWith("Claude")) return AIProvider.Anthropic;
            if (modelName.StartsWith("Gpt") || modelName.StartsWith("GPT") || modelName.StartsWith("o3")) return AIProvider.OpenAI;
            if (modelName.StartsWith("Grok")) return AIProvider.xAI;
            if (modelName.StartsWith("Gemini")) return AIProvider.Google;
            if (modelName.StartsWith("DeepSeek")) return AIProvider.DeepSeek;
            if (modelName.StartsWith("Perplexity")) return AIProvider.Perplexity;

            throw new ArgumentException($"Cannot determine provider for model {model}");
        }

        #endregion
    }
}