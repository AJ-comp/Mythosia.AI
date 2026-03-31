using Mythosia.AI.Models.Streaming;
using System.Collections.Generic;
using System.Text.Json;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
    {
        #region Response Parsing

        protected override string ExtractResponseContent(string responseContent)
            => _protocol.ExtractResponse(responseContent);

        protected override string StreamParseJson(string jsonData)
            => _protocol.ParseStreamChunk(jsonData);

        private StreamingContent? ParseDeepSeekStreamChunk(
            string jsonData,
            StreamOptions options,
            ref string? currentModel)
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            // Extract model on first chunk
            if (currentModel == null && root.TryGetProperty("model", out var modelElem))
            {
                currentModel = modelElem.GetString();
            }

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
                return null;

            var choice = choices[0];
            var content = new StreamingContent();

            // Check for finish reason
            if (choice.TryGetProperty("finish_reason", out var finishReason))
            {
                var reason = finishReason.GetString();
                if (reason != null && options.IncludeMetadata)
                {
                    content.Type = StreamingContentType.Status;
                    content.Metadata = new Dictionary<string, object>
                    {
                        ["finish_reason"] = reason
                    };
                    if (root.TryGetProperty("usage", out var usage))
                        content.Usage = ParseOpenAICompatibleUsage(usage);
                    return content;
                }
            }

            // Check for delta content
            if (choice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var textContent))
                {
                    var text = textContent.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        content.Type = StreamingContentType.Text;
                        content.Content = text;

                        if (options.IncludeMetadata)
                        {
                            content.Metadata = new Dictionary<string, object>();
                            if (currentModel != null)
                                content.Metadata["model"] = currentModel;
                        }

                        return content;
                    }
                }
            }

            return null;
        }

        private static TokenUsage ParseOpenAICompatibleUsage(JsonElement usage)
        {
            var tokenUsage = new TokenUsage();
            if (usage.TryGetProperty("prompt_tokens", out var prompt))
                tokenUsage.InputTokens = prompt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var completion))
                tokenUsage.OutputTokens = completion.GetInt32();
            if (usage.TryGetProperty("total_tokens", out var total))
                tokenUsage.TotalTokens = total.GetInt32();
            else
                tokenUsage.TotalTokens = tokenUsage.InputTokens + tokenUsage.OutputTokens;
            return tokenUsage;
        }

        #endregion
    }
}