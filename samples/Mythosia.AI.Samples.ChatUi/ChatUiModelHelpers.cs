using Mythosia.AI.Models.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Mythosia.AI.Samples.ChatUi
{
    internal static class ChatUiModelHelpers
    {
        public static List<object> BuildModelCatalogue()
        {
            var groups = new Dictionary<string, List<object>>();

            foreach (AIModel model in Enum.GetValues(typeof(AIModel)))
            {
                var provider = GetProviderForModel(model);
                var description = model.GetType()
                    .GetField(model.ToString())!
                    .GetCustomAttribute<DescriptionAttribute>()?.Description ?? model.ToString();

                if (!groups.ContainsKey(provider))
                    groups[provider] = new List<object>();

                var reasoning = GetReasoningLevels(model);
                var maxOutputTokens = GetDefaultMaxOutputTokens(model);
                groups[provider].Add(new { name = model.ToString(), description, reasoning, maxOutputTokens });
            }

            return groups.Select(g => (object)new { provider = g.Key, models = g.Value }).ToList();
        }

        public static object? GetReasoningLevels(AIModel model)
        {
            var name = model.ToString();
            // OpenAI GPT-5
            if (name.StartsWith("Gpt5") &&
                !name.StartsWith("Gpt5_1") &&
                !name.StartsWith("Gpt5_2") &&
                !name.StartsWith("Gpt5_3") &&
                !name.StartsWith("Gpt5_4"))
                return new { type = "gpt5", levels = new[] { "Auto", "Minimal", "Low", "Medium", "High" } };
            // OpenAI GPT-5.1
            if (name.StartsWith("Gpt5_1"))
                return new { type = "gpt5_1", levels = new[] { "Auto", "None", "Low", "Medium", "High" } };
            // OpenAI GPT-5.2
            if (name.StartsWith("Gpt5_2"))
                return new { type = "gpt5_2", levels = new[] { "Auto", "None", "Low", "Medium", "High", "XHigh" } };
            // OpenAI GPT-5.3
            if (name.StartsWith("Gpt5_3"))
                return new { type = "gpt5_3", levels = new[] { "Auto", "None", "Low", "Medium", "High", "XHigh" } };
            // OpenAI GPT-5.4
            if (name.StartsWith("Gpt5_4"))
                return new { type = "gpt5_4", levels = new[] { "Auto", "None", "Low", "Medium", "High", "XHigh" } };
            // Gemini 3
            if (name.StartsWith("Gemini3Pro"))
                return new { type = "gemini3", levels = new[] { "Auto", "Low", "High" } };
            if (name.StartsWith("Gemini3Flash"))
                return new { type = "gemini3", levels = new[] { "Auto", "Minimal", "Low", "Medium", "High" } };
            // Gemini 2.5
            if (name.StartsWith("Gemini2_5"))
                return new { type = "gemini25", levels = new[] { "128", "1024", "4096", "8192", "16384" } };
            // OpenAI o3
            if (name.StartsWith("o3") || name.StartsWith("O3"))
                return new { type = "o3", levels = new[] { "Low", "Medium", "High" } };
            // Claude (extended thinking)
            if (name.StartsWith("Claude"))
            {
                // Sonnet 4+, Opus 4+, Haiku 4.5+
                if (name.Contains("Sonnet4") || name.Contains("Opus4") || name.Contains("Haiku4_5"))
                    return new { type = "claude", levels = new[] { "1024", "2048", "4096", "8192", "16384" } };
            }
            // xAI Grok reasoning models
            if (name.StartsWith("Grok"))
            {
                // grok-3-mini: supports reasoning_effort (Low/High), returns reasoning_content
                if (name.Contains("Grok3Mini"))
                    return new { type = "grok", levels = new[] { "Low", "High" } };
                // grok-4, grok-4-1-fast: always reasoning, no controllable parameters, no visible reasoning
                if (name.Contains("Grok4"))
                    return new { type = "grok_always", levels = Array.Empty<string>() };
            }
            return null;
        }

        public static string GetProviderForModel(AIModel model)
        {
            var name = model.ToString();
            if (name.StartsWith("Claude")) return "Anthropic";
            if (name.StartsWith("Gpt") || name.StartsWith("GPT") || name.StartsWith("o3")) return "OpenAI";
            if (name.StartsWith("Grok")) return "xAI";
            if (name.StartsWith("Gemini")) return "Google";
            if (name.StartsWith("DeepSeek")) return "DeepSeek";
            if (name.StartsWith("Perplexity")) return "Perplexity";
            return "Unknown";
        }

        public static uint GetDefaultMaxOutputTokens(AIModel model)
        {
            var desc = model.GetType()
                .GetField(model.ToString())!
                .GetCustomAttribute<DescriptionAttribute>()?.Description?.ToLower() ?? "";

            var provider = GetProviderForModel(model);
            return provider switch
            {
                "OpenAI" => desc switch
                {
                    _ when desc.StartsWith("o3") => 100000,
                    _ when desc.StartsWith("gpt-5") && desc.Contains("chat") => 16384,
                    _ when desc.StartsWith("gpt-5") => 128000,
                    _ when desc.StartsWith("gpt-4.1") => 32768,
                    _ when desc.Contains("4o-mini") => 16384,
                    _ when desc.Contains("4o") => 16384,
                    _ when desc.Contains("vision") => 4096,
                    _ => 16384
                },
                "Anthropic" => desc switch
                {
                    _ when desc.Contains("opus-4-6") => 128000,
                    _ when desc.Contains("sonnet-4-6") => 65536,
                    _ when desc.Contains("opus-4-5") => 65536,
                    _ when desc.Contains("sonnet-4-5") => 65536,
                    _ when desc.Contains("haiku-4-5") => 65536,
                    _ when desc.Contains("opus-4") => 32768,
                    _ when desc.Contains("sonnet-4") => 16384,
                    _ when desc.Contains("haiku-4") => 8192,
                    _ => 8192
                },
                "Google" => 65536,
                "xAI" => 131072,
                "DeepSeek" => 8192,
                "Perplexity" => 8192,
                _ => 4096
            };
        }
    }
}
