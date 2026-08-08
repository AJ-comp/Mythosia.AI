using Mythosia.AI.Models;
using Mythosia.AI.Providers.Alibaba;

namespace Mythosia.AI.Samples.ChatUi
{
    internal static class ChatUiModelHelpers
    {
        private static readonly (string Provider, string Name, string Value)[] Catalogue =
        {
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_6), AIModels.OpenAI.Gpt5_6),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_6Sol), AIModels.OpenAI.Gpt5_6Sol),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_6Terra), AIModels.OpenAI.Gpt5_6Terra),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5_6Luna), AIModels.OpenAI.Gpt5_6Luna),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5), AIModels.OpenAI.Gpt5),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5Mini), AIModels.OpenAI.Gpt5Mini),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5Nano), AIModels.OpenAI.Gpt5Nano),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt5Pro), AIModels.OpenAI.Gpt5Pro),
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
            ("OpenAI", nameof(AIModels.OpenAI.O3Pro), AIModels.OpenAI.O3Pro),
            ("OpenAI", nameof(AIModels.OpenAI.O3), AIModels.OpenAI.O3),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4_1), AIModels.OpenAI.Gpt4_1),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4_1Mini), AIModels.OpenAI.Gpt4_1Mini),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4o), AIModels.OpenAI.Gpt4o),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4o241120), AIModels.OpenAI.Gpt4o241120),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4o240806), AIModels.OpenAI.Gpt4o240806),
            ("OpenAI", nameof(AIModels.OpenAI.Gpt4oMini), AIModels.OpenAI.Gpt4oMini),
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
            ("Google", nameof(AIModels.Google.Gemini3_6Flash), AIModels.Google.Gemini3_6Flash),
            ("Google", nameof(AIModels.Google.Gemini3_5Flash), AIModels.Google.Gemini3_5Flash),
            ("Google", nameof(AIModels.Google.Gemini3_5FlashLite), AIModels.Google.Gemini3_5FlashLite),
            ("Google", nameof(AIModels.Google.Gemini3_1ProPreview), AIModels.Google.Gemini3_1ProPreview),
            ("Google", nameof(AIModels.Google.Gemini3_1FlashLite), AIModels.Google.Gemini3_1FlashLite),
            ("Google", nameof(AIModels.Google.Gemini3FlashPreview), AIModels.Google.Gemini3FlashPreview),
            ("Google", nameof(AIModels.Google.Gemini2_5Pro), AIModels.Google.Gemini2_5Pro),
            ("Google", nameof(AIModels.Google.Gemini2_5Flash), AIModels.Google.Gemini2_5Flash),
            ("Google", nameof(AIModels.Google.Gemini2_5FlashLite), AIModels.Google.Gemini2_5FlashLite),
            ("xAI", nameof(AIModels.xAI.Grok4_5), AIModels.xAI.Grok4_5),
            ("xAI", nameof(AIModels.xAI.Grok4_3), AIModels.xAI.Grok4_3),
            ("xAI", nameof(AIModels.xAI.Grok4_20Reasoning), AIModels.xAI.Grok4_20Reasoning),
            ("xAI", nameof(AIModels.xAI.Grok4_20NonReasoning), AIModels.xAI.Grok4_20NonReasoning),
            ("xAI", nameof(AIModels.xAI.GrokBuild0_1), AIModels.xAI.GrokBuild0_1),
            ("DeepSeek", nameof(AIModels.DeepSeek.Chat), AIModels.DeepSeek.Chat),
            ("DeepSeek", nameof(AIModels.DeepSeek.Reasoner), AIModels.DeepSeek.Reasoner),
            ("Perplexity", nameof(AIModels.Perplexity.Sonar), AIModels.Perplexity.Sonar),
            ("Perplexity", nameof(AIModels.Perplexity.SonarPro), AIModels.Perplexity.SonarPro),
            ("Perplexity", nameof(AIModels.Perplexity.SonarReasoningPro), AIModels.Perplexity.SonarReasoningPro),
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
                    StringComparison.OrdinalIgnoreCase)
                    ? $"{entry.Value} (Project Glasswing limited access)"
                    : entry.Value;

                if (!groups.ContainsKey(provider))
                    groups[provider] = new List<object>();

                var reasoning = GetReasoningLevels(entry.Value);
                var maxOutputTokens = GetDefaultMaxOutputTokens(entry.Value);
                var sampling = GetSamplingControls(entry.Value);
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

        public static object? GetReasoningLevels(string model)
        {
            var name = model;
            // OpenAI GPT-5
            if (name.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("gpt-5.1", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("gpt-5.2", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("gpt-5.3", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("gpt-5.5", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase))
                return new { type = "gpt5", levels = new[] { "Auto", "Minimal", "Low", "Medium", "High" } };
            // OpenAI GPT-5.1
            if (name.StartsWith("gpt-5.1", StringComparison.OrdinalIgnoreCase))
                return new { type = "gpt5_1", levels = new[] { "Auto", "None", "Low", "Medium", "High" } };
            // OpenAI GPT-5.2
            if (name.StartsWith("gpt-5.2", StringComparison.OrdinalIgnoreCase))
                return new { type = "gpt5_2", levels = new[] { "Auto", "None", "Low", "Medium", "High", "XHigh" } };
            // OpenAI GPT-5.3
            if (name.StartsWith("gpt-5.3", StringComparison.OrdinalIgnoreCase))
                return new { type = "gpt5_3", levels = new[] { "Auto", "None", "Low", "Medium", "High", "XHigh" } };
            // OpenAI GPT-5.4
            if (name.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase))
                return new { type = "gpt5_4", levels = new[] { "Auto", "None", "Low", "Medium", "High", "XHigh" } };
            // OpenAI GPT-5.5
            if (name.StartsWith("gpt-5.5", StringComparison.OrdinalIgnoreCase))
                return new { type = "gpt5_5", levels = new[] { "Auto", "None", "Low", "Medium", "High", "XHigh" } };
            // OpenAI GPT-5.6
            if (name.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase))
                return new { type = "gpt5_6", levels = new[] { "Auto", "None", "Low", "Medium", "High", "XHigh", "Max" } };
            // Gemini 3 series (3, 3.1, 3.5, ...)
            if (name.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("-pro"))
                    return new { type = "gemini3", levels = new[] { "Auto", "Low", "Medium", "High" } };
                // flash / flash-lite
                return new { type = "gemini3", levels = new[] { "Auto", "Minimal", "Low", "Medium", "High" } };
            }
            // Gemini 2.5
            if (name.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("-pro", StringComparison.OrdinalIgnoreCase))
                    return new { type = "gemini25", levels = new[] { "-1", "128", "1024", "4096", "8192", "16384", "32768" } };

                return new { type = "gemini25", levels = new[] { "0", "-1", "512", "1024", "4096", "8192", "16384", "24576" } };
            }
            // OpenAI o3
            if (name.StartsWith("o3", StringComparison.OrdinalIgnoreCase))
                return new { type = "o3", levels = new[] { "Low", "Medium", "High" } };
            // Claude (extended thinking)
            if (name.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
            {
                var adaptiveLevels = new[] { "Low", "Medium", "High", "XHigh", "Max" };
                if (name.Contains("fable-5") || name.Contains("mythos-5"))
                    return new { type = "claude_always", levels = adaptiveLevels };
                if (name.Contains("opus-5") || name.Contains("sonnet-5") ||
                    name.Contains("opus-4-7") || name.Contains("opus-4-8"))
                    return new { type = "claude_adaptive", levels = adaptiveLevels };
                if (name.Contains("sonnet-4") || name.Contains("opus-4") || name.Contains("haiku-4-5"))
                    return new { type = "claude", levels = new[] { "1024", "2048", "4096", "8192", "16384" } };
            }
            // xAI Grok reasoning models
            if (name.StartsWith("grok", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Equals(AIModels.xAI.Grok4_5, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(AIModels.xAI.Grok4_5Latest, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(AIModels.xAI.GrokBuildLatest, StringComparison.OrdinalIgnoreCase))
                {
                    return new { type = "grok_always", levels = new[] { "Low", "Medium", "High" } };
                }

                if (name.Equals(AIModels.xAI.Grok4_3, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(AIModels.xAI.Grok4_3Latest, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(AIModels.xAI.GrokLatest, StringComparison.OrdinalIgnoreCase))
                {
                    return new { type = "grok", levels = new[] { "None", "Low", "Medium", "High" } };
                }

                if (name.Contains("grok-4.20-non-reasoning", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("grok-4.20-0309-non-reasoning", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (name.Contains("grok-4.20", StringComparison.OrdinalIgnoreCase))
                    return new { type = "grok_always", levels = Array.Empty<string>() };
            }
            // Alibaba Qwen3/3.5 thinking mode
            if (name.StartsWith("qwen3", StringComparison.OrdinalIgnoreCase))
                return new { type = "qwen_thinking", levels = Array.Empty<string>() };
            return null;
        }

        public static object GetSamplingControls(string model)
        {
            var name = model.Trim();
            if (name.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("gpt-4.1", StringComparison.OrdinalIgnoreCase))
                return new { temperature = false, topP = false };

            if (name.StartsWith(AIModels.Google.Gemini3_6Flash, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(AIModels.Google.Gemini3_5FlashLite, StringComparison.OrdinalIgnoreCase))
                return new { temperature = false, topP = false };

            if (!name.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
                return new { temperature = true, topP = true };

            var rejectsCustomSampling = name.Contains("fable-5") ||
                                        name.Contains("mythos-5") ||
                                        name.Contains("opus-5") ||
                                        name.Contains("sonnet-5") ||
                                        name.Contains("opus-4-7") ||
                                        name.Contains("opus-4-8");

            // AnthropicService currently exposes temperature for manual-thinking Claude models,
            // but does not serialize the generic TopP property into Messages requests.
            return new { temperature = !rejectsCustomSampling, topP = false };
        }

        public static string GetProviderForModel(string model)
        {
            var name = model.Trim();
            if (name.StartsWith("claude", StringComparison.OrdinalIgnoreCase)) return "Anthropic";
            if (name.StartsWith("gpt", StringComparison.OrdinalIgnoreCase) || name.StartsWith("chatgpt", StringComparison.OrdinalIgnoreCase) || name.StartsWith("o3", StringComparison.OrdinalIgnoreCase)) return "OpenAI";
            if (name.StartsWith("grok", StringComparison.OrdinalIgnoreCase)) return "xAI";
            if (name.StartsWith("gemini", StringComparison.OrdinalIgnoreCase)) return "Google";
            if (name.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase)) return "DeepSeek";
            if (name.StartsWith("sonar", StringComparison.OrdinalIgnoreCase)) return "Perplexity";
            if (name.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)) return "Alibaba";
            return "Unknown";
        }

        public static uint GetDefaultMaxOutputTokens(string model)
        {
            var desc = model.ToLowerInvariant();

            var provider = GetProviderForModel(model);
            return provider switch
            {
                "OpenAI" => desc switch
                {
                    _ when desc.StartsWith("o3") => 100000,
                    _ when desc == AIModels.OpenAI.Gpt5Pro => 272000,
                    _ when desc.StartsWith("gpt-5") => 128000,
                    _ when desc.StartsWith("gpt-4.1") => 32768,
                    _ when desc.Contains("4o-mini") => 16384,
                    _ when desc.Contains("4o") => 16384,
                    _ when desc.Contains("vision") => 4096,
                    _ => 16384
                },
                "Anthropic" => desc switch
                {
                    _ when desc.Contains("fable-5") => 128000,
                    _ when desc.Contains("mythos-5") => 128000,
                    _ when desc.Contains("opus-5") => 128000,
                    _ when desc.Contains("sonnet-5") => 128000,
                    _ when desc.Contains("opus-4-8") => 128000,
                    _ when desc.Contains("opus-4-7") => 128000,
                    _ when desc.Contains("opus-4-6") => 128000,
                    _ when desc.Contains("sonnet-4-6") => 128000,
                    _ when desc.Contains("opus-4-5") => 64000,
                    _ when desc.Contains("sonnet-4-5") => 64000,
                    _ when desc.Contains("haiku-4-5") => 64000,
                    _ when desc.Contains("opus-4") => 32768,
                    _ when desc.Contains("sonnet-4") => 16384,
                    _ when desc.Contains("haiku-4") => 8192,
                    _ => 8192
                },
                "Google" => 65536,
                "xAI" => 131072,
                "DeepSeek" => 8192,
                "Perplexity" => 8192,
                "Alibaba" => 8192,
                _ => 4096
            };
        }
    }
}
