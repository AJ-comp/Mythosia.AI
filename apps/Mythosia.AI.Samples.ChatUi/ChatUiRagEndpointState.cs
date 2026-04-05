using Mythosia.AI.Models;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Services.xAI;
using static Mythosia.AI.Samples.ChatUi.ChatUiModelHelpers;

namespace Mythosia.AI.Samples.ChatUi
{
    internal sealed class ChatUiRagEndpointState
    {
        public string? RewriterApiKey { get; set; }

        // ── Cached rewriter service (avoids creating HttpClient+AIService per query) ──
        private HttpClient? _rewriterHttpClient;
        private AIService? _cachedRewriterService;
        private string? _cachedRewriterModelKey;
        private string? _cachedRewriterApiKey;

        /// <summary>
        /// Returns a cached AIService for query rewriting, recreating only when
        /// the model or API key changes. Falls back to <paramref name="fallback"/>
        /// when no override is configured.
        /// </summary>
        public AIService GetOrCreateRewriterService(string? modelOverride, AIService fallback)
        {
            var overrideModel = FindModelValueByName(modelOverride) ?? modelOverride?.Trim();

            if (string.IsNullOrWhiteSpace(overrideModel)
                || string.IsNullOrWhiteSpace(RewriterApiKey))
            {
                return fallback;
            }

            var cacheKey = $"{overrideModel}:{RewriterApiKey}";
            if (_cachedRewriterService != null && _cachedRewriterModelKey == cacheKey)
                return _cachedRewriterService;

            _rewriterHttpClient?.Dispose();
            _rewriterHttpClient = new HttpClient();

            var provider = GetProviderForModel(overrideModel);
            _cachedRewriterService = provider switch
            {
                "OpenAI" => new OpenAIService(RewriterApiKey, _rewriterHttpClient),
                "Anthropic" => new AnthropicService(RewriterApiKey, _rewriterHttpClient),
                "Google" => new GoogleAIService(RewriterApiKey, _rewriterHttpClient),
                "DeepSeek" => new DeepSeekService(RewriterApiKey, _rewriterHttpClient),
                "xAI" => new XAIService(RewriterApiKey, _rewriterHttpClient),
                "Perplexity" => new PerplexityService(RewriterApiKey, _rewriterHttpClient),
                _ => fallback
            };
            _cachedRewriterService.ChangeModel(overrideModel);
            _cachedRewriterModelKey = cacheKey;
            _cachedRewriterApiKey = RewriterApiKey;

            return _cachedRewriterService;
        }
    }
}
