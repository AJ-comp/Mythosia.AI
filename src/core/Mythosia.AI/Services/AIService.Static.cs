using Mythosia.AI.Models;
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

        public static async Task<string> QuickAskAsync(string apiKey, string prompt, string model = AIModels.OpenAI.Gpt4oMini)
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
            string model = AIModels.OpenAI.Gpt4_1)
        {
            using var httpClient = new HttpClient();
            var service = CreateService(model, apiKey, httpClient);
            service.StatelessMode = true;
            return await service.GetCompletionWithImageAsync(prompt, imagePath);
        }

        internal static AIService CreateService(string model, string apiKey, HttpClient httpClient)
        {
            var provider = GetProviderFromModel(model);
            AIService service = provider switch
            {
                nameof(AIProvider.OpenAI) => new OpenAIService(apiKey, httpClient),
                nameof(AIProvider.Anthropic) => new AnthropicService(apiKey, httpClient),
                nameof(AIProvider.Google) => new GoogleAIService(apiKey, httpClient),
                nameof(AIProvider.DeepSeek) => new DeepSeekService(apiKey, httpClient),
                nameof(AIProvider.xAI) => new XAIService(apiKey, httpClient),
                nameof(AIProvider.Perplexity) => new PerplexityService(apiKey, httpClient),
                _ => throw new NotSupportedException($"Provider {provider} not supported")
            };
            service.ChangeModel(model);
            return service;
        }

        internal static string GetProviderFromModel(string model)
        {
            var modelName = model.ToString();
            if (modelName.StartsWith("claude", StringComparison.OrdinalIgnoreCase)) return nameof(AIProvider.Anthropic);
            if (modelName.StartsWith("gpt", StringComparison.OrdinalIgnoreCase) ||
                modelName.StartsWith("chatgpt", StringComparison.OrdinalIgnoreCase) ||
                modelName.StartsWith("o3", StringComparison.OrdinalIgnoreCase)) return nameof(AIProvider.OpenAI);
            if (modelName.StartsWith("grok", StringComparison.OrdinalIgnoreCase)) return nameof(AIProvider.xAI);
            if (modelName.StartsWith("gemini", StringComparison.OrdinalIgnoreCase)) return nameof(AIProvider.Google);
            if (modelName.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase)) return nameof(AIProvider.DeepSeek);
            if (modelName.StartsWith("sonar", StringComparison.OrdinalIgnoreCase) ||
                modelName.StartsWith("perplexity", StringComparison.OrdinalIgnoreCase)) return nameof(AIProvider.Perplexity);

            throw new ArgumentException($"Cannot determine provider for model {model}");
        }

        #endregion
    }
}
