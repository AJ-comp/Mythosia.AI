using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.xAI;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Perplexity;
using System.Globalization;
using Mythosia.AI.Providers.Alibaba;

namespace Mythosia.AI.Samples.ChatUi
{
    internal static class ChatUiModelHelpers
    {
        private static readonly (string Provider, string Name, string Value)[] Catalogue =
        {
            ("OpenAI", nameof(AIModels.OpenAI.Gpt6Astra), AIModels.OpenAI.Gpt6Astra),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_6), AIModels.OpenAI.Gpt5_6),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_6Sol), AIModels.OpenAI.Gpt5_6Sol),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_6Terra), AIModels.OpenAI.Gpt5_6Terra),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_6Luna), AIModels.OpenAI.Gpt5_6Luna),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_1), AIModels.OpenAI.Gpt5_1),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_2), AIModels.OpenAI.Gpt5_2),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_2Pro), AIModels.OpenAI.Gpt5_2Pro),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_3Codex), AIModels.OpenAI.Gpt5_3Codex),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_4), AIModels.OpenAI.Gpt5_4),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_4Mini), AIModels.OpenAI.Gpt5_4Mini),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_4Nano), AIModels.OpenAI.Gpt5_4Nano),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_4Pro), AIModels.OpenAI.Gpt5_4Pro),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_5), AIModels.OpenAI.Gpt5_5),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_5Pro), AIModels.OpenAI.Gpt5_5Pro),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4_1), AIModels.OpenAI.Gpt4_1),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4_1Mini), AIModels.OpenAI.Gpt4_1Mini),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4o), AIModels.OpenAI.Gpt4o),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4o241120), AIModels.OpenAI.Gpt4o241120),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4o240806), AIModels.OpenAI.Gpt4o240806),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4oMini), AIModels.OpenAI.Gpt4oMini),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeFable5_1), AIModels.Anthropic.ClaudeFable5_1),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeMythos5_1), AIModels.Anthropic.ClaudeMythos5_1),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeFable5), AIModels.Anthropic.ClaudeFable5),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeMythos5), AIModels.Anthropic.ClaudeMythos5),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeOpus5), AIModels.Anthropic.ClaudeOpus5),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeSonnet5), AIModels.Anthropic.ClaudeSonnet5),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeOpus4_8), AIModels.Anthropic.ClaudeOpus4_8),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeOpus4_7), AIModels.Anthropic.ClaudeOpus4_7),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeOpus4_6), AIModels.Anthropic.ClaudeOpus4_6),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeSonnet4_6), AIModels.Anthropic.ClaudeSonnet4_6),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeOpus4_5_251101), AIModels.Anthropic.ClaudeOpus4_5_251101),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeSonnet4_5_250929), AIModels.Anthropic.ClaudeSonnet4_5_250929),
            ("Anthropic", nameof(AIModels.Anthropic.ClaudeHaiku4_5_251001), AIModels.Anthropic.ClaudeHaiku4_5_251001),
            ("Google", nameof(AIModels.Google.Gemini3_8Flash), AIModels.Google.Gemini3_8Flash),
            ("Google", nameof(AIModels.Google.Gemini3_7Flash), AIModels.Google.Gemini3_7Flash),
            ("Google", nameof(AIModels.Google.Gemini3_6Flash), AIModels.Google.Gemini3_6Flash),
            ("Google", nameof(AIModels.Google.Gemini3_5Flash), AIModels.Google.Gemini3_5Flash),
            ("Google", nameof(AIModels.Google.Gemini3_5FlashLite), AIModels.Google.Gemini3_5FlashLite),
            ("Google", nameof(AIModels.Google.Gemini3_1ProPreview), AIModels.Google.Gemini3_1ProPreview),
            ("Google", nameof(AIModels.Google.Gemini3_1FlashLite), AIModels.Google.Gemini3_1FlashLite),
            ("Google", nameof(AIModels.Google.Gemini3FlashPreview), AIModels.Google.Gemini3FlashPreview),
            ("Google", nameof(AIModels.Google.Gemini2_5Pro), AIModels.Google.Gemini2_5Pro),
            ("Google", nameof(AIModels.Google.Gemini2_5Flash), AIModels.Google.Gemini2_5Flash),
            ("Google", nameof(AIModels.Google.Gemini2_5FlashLite), AIModels.Google.Gemini2_5FlashLite),
            ("xAI", nameof(AIModels.xAI.Grok4_6), AIModels.xAI.Grok4_6),
            ("xAI", nameof(AIModels.xAI.Grok4_5), AIModels.xAI.Grok4_5),
            ("xAI", nameof(AIModels.xAI.Grok4_3), AIModels.xAI.Grok4_3),
            ("xAI", nameof(AIModels.xAI.Grok4_20Reasoning), AIModels.xAI.Grok4_20Reasoning),
            ("xAI", nameof(AIModels.xAI.Grok4_20NonReasoning), AIModels.xAI.Grok4_20NonReasoning),
            ("xAI", nameof(AIModels.xAI.GrokBuild0_1), AIModels.xAI.GrokBuild0_1),
            ("DeepSeek", nameof(AIModels.DeepSeek.Flash), AIModels.DeepSeek.Flash),
            ("Perplexity", nameof(AIModels.Perplexity.Sonar), AIModels.Perplexity.Sonar),
            ("Perplexity", "PerplexityGpt5_6Luna", AIModels.Perplexity.Gpt5_6Luna),
            ("Perplexity", "PerplexityGpt5_6Terra", AIModels.Perplexity.Gpt5_6Terra),
            ("Perplexity", "PerplexityGpt5_6Sol", AIModels.Perplexity.Gpt5_6Sol),
            ("Perplexity", "PerplexityClaudeFable5", AIModels.Perplexity.ClaudeFable5),
            ("Perplexity", "PerplexityClaudeOpus5", AIModels.Perplexity.ClaudeOpus5),
            ("Perplexity", "PerplexityClaudeSonnet5", AIModels.Perplexity.ClaudeSonnet5),
            ("Perplexity", "PerplexityGemini3_8Flash", AIModels.Perplexity.Gemini3_8Flash),
            ("Perplexity", "PerplexityGrok4_6", AIModels.Perplexity.Grok4_6),
            ("Perplexity", "PerplexityDeepSeekV4Flash0731", AIModels.Perplexity.DeepSeekV4Flash0731),
            ("Perplexity", "PerplexityKimiK3", AIModels.Perplexity.KimiK3),
            ("Alibaba", nameof(AlibabaModels.QwenMax), AlibabaModels.QwenMax),
            ("Alibaba", nameof(AlibabaModels.QwenPlus), AlibabaModels.QwenPlus),
            ("Alibaba", nameof(AlibabaModels.QwenTurbo), AlibabaModels.QwenTurbo),
            ("Alibaba", nameof(AlibabaModels.Qwen3_235B), AlibabaModels.Qwen3_235B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_30B), AlibabaModels.Qwen3_30B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_32B), AlibabaModels.Qwen3_32B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_14B), AlibabaModels.Qwen3_14B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_8B), AlibabaModels.Qwen3_8B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_4B), AlibabaModels.Qwen3_4B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_1_7B), AlibabaModels.Qwen3_1_7B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_0_6B), AlibabaModels.Qwen3_0_6B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_5_397B), AlibabaModels.Qwen3_5_397B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_5_122B), AlibabaModels.Qwen3_5_122B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_5_35B), AlibabaModels.Qwen3_5_35B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_5_27B), AlibabaModels.Qwen3_5_27B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_5_9B), AlibabaModels.Qwen3_5_9B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_5_4B), AlibabaModels.Qwen3_5_4B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_5_2B), AlibabaModels.Qwen3_5_2B),
            ("Alibaba", nameof(AlibabaModels.Qwen3_5_0_8B), AlibabaModels.Qwen3_5_0_8B),
        };

        public static List<object> BuildModelCatalogue()
        {
            var groups = new Dictionary<string, List<object>>();

            foreach (var entry in Catalogue)
            {
                var provider = entry.Provider;
                var description = string.Equals(
                    entry.Value,
                    AIModels.Anthropic.ClaudeMythos5,
                    StringComparison.OrdinalIgnoreCase) || string.Equals(
                    entry.Value,
                    AIModels.Anthropic.ClaudeMythos5_1,
                    StringComparison.OrdinalIgnoreCase)
                    ? $"{entry.Value} (Project Glasswing limited access)"
                    : entry.Value;

                if (!groups.ContainsKey(provider))
                    groups[provider] = new List<object>();

                var capabilities = GetModelCapabilities(entry.Value);
                var reasoning = GetReasoningLevels(entry.Value, capabilities);
                var maxOutputTokens = capabilities.MaxOutputTokens;
                var sampling = GetSamplingControls(capabilities);
                groups[provider].Add(new { name = entry.Name, description, reasoning, maxOutputTokens, sampling });
            }

            return groups.Select(g => (object)new { provider = g.Key, models = g.Value }).ToList();
        }

        public static string? FindModelValueByName(string? modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return null;

            var trimmed = modelName.Trim();
            var entry = Catalogue.FirstOrDefault(x =>
                string.Equals(x.Name, trimmed, StringComparison.Ordinal) ||
                string.Equals(x.Value, trimmed, StringComparison.OrdinalIgnoreCase));

            return string.IsNullOrEmpty(entry.Value) ? null : entry.Value;
        }

        // The catalogue inspects adapters offline. Each temporary service owns its client;
        // no key or account lookup is needed and no request is started.
        internal static AIModelCapabilities GetModelCapabilities(string model, bool deepSeekThinkingEnabled = false)
        {
            using var client = new HttpClient();
            AIService? service = GetProviderForModel(model) switch
            {
                "OpenAI" => new OpenAIService("capability-inspection", model, client),
                "Anthropic" => new AnthropicService("capability-inspection", model, client),
                "Google" => new GoogleAIService("capability-inspection", model, client),
                "xAI" => new XAIService("capability-inspection", model, client),
                "DeepSeek" => new DeepSeekService("capability-inspection", model, client)
                    { ThinkingEnabled = deepSeekThinkingEnabled },
                "Perplexity" => new PerplexityService("capability-inspection", model, client),
                "Alibaba" => new QwenService("capability-inspection", model, client),
                _ => null
            };
            return service?.GetCapabilities() ?? AIModelCapabilities.Unknown;
        }

        public static object? GetReasoningLevels(string model)
            => GetReasoningLevels(model, GetModelCapabilities(model));

        internal static object GetModelControls(AIService service)
        {
            var capabilities = service.GetCapabilities();
            return new
            {
                reasoning = GetReasoningLevels(capabilities.Model ?? service.Model, capabilities),
                sampling = GetSamplingControls(capabilities),
                maxOutputTokens = capabilities.MaxOutputTokens
            };
        }

        private static object? GetReasoningLevels(string model, AIModelCapabilities capabilities)
        {
            if (capabilities.NativeReasoning != CapabilitySupport.Supported)
                return null;

            var levels = capabilities.NativeReasoningLevels.Select(level => level.ToString()).ToArray();
            var budgets = capabilities.ThinkingBudgetPresets.Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray();
            // These identifiers choose existing UI/native-setting handlers. Support and choices
            // come from the provider, rather than another model capability table in this app.
            var provider = capabilities.Provider ?? GetProviderForModel(model);
            if (provider == "Anthropic")
            {
                if (budgets.Length > 0) return new { type = "claude", levels = budgets };
                return new
                {
                    type = capabilities.ThinkingToggle == CapabilitySupport.Supported ? "claude_adaptive" : "claude_always",
                    levels = levels.Where(level => level != "Auto" && level != "None").ToArray()
                };
            }
            if (provider == "Google")
                return budgets.Length > 0
                    ? new { type = "gemini25", levels = budgets }
                    : new { type = "gemini3", levels };
            if (provider == "xAI")
                return new { type = capabilities.ThinkingToggle == CapabilitySupport.Supported ? "grok" : "grok_always", levels };
            if (provider == "DeepSeek") return new { type = "deepseek_thinking", levels };
            if (provider == "Alibaba")
                return capabilities.ThinkingToggle == CapabilitySupport.Supported
                    ? new { type = "qwen_thinking", levels } : null;
            if (provider == "Perplexity") return new { type = "perplexity", levels };
            if (provider == "OpenAI")
            {
                var type = model.StartsWith("gpt-6", StringComparison.OrdinalIgnoreCase) ? "gpt6"
                    : model.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ? "o3" : "gpt5";
                for (var minor = 1; minor <= 6; minor++)
                    if (model.StartsWith($"gpt-5.{minor}", StringComparison.OrdinalIgnoreCase)) type = $"gpt5_{minor}";
                return new { type, levels };
            }
            return null;
        }

        public static object GetSamplingControls(string model, bool deepSeekThinkingEnabled = false)
            => GetSamplingControls(GetModelCapabilities(model, deepSeekThinkingEnabled));

        internal static object GetSamplingControls(AIModelCapabilities capabilities)
            => new
            {
                temperature = capabilities.Temperature == CapabilitySupport.Supported,
                topP = capabilities.TopP == CapabilitySupport.Supported,
                temperatureSupport = capabilities.Temperature.ToString(),
                topPSupport = capabilities.TopP.ToString()
            };

        internal static bool RequiresLowGeminiThinking(string model)
        {
            var levels = GetModelCapabilities(model).NativeReasoningLevels;
            return levels.Contains(ReasoningLevel.Low) && !levels.Contains(ReasoningLevel.Minimal);
        }

        public static string GetProviderForModel(string model)
        {
            var name = model.Trim();
            if (name.StartsWith("perplexity/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("openai/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("anthropic/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("google/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("xai/", StringComparison.OrdinalIgnoreCase)) return "Perplexity";
            if (name.StartsWith("claude", StringComparison.OrdinalIgnoreCase)) return "Anthropic";
            if (name.StartsWith("gpt", StringComparison.OrdinalIgnoreCase) || name.StartsWith("chatgpt", StringComparison.OrdinalIgnoreCase) || name.StartsWith("o3", StringComparison.OrdinalIgnoreCase)) return "OpenAI";
            if (name.StartsWith("grok", StringComparison.OrdinalIgnoreCase)) return "xAI";
            if (name.StartsWith("gemini", StringComparison.OrdinalIgnoreCase)) return "Google";
            if (name.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase)) return "DeepSeek";
            if (name.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)) return "Alibaba";
            return "Unknown";
        }

        public static uint GetDefaultMaxOutputTokens(string model)
            => GetModelCapabilities(model).MaxOutputTokens ?? 4096u;
    }
}
