using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using System.Collections.Generic;
using System.Text.Json;

namespace Mythosia.AI.Services.xAI
{
    public partial class GrokService
    {
        #region Stream Chunk Parsing

        protected override OpenAIStreamChunk ParseStreamChunk(string jsonData, StreamOptions options)
        {
            var chunk = new OpenAIStreamChunk();

            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            if (options.IncludeMetadata)
            {
                chunk.Metadata = new Dictionary<string, object>();
                if (root.TryGetProperty("model", out var m))
                {
                    chunk.Model = m.GetString();
                    chunk.Metadata["model"] = chunk.Model!;
                }
            }

            if (root.TryGetProperty("usage", out var usage))
                chunk.Usage = ParseOpenAICompatibleUsage(usage);

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
                return chunk;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
                return chunk;

            // Text content
            if (delta.TryGetProperty("content", out var contentElem) &&
                contentElem.ValueKind == JsonValueKind.String)
            {
                var text = contentElem.GetString();
                if (!string.IsNullOrEmpty(text))
                    chunk.Text = text;
            }

            // Reasoning content (xAI reasoning models: grok-3-mini, grok-4, grok-4-1-fast)
            if (delta.TryGetProperty("reasoning_content", out var reasoningElem) &&
                reasoningElem.ValueKind == JsonValueKind.String)
            {
                chunk.Reasoning = reasoningElem.GetString();
            }

            // Tool calls (OpenAI-compatible format)
            if (delta.TryGetProperty("tool_calls", out var toolCalls) &&
                toolCalls.ValueKind == JsonValueKind.Array &&
                toolCalls.GetArrayLength() > 0)
            {
                var tc = toolCalls[0];
                chunk.FunctionCall = new FunctionCall { Source = IdSource.OpenAI };

                if (tc.TryGetProperty("id", out var idElem))
                {
                    chunk.FunctionCall.Id = idElem.GetString();
                }

                if (tc.TryGetProperty("function", out var funcElem))
                {
                    if (funcElem.TryGetProperty("name", out var nameElem))
                    {
                        chunk.FunctionCall.Name = nameElem.GetString();
                    }

                    if (funcElem.TryGetProperty("arguments", out var argsElem))
                    {
                        var argsStr = argsElem.GetString();
                        if (!string.IsNullOrEmpty(argsStr))
                        {
                            chunk.FunctionCall.Arguments = new Dictionary<string, object>
                            {
                                ["_partial"] = argsStr
                            };
                        }
                    }
                }
            }

            return chunk;
        }

        #endregion
    }
}
