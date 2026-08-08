using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System.Collections.Generic;
using System.Text.Json;

namespace Mythosia.AI.Providers.Alibaba
{
    public partial class QwenService
    {
        #region Stream Chunk Parsing

        protected override OpenAIStreamChunk ParseStreamChunk(string jsonData, StreamOptions options)
        {
            var chunk = new OpenAIStreamChunk();

            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                chunk.Error = CreateCompatibleStreamError(
                    "Qwen streaming request failed.",
                    "provider_error",
                    ExtractErrorMessage(errorElement));
                return chunk;
            }

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

            if (!root.TryGetProperty("choices", out var choices))
                return chunk;

            if (choices.ValueKind != JsonValueKind.Array)
                throw new JsonException("The choices field must be an array.");
            if (choices.GetArrayLength() == 0)
                return chunk;

            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var finishReasonElement) &&
                finishReasonElement.ValueKind != JsonValueKind.Null)
            {
                if (finishReasonElement.ValueKind != JsonValueKind.String)
                    throw new JsonException("The finish_reason field must be a string or null.");

                var finishReason = finishReasonElement.GetString();
                chunk.FinishReason = finishReason;
                if (!IsSuccessfulFinishReason(finishReason))
                {
                    chunk.Error = CreateCompatibleStreamError(
                        "Qwen ended the stream before a complete tool response was available; no tools were executed.",
                        "unsafe_finish_reason",
                        finishReason ?? "missing");
                    chunk.Error.Metadata!["finish_reason"] = finishReason ?? "missing";
                    return chunk;
                }
            }

            if (!choice.TryGetProperty("delta", out var delta))
                return chunk;

            if (delta.TryGetProperty("content", out var contentElem) &&
                contentElem.ValueKind == JsonValueKind.String)
            {
                var text = contentElem.GetString();
                if (!string.IsNullOrEmpty(text))
                    chunk.Text = text;
            }

            if (delta.TryGetProperty("reasoning_content", out var reasoningElem) &&
                reasoningElem.ValueKind == JsonValueKind.String)
            {
                chunk.Reasoning = reasoningElem.GetString();
            }
            else if (delta.TryGetProperty("reasoning", out var ollamaReasoningElem) &&
                     ollamaReasoningElem.ValueKind == JsonValueKind.String)
            {
                chunk.Reasoning = ollamaReasoningElem.GetString();
            }

            if (delta.TryGetProperty("tool_calls", out var toolCalls) &&
                toolCalls.ValueKind != JsonValueKind.Null)
            {
                if (toolCalls.ValueKind != JsonValueKind.Array)
                    throw new JsonException("The tool_calls field must be an array.");

                var providerPosition = 0;
                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    if (toolCall.ValueKind != JsonValueKind.Object)
                        throw new JsonException("Every streamed tool call must be an object.");

                    chunk.FunctionCalls.Add(ParseFunctionCallDelta(
                        toolCall,
                        providerPosition,
                        requiresProviderCallId: true));

                    providerPosition++;
                }
            }

            // Older OpenAI-compatible endpoints can still stream one call through
            // delta.function_call instead of the indexed tool_calls array.
            if (chunk.FunctionCalls.Count == 0 &&
                delta.TryGetProperty("function_call", out var legacyFunctionCall) &&
                legacyFunctionCall.ValueKind == JsonValueKind.Object)
            {
                chunk.FunctionCalls.Add(ParseFunctionCallDelta(
                    legacyFunctionCall,
                    0,
                    requiresProviderCallId: false));
            }

            return chunk;
        }

        private static FunctionCall ParseFunctionCallDelta(
            JsonElement toolCall,
            int providerPosition,
            bool requiresProviderCallId)
        {
            var hasExplicitIndex = false;
            var functionCall = new FunctionCall
            {
                Index = providerPosition,
                Source = IdSource.OpenAI,
                Arguments = new Dictionary<string, object> { ["_missing"] = true },
                Metadata = new Dictionary<string, object>
                {
                    [StreamIndexExplicitMetadataKey] = false,
                    [RequiresProviderCallIdMetadataKey] = requiresProviderCallId
                }
            };

            if (toolCall.TryGetProperty("index", out var indexElem))
            {
                if (indexElem.ValueKind != JsonValueKind.Number ||
                    !indexElem.TryGetInt32(out var index))
                {
                    throw new JsonException("A streamed tool-call index must be an integer.");
                }

                functionCall.Index = index;
                hasExplicitIndex = true;
            }

            functionCall.Metadata![StreamIndexExplicitMetadataKey] = hasExplicitIndex;

            if (toolCall.TryGetProperty("id", out var idElem))
            {
                if (idElem.ValueKind != JsonValueKind.String)
                    throw new JsonException("A streamed tool-call ID must be a string.");
                functionCall.Id = idElem.GetString() ?? string.Empty;
            }

            // Standard tool_calls wrap the name and argument fragment in "function".
            // Legacy function_call exposes the same fields directly.
            var functionElement = toolCall;
            if (toolCall.TryGetProperty("function", out var nestedFunction))
            {
                if (nestedFunction.ValueKind != JsonValueKind.Object)
                    throw new JsonException("The streamed function payload must be an object.");
                functionElement = nestedFunction;
            }
            else if (requiresProviderCallId)
            {
                throw new JsonException("A streamed tool call is missing its function payload.");
            }

            if (functionElement.TryGetProperty("name", out var nameElem))
            {
                if (nameElem.ValueKind != JsonValueKind.String)
                    throw new JsonException("A streamed function name must be a string.");
                functionCall.Name = nameElem.GetString()
                    ?? throw new JsonException("A streamed function name cannot be null.");
            }

            if (functionElement.TryGetProperty("arguments", out var argsElem))
            {
                if (argsElem.ValueKind != JsonValueKind.String)
                    throw new JsonException("Streamed function arguments must be a string.");
                functionCall.Arguments = new Dictionary<string, object>
                {
                    ["_partial"] = argsElem.GetString() ?? string.Empty
                };
            }

            return functionCall;
        }

        protected override StreamingContent? CreateStreamParseFailure(
            string jsonData,
            System.Exception exception,
            StreamDiagnostics diagnostics)
        {
            return CreateCompatibleStreamError(
                "Qwen emitted a malformed streaming response; no tools were executed.",
                "malformed_stream",
                exception.Message);
        }

        protected override StreamingContent? ValidateStreamTermination(
            bool doneMarkerReceived,
            bool completionEventReceived,
            StreamDiagnostics diagnostics)
        {
            if (doneMarkerReceived)
                return null;

            return CreateCompatibleStreamError(
                "Qwen stream ended before the [DONE] marker; no tools were executed.",
                "incomplete_stream",
                $"lines={diagnostics.LinesRead}, data={diagnostics.DataLinesProcessed}");
        }

        private static bool IsSuccessfulFinishReason(string? finishReason)
        {
            return string.IsNullOrEmpty(finishReason) ||
                   finishReason == "stop" ||
                   finishReason == "tool_calls" ||
                   finishReason == "function_call";
        }

        private static string ExtractErrorMessage(JsonElement errorElement)
        {
            if (errorElement.ValueKind == JsonValueKind.Object &&
                errorElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? "Unknown provider error.";
            }

            return errorElement.ToString();
        }

        private static StreamingContent CreateCompatibleStreamError(
            string message,
            string status,
            string detail)
        {
            return new StreamingContent
            {
                Type = StreamingContentType.Error,
                Content = $"{message} {detail}",
                Metadata = new Dictionary<string, object>
                {
                    ["status"] = status,
                    ["detail"] = detail
                }
            };
        }

        #endregion
    }
}
