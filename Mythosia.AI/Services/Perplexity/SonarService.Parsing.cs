using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Mythosia.AI.Services.Perplexity
{
    public partial class SonarService
    {
        #region Response Parsing

        protected override string ExtractResponseContent(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                throw new AIServiceException("Failed to parse Perplexity response", ex);
            }
        }

        protected override string StreamParseJson(string jsonData)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private StreamingContent? ParseSonarStreamChunk(
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

        private SonarSearchResponse ParseSearchResponse(string responseContent)
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            var result = new SonarSearchResponse();

            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("content", out var content))
                    {
                        result.Content = content.GetString() ?? string.Empty;
                    }

                    if (message.TryGetProperty("citations", out var citations))
                    {
                        result.Citations = ParseCitations(citations);
                    }
                }
            }

            return result;
        }

        private List<Citation> ParseCitations(JsonElement citationsElement)
        {
            var citations = new List<Citation>();

            if (citationsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var citation in citationsElement.EnumerateArray())
                {
                    var cit = new Citation();

                    if (citation.TryGetProperty("url", out var url))
                        cit.Url = url.GetString() ?? string.Empty;

                    if (citation.TryGetProperty("title", out var title))
                        cit.Title = title.GetString() ?? string.Empty;

                    if (citation.TryGetProperty("snippet", out var snippet))
                        cit.Snippet = snippet.GetString() ?? string.Empty;

                    citations.Add(cit);
                }
            }

            return citations;
        }

        #endregion
    }
}