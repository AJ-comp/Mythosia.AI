using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
    {
        protected override string ExtractResponseContent(string responseContent) => _protocol.ExtractResponse(responseContent);
        protected override string StreamParseJson(string jsonData) => _protocol.ParseStreamChunk(jsonData);

        private sealed class DeepSeekResponse
        {
            public string Text { get; set; } = string.Empty;
            public string? Reasoning { get; set; }
            public FunctionCallBatch Calls { get; set; } = new FunctionCallBatch();
            public TokenUsage? Usage { get; set; }
        }

        private DeepSeekResponse ParseDeepSeekResponse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.TryGetProperty("error", out var error))
                    throw new AIServiceException("DeepSeek returned a provider error.", error.GetRawText());
                if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                    throw new AIServiceException("DeepSeek returned no completion choices.");
                var choice = choices[0];
                var finish = ReadDeepSeekString(choice, "finish_reason");
                if (finish != "stop" && finish != "tool_calls")
                    throw new AIServiceException($"DeepSeek ended the response with finish_reason={finish ?? "missing"}; the incomplete response was not saved.");
                if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
                    throw new AIServiceException("DeepSeek returned no assistant message.");
                var extracted = ExtractFunctionCalls(json);
                return new DeepSeekResponse
                {
                    Text = extracted.functionCalls.Calls.Count == 0 ? ExtractResponseContent(json) : extracted.content,
                    Calls = extracted.functionCalls,
                    Reasoning = ReadDeepSeekString(message, "reasoning_content"),
                    Usage = root.TryGetProperty("usage", out var usage) ? ParseDeepSeekUsage(usage) : null
                };
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException)
            {
                throw new AIServiceException("Failed to parse the DeepSeek response.", exception);
            }
        }

        private static Dictionary<string, object>? CreateReasoningMetadata(string? reasoning)
            => reasoning == null ? null : new Dictionary<string, object> { [ReasoningMetadataKey] = reasoning };

        private static string? ReadDeepSeekString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.String) throw new JsonException($"DeepSeek {name} must be a string or null.");
            return value.GetString();
        }

        private static TokenUsage? ParseDeepSeekUsage(JsonElement usage)
        {
            if (usage.ValueKind == JsonValueKind.Null) return null;
            if (usage.ValueKind != JsonValueKind.Object) throw new JsonException("DeepSeek usage must be an object or null.");
            var input = ReadDeepSeekTokenCount(usage, "prompt_tokens");
            var output = ReadDeepSeekTokenCount(usage, "completion_tokens");
            var total = ReadDeepSeekTokenCount(usage, "total_tokens");
            if (!input.HasValue && !output.HasValue && !total.HasValue) return null;
            var cached = ReadDeepSeekTokenCount(usage, "prompt_cache_hit_tokens");
            if (!cached.HasValue && usage.TryGetProperty("prompt_tokens_details", out var inputDetails) && inputDetails.ValueKind == JsonValueKind.Object)
                cached = ReadDeepSeekTokenCount(inputDetails, "cached_tokens");
            var reasoning = usage.TryGetProperty("completion_tokens_details", out var outputDetails) && outputDetails.ValueKind == JsonValueKind.Object
                ? ReadDeepSeekTokenCount(outputDetails, "reasoning_tokens") : null;
            return new TokenUsage
            {
                InputTokens = input ?? 0, OutputTokens = output ?? 0,
                TotalTokens = total ?? (input ?? 0) + (output ?? 0),
                CachedInputTokens = cached ?? 0, ReasoningTokens = reasoning ?? 0
            };
        }

        private static int? ReadDeepSeekTokenCount(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var count) || count < 0)
                throw new JsonException($"DeepSeek {name} must be a nonnegative integer.");
            return count;
        }
    }
}
