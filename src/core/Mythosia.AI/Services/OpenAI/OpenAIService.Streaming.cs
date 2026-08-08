using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService
    {
        private List<JsonElement>? _currentStreamResponseOutputItems;
        private readonly SortedDictionary<int, JsonElement> _currentStreamCompletedOutputItems =
            new SortedDictionary<int, JsonElement>();
        private readonly Dictionary<string, StringBuilder> _currentStreamReasoningParts =
            new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        private int _currentStreamNextOutputIndex;

        #region Stream Chunk Parsing

        protected override void OnStreamRoundStarting()
        {
            ResetCurrentStreamOutputItems();
        }

        protected override void OnStreamRoundFailed()
        {
            ResetCurrentStreamOutputItems();
        }

        protected override StreamingContent? CreateStreamParseFailure(
            string jsonData,
            Exception exception,
            StreamDiagnostics diagnostics)
        {
            return CreateResponsesStreamError(
                IsNewApiModel(Model)
                    ? "OpenAI Responses API emitted malformed streaming JSON; the partial response was not saved and no tools were executed."
                    : "OpenAI Chat Completions emitted malformed streaming JSON; the partial response was not saved and no tools were executed.",
                "malformed",
                exception.Message);
        }

        protected override StreamingContent? ValidateStreamTermination(
            bool doneMarkerReceived,
            bool completionEventReceived,
            StreamDiagnostics diagnostics)
        {
            if (!IsNewApiModel(Model))
            {
                if (doneMarkerReceived)
                    return null;

                return CreateResponsesStreamError(
                    "OpenAI Chat Completions stream ended before the [DONE] marker; the partial response was not saved and no tools were executed.",
                    "incomplete_stream",
                    $"lines={diagnostics.LinesRead}, data={diagnostics.DataLinesProcessed}, parse_failures={diagnostics.ParseFailures}");
            }

            if (completionEventReceived)
                return null;

            return CreateResponsesStreamError(
                "OpenAI Responses API stream ended before response.completed; the partial response was not saved and no tools were executed.",
                "incomplete_stream",
                $"lines={diagnostics.LinesRead}, data={diagnostics.DataLinesProcessed}, parse_failures={diagnostics.ParseFailures}");
        }

        protected override void EnrichStreamAssistantMessage(Message assistantMessage)
        {
            try
            {
                var functionCalls = assistantMessage.FunctionCallBatch;
                if (functionCalls == null)
                    return;

                var outputItems = _currentStreamResponseOutputItems;
                if ((outputItems == null || outputItems.Count == 0) &&
                    _currentStreamCompletedOutputItems.Count > 0)
                {
                    outputItems = new List<JsonElement>(_currentStreamCompletedOutputItems.Values);
                }

                if (outputItems != null && outputItems.Count > 0)
                {
                    functionCalls.Metadata ??= new Dictionary<string, object>();
                    functionCalls.Metadata[ResponsesOutputItemsMetadataKey] =
                        outputItems.Select(item => item.Clone()).ToList();
                }
            }
            finally
            {
                ResetCurrentStreamOutputItems();
            }
        }

        protected override OpenAIStreamChunk ParseStreamChunk(string jsonData, StreamOptions options)
        {
            var chunk = new OpenAIStreamChunk();

            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorElement) &&
                !root.TryGetProperty("type", out _))
            {
                chunk.Error = CreateResponsesStreamError(
                    "OpenAI streaming request failed; the partial response was not saved and no tools were executed.",
                    "provider_error",
                    errorElement.ToString());
                return chunk;
            }

            // Extract metadata if needed
            if (options.IncludeMetadata)
            {
                chunk.Metadata = new Dictionary<string, object>();
                if (root.TryGetProperty("model", out var m))
                {
                    chunk.Model = m.GetString();
                    if (chunk.Model != null)
                        chunk.Metadata["model"] = chunk.Model;
                }
                if (root.TryGetProperty("id", out var id))
                {
                    var responseId = id.GetString();
                    if (responseId != null)
                        chunk.Metadata["response_id"] = responseId;
                }
            }

            // New API format (o3, GPT-5, etc.)
            if (root.TryGetProperty("type", out var typeProp))
            {
                ParseNewApiStreamChunk(root, typeProp.GetString(), chunk);
            }
            // Legacy format (GPT-4o, etc.)
            else if (root.TryGetProperty("choices", out var choices))
            {
                ParseLegacyStreamChunk(choices, chunk);
            }

            // Legacy API sends usage in the final chunk (with empty choices) at root level
            if (root.TryGetProperty("usage", out var usage) &&
                usage.ValueKind == JsonValueKind.Object)
                chunk.Usage = ParseOpenAICompatibleUsage(usage);

            return chunk;
        }

        private void ParseNewApiStreamChunk(JsonElement root, string? type, OpenAIStreamChunk chunk)
        {
            switch (type)
            {
                // 텍스트 델타
                case "response.output_text.delta":
                    ParseStreamTextDelta(root, chunk);
                    break;

                // Function call events.
                case "response.function_call":
                case "response.function_call_arguments.delta":
                case "response.function_call_arguments.done":
                case "response.function_call.arguments.delta":  // legacy compat
                case "response.function_call.arguments.done":   // legacy compat
                    ParseStreamFunctionCallEvent(root, type, chunk);
                    break;

                // 출력 아이템 이벤트 (텍스트 또는 함수 호출 포함)
                case "response.output_item.added":
                case "response.output_item.delta":
                case "response.output_item.done":
                    ParseStreamOutputItemEvent(root, type, chunk);
                    break;

                // Reasoning summary events.
                case "response.reasoning_summary_text.delta":
                case "response.reasoning_summary_text.done":
                case "response.reasoning_summary_part.added":
                case "response.reasoning_summary_part.done":
                case "response.reasoning_text.delta":
                case "response.reasoning_text.done":
                    ParseStreamReasoningEvent(root, type, chunk);
                    break;

                // Response lifecycle events.
                case "response.created":
                    ParseStreamCreatedEvent(root, chunk);
                    break;

                // Only response.completed commits a Responses API stream.
                case "response.completed":
                    ParseStreamCompletionEvent(root, chunk);
                    break;

                case "response.failed":
                case "response.incomplete":
                case "error":
                    ParseStreamFailureEvent(root, type, chunk);
                    break;

                case "response.refusal.delta":
                case "response.refusal.done":
                    ParseStreamRefusalEvent(root, chunk);
                    break;
            }
        }

        /// <summary>
        /// response.output_text.delta 파싱
        /// </summary>
        private void ParseStreamTextDelta(JsonElement root, OpenAIStreamChunk chunk)
        {
            if (root.TryGetProperty("delta", out var delta))
            {
                chunk.Text = delta.ValueKind == JsonValueKind.String
                    ? delta.GetString()
                    : delta.TryGetProperty("text", out var t) ? t.GetString() : null;
            }
        }

        /// <summary>
        /// 함수 호출 관련 이벤트 파싱
        /// - response.function_call: 초기 함수 호출 정보
        /// - response.function_call_arguments.delta: 인자 스트리밍 델타
        /// - response.function_call_arguments.done: 인자 스트리밍 완료
        /// </summary>
        private void ParseStreamFunctionCallEvent(JsonElement root, string type, OpenAIStreamChunk chunk)
        {
            var functionCall = CreateStreamFunctionCall(root);

            if (type == "response.function_call")
            {
                if (root.TryGetProperty("function_call", out var fc))
                {
                    if (fc.TryGetProperty("name", out var n))
                        functionCall.Name = n.GetString() ?? string.Empty;
                    if (fc.TryGetProperty("call_id", out var callId))
                        functionCall.Id = callId.GetString() ?? string.Empty;
                    else if (fc.TryGetProperty("id", out var id))
                        functionCall.Id = id.GetString() ?? string.Empty;
                }
            }
            else if (type.Contains("done"))
            {
                // response.function_call_arguments.done — 완성된 인자 JSON
                if (root.TryGetProperty("arguments", out var argsComplete))
                {
                    var argsStr = argsComplete.GetString();
                    if (!string.IsNullOrEmpty(argsStr))
                    {
                        functionCall.Arguments = new Dictionary<string, object>
                        {
                            ["_complete"] = argsStr
                        };
                    }
                }
            }
            else
            {
                // response.function_call_arguments.delta — 인자 스트리밍 델타
                if (root.TryGetProperty("delta", out var argDelta))
                {
                    functionCall.Arguments = new Dictionary<string, object>
                    {
                        ["_partial"] = argDelta.GetString() ?? string.Empty
                    };
                }
            }

            chunk.FunctionCalls.Add(functionCall);
        }

        /// <summary>
        /// 출력 아이템 이벤트 파싱 (response.output_item.added, response.output_item.delta)
        /// </summary>
        private void ParseStreamOutputItemEvent(JsonElement root, string type, OpenAIStreamChunk chunk)
        {
            if (!root.TryGetProperty("item", out var item))
                return;

            if (type == "response.output_item.done")
            {
                CaptureCompletedStreamOutputItem(root, item);
            }

            var refusal = FindResponsesRefusal(item);
            if (refusal != null)
            {
                chunk.Error = CreateResponsesStreamError(
                    "OpenAI Responses API streamed a refusal; the partial response was not saved and no tools were executed.",
                    "refusal",
                    refusal);
                return;
            }

            // Function-call output item.
            if (item.TryGetProperty("type", out var itemType) &&
                itemType.GetString() == "function_call")
            {
                var functionCall = CreateStreamFunctionCall(root, item);

                if (item.TryGetProperty("name", out var fname))
                    functionCall.Name = fname.GetString() ?? string.Empty;
                if (item.TryGetProperty("call_id", out var cid))
                    functionCall.Id = cid.GetString() ?? string.Empty;
                if (item.TryGetProperty("arguments", out var args))
                {
                    functionCall.Arguments = new Dictionary<string, object>
                    {
                        [type == "response.output_item.done" ? "_complete" : "_partial"] =
                            args.GetString() ?? string.Empty
                    };
                }
                chunk.FunctionCalls.Add(functionCall);
                return;
            }

            // reasoning 타입 아이템에서 reasoning 요약/텍스트 추출
            if (item.TryGetProperty("type", out var reasoningItemType) &&
                (reasoningItemType.GetString() == "reasoning" || reasoningItemType.GetString() == "reasoning_summary"))
            {
                if (item.TryGetProperty("summary", out var summaryElem) && summaryElem.ValueKind == JsonValueKind.Array)
                {
                    var reasoningText = new StringBuilder();
                    var summaryIndex = 0;
                    foreach (var summaryItem in summaryElem.EnumerateArray())
                    {
                        if (summaryItem.TryGetProperty("text", out var summaryText) &&
                            summaryText.ValueKind == JsonValueKind.String)
                        {
                            reasoningText.Append(GetUnseenReasoningSnapshot(
                                GetReasoningPartKey(root, item, summaryIndex),
                                summaryText.GetString()));
                        }

                        summaryIndex++;
                    }

                    if (reasoningText.Length > 0)
                    {
                        chunk.Reasoning = reasoningText.ToString();
                        return;
                    }
                }

                if (item.TryGetProperty("text", out var itemText) && itemText.ValueKind == JsonValueKind.String)
                {
                    chunk.Reasoning = GetUnseenReasoningSnapshot(
                        GetReasoningPartKey(root, item),
                        itemText.GetString());
                    return;
                }
            }

            // message 타입 아이템에서 텍스트 추출
            if (item.TryGetProperty("message", out var messageObj) &&
                messageObj.TryGetProperty("content", out var content))
            {
                chunk.Text = ExtractTextFromContent(content);
            }
        }

        /// <summary>
        /// 추론 요약 이벤트 파싱 (response.reasoning_summary_text.delta, response.reasoning_summary_part.*)
        /// </summary>
        private void ParseStreamReasoningEvent(JsonElement root, string type, OpenAIStreamChunk chunk)
        {
            if ((type == "response.reasoning_summary_text.delta" ||
                 type == "response.reasoning_text.delta") &&
                root.TryGetProperty("delta", out var reasoningDelta))
            {
                var delta = reasoningDelta.ValueKind == JsonValueKind.String
                    ? reasoningDelta.GetString()
                    : reasoningDelta.TryGetProperty("text", out var deltaText) ? deltaText.GetString() : null;

                if (!string.IsNullOrEmpty(delta))
                {
                    AppendReasoningDelta(GetReasoningPartKey(root), delta);
                    chunk.Reasoning = delta;
                }

                return;
            }

            // done 이벤트에서 최종 텍스트가 제공되는 경우 처리
            if ((type == "response.reasoning_summary_text.done" ||
                 type == "response.reasoning_text.done") &&
                root.TryGetProperty("text", out var reasoningText) &&
                reasoningText.ValueKind == JsonValueKind.String)
            {
                chunk.Reasoning = GetUnseenReasoningSnapshot(
                    GetReasoningPartKey(root),
                    reasoningText.GetString());
                return;
            }

            // Some reasoning events carry a summary array snapshot.
            if (root.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                var summaryIndex = 0;
                foreach (var summaryItem in summary.EnumerateArray())
                {
                    if (summaryItem.TryGetProperty("text", out var sText) &&
                        sText.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(GetUnseenReasoningSnapshot(
                            GetReasoningPartKey(root, summaryIndex: summaryIndex),
                            sText.GetString()));
                    }

                    summaryIndex++;
                }

                if (sb.Length > 0)
                    chunk.Reasoning = sb.ToString();
            }

            // response.reasoning_summary_part.added / done 는 텍스트 필드가 없으면 무시
        }

        /// <summary>
        /// response.created 이벤트 파싱
        /// </summary>
        private void ParseStreamCreatedEvent(JsonElement root, OpenAIStreamChunk chunk)
        {
            if (root.TryGetProperty("response", out var createdResp))
            {
                if (createdResp.TryGetProperty("model", out var createdModel))
                    chunk.Model = createdModel.GetString();
                if (createdResp.TryGetProperty("id", out var createdId))
                {
                    chunk.Metadata ??= new Dictionary<string, object>();
                    var responseId = createdId.GetString();
                    if (responseId != null)
                        chunk.Metadata["response_id"] = responseId;
                }
            }
        }

        /// <summary>
        /// Parses the response.completed terminal event.
        /// </summary>
        private void ParseStreamCompletionEvent(JsonElement root, OpenAIStreamChunk chunk)
        {
            if (!root.TryGetProperty("response", out var doneResp) ||
                doneResp.ValueKind != JsonValueKind.Object)
            {
                chunk.Error = CreateResponsesStreamError(
                    "OpenAI Responses API emitted response.completed without a response object; no tools were executed.",
                    "malformed_terminal",
                    "missing response");
                return;
            }

            var status = doneResp.TryGetProperty("status", out var statusElement) &&
                         statusElement.ValueKind == JsonValueKind.String
                ? statusElement.GetString()
                : null;
            if (!string.Equals(status, "completed", StringComparison.Ordinal))
            {
                chunk.Error = CreateResponsesStreamError(
                    "OpenAI Responses API emitted response.completed without completed status; the partial response was not saved and no tools were executed.",
                    status ?? "missing",
                    ExtractResponsesFailureReason(doneResp));
                return;
            }

            var refusal = FindResponsesRefusal(doneResp);
            if (refusal != null)
            {
                chunk.Error = CreateResponsesStreamError(
                    "OpenAI Responses API completed with a refusal; the response was not saved and no tools were executed.",
                    "refusal",
                    refusal);
                return;
            }

            chunk.IsCompletion = true;
            chunk.Metadata ??= new Dictionary<string, object>();
            chunk.Metadata["finish_reason"] = "stop";
            if (doneResp.TryGetProperty("usage", out var usage) &&
                usage.ValueKind == JsonValueKind.Object)
                chunk.Usage = ParseOpenAICompatibleUsage(usage);
            if (doneResp.TryGetProperty("model", out var doneModel))
                chunk.Model = doneModel.GetString();
            if (doneResp.TryGetProperty("id", out var doneId))
            {
                var responseId = doneId.GetString();
                if (responseId != null)
                    chunk.Metadata["response_id"] = responseId;
            }

            if (doneResp.TryGetProperty("output", out var output) &&
                output.ValueKind == JsonValueKind.Array)
            {
                var outputItems = new List<JsonElement>();
                var outputIndex = 0;
                foreach (var item in output.EnumerateArray())
                {
                    outputItems.Add(item.Clone());

                    if (item.TryGetProperty("type", out var itemType) &&
                        itemType.GetString() == "function_call")
                    {
                        if (!item.TryGetProperty("call_id", out var callId) ||
                            callId.ValueKind != JsonValueKind.String ||
                            string.IsNullOrWhiteSpace(callId.GetString()))
                        {
                            chunk.Error = CreateResponsesStreamError(
                                "OpenAI Responses API completed a function call without a valid call_id; no tools were executed.",
                                "malformed_function_call",
                                $"output_index={outputIndex}");
                            return;
                        }
                        if (!item.TryGetProperty("name", out var functionName) ||
                            functionName.ValueKind != JsonValueKind.String ||
                            string.IsNullOrWhiteSpace(functionName.GetString()))
                        {
                            chunk.Error = CreateResponsesStreamError(
                                "OpenAI Responses API completed a function call without a valid name; no tools were executed.",
                                "malformed_function_call",
                                $"output_index={outputIndex}");
                            return;
                        }
                        if (!item.TryGetProperty("arguments", out var arguments) ||
                            arguments.ValueKind != JsonValueKind.String)
                        {
                            chunk.Error = CreateResponsesStreamError(
                                "OpenAI Responses API completed a function call without string JSON arguments; no tools were executed.",
                                "malformed_function_call",
                                $"output_index={outputIndex}");
                            return;
                        }

                        var functionCall = CreateStreamFunctionCall(outputIndex, item);
                        functionCall.Name = functionName.GetString()!;
                        functionCall.Id = callId.GetString()!;
                        functionCall.Arguments = new Dictionary<string, object>
                        {
                            ["_complete"] = arguments.GetString() ?? string.Empty
                        };

                        chunk.FunctionCalls.Add(functionCall);
                    }

                    outputIndex++;
                }

                if (outputItems.Count > 0)
                    _currentStreamResponseOutputItems = outputItems;
            }
        }

        private void ParseStreamFailureEvent(JsonElement root, string type, OpenAIStreamChunk chunk)
        {
            var response = root.TryGetProperty("response", out var responseElement) &&
                           responseElement.ValueKind == JsonValueKind.Object
                ? responseElement
                : root;
            var status = response.TryGetProperty("status", out var statusElement) &&
                         statusElement.ValueKind == JsonValueKind.String
                ? statusElement.GetString()
                : type == "error" ? "error" : type.Substring("response.".Length);

            chunk.Error = CreateResponsesStreamError(
                $"OpenAI Responses API stream ended with {type}; the partial response was not saved and no tools were executed.",
                status ?? "failed",
                ExtractResponsesFailureReason(response));
        }

        private void ParseStreamRefusalEvent(JsonElement root, OpenAIStreamChunk chunk)
        {
            string? refusal = null;
            if (root.TryGetProperty("refusal", out var refusalElement) &&
                refusalElement.ValueKind == JsonValueKind.String)
            {
                refusal = refusalElement.GetString();
            }
            else if (root.TryGetProperty("delta", out var delta) &&
                     delta.ValueKind == JsonValueKind.String)
            {
                refusal = delta.GetString();
            }

            chunk.Error = CreateResponsesStreamError(
                "OpenAI Responses API streamed a refusal; the partial response was not saved and no tools were executed.",
                "refusal",
                refusal);
        }

        private StreamingContent CreateResponsesStreamError(
            string message,
            string status,
            string? reason)
        {
            var metadata = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["status"] = status
            };
            if (!string.IsNullOrEmpty(reason))
                metadata["reason"] = reason;

            return new StreamingContent
            {
                Type = StreamingContentType.Error,
                Content = message,
                Metadata = metadata
            };
        }

        private void CaptureCompletedStreamOutputItem(JsonElement root, JsonElement item)
        {
            int outputIndex;
            if (root.TryGetProperty("output_index", out var outputIndexElement) &&
                outputIndexElement.TryGetInt32(out var parsedOutputIndex))
            {
                outputIndex = parsedOutputIndex;
            }
            else
            {
                outputIndex = _currentStreamNextOutputIndex;
                while (_currentStreamCompletedOutputItems.ContainsKey(outputIndex))
                    outputIndex++;
            }

            _currentStreamCompletedOutputItems[outputIndex] = item.Clone();
            if (outputIndex >= _currentStreamNextOutputIndex)
                _currentStreamNextOutputIndex = outputIndex + 1;
        }

        private void ResetCurrentStreamOutputItems()
        {
            _currentStreamResponseOutputItems = null;
            _currentStreamCompletedOutputItems.Clear();
            _currentStreamReasoningParts.Clear();
            _currentStreamNextOutputIndex = 0;
        }

        private void AppendReasoningDelta(string partKey, string delta)
        {
            if (!_currentStreamReasoningParts.TryGetValue(partKey, out var emitted))
            {
                emitted = new StringBuilder();
                _currentStreamReasoningParts[partKey] = emitted;
            }

            emitted.Append(delta);
        }

        private string? GetUnseenReasoningSnapshot(string partKey, string? snapshot)
        {
            if (string.IsNullOrEmpty(snapshot))
                return null;

            if (!_currentStreamReasoningParts.TryGetValue(partKey, out var emitted))
            {
                _currentStreamReasoningParts[partKey] = new StringBuilder(snapshot);
                return snapshot;
            }

            var emittedText = emitted.ToString();
            if (snapshot.StartsWith(emittedText, StringComparison.Ordinal))
            {
                var unseen = snapshot.Substring(emittedText.Length);
                emitted.Clear();
                emitted.Append(snapshot);
                return unseen.Length == 0 ? null : unseen;
            }

            // A completed snapshot must not replay text that was already yielded as deltas.
            // If the provider sends a shorter or divergent snapshot, there is no safe way to
            // retract streamed text, so preserve the already emitted value and suppress it.
            return null;
        }

        private static string GetReasoningPartKey(
            JsonElement root,
            JsonElement? item = null,
            int? summaryIndex = null)
        {
            string outputKey;
            if (root.TryGetProperty("output_index", out var outputIndex) &&
                outputIndex.TryGetInt32(out var parsedOutputIndex))
            {
                outputKey = $"output:{parsedOutputIndex}";
            }
            else if (root.TryGetProperty("item_id", out var itemId) &&
                     itemId.ValueKind == JsonValueKind.String)
            {
                outputKey = $"item:{itemId.GetString()}";
            }
            else if (item.HasValue &&
                     item.Value.TryGetProperty("id", out var nestedItemId) &&
                     nestedItemId.ValueKind == JsonValueKind.String)
            {
                outputKey = $"item:{nestedItemId.GetString()}";
            }
            else
            {
                outputKey = "reasoning";
            }

            if (!summaryIndex.HasValue &&
                root.TryGetProperty("summary_index", out var rootSummaryIndex) &&
                rootSummaryIndex.TryGetInt32(out var parsedSummaryIndex))
            {
                summaryIndex = parsedSummaryIndex;
            }

            return summaryIndex.HasValue
                ? $"{outputKey}:summary:{summaryIndex.Value}"
                : outputKey;
        }

        private void ParseLegacyStreamChunk(JsonElement choices, OpenAIStreamChunk chunk)
        {
            if (choices.ValueKind != JsonValueKind.Array)
                throw new JsonException("The choices field must be an array.");
            if (choices.GetArrayLength() == 0) return;

            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var finishReasonElement) &&
                finishReasonElement.ValueKind != JsonValueKind.Null)
            {
                if (finishReasonElement.ValueKind != JsonValueKind.String)
                    throw new JsonException("The finish_reason field must be a string or null.");

                var finishReason = finishReasonElement.GetString();
                chunk.FinishReason = finishReason;
                if (finishReason != "stop" &&
                    finishReason != "tool_calls" &&
                    finishReason != "function_call")
                {
                    chunk.Error = CreateResponsesStreamError(
                        "OpenAI Chat Completions ended before a complete response was available; no tools were executed.",
                        "unsafe_finish_reason",
                        finishReason ?? "missing");
                    chunk.Error.Metadata!["finish_reason"] = finishReason ?? "missing";
                    return;
                }
            }

            if (!choice.TryGetProperty("delta", out var legacyDelta)) return;

            if (legacyDelta.TryGetProperty("content", out var legacyContent))
            {
                var text = legacyContent.GetString();
                if (!string.IsNullOrEmpty(text))
                    chunk.Text = text;
            }

            if (legacyDelta.TryGetProperty("tool_calls", out var toolCalls) &&
                toolCalls.ValueKind != JsonValueKind.Null)
            {
                if (toolCalls.ValueKind != JsonValueKind.Array)
                    throw new JsonException("The tool_calls field must be an array.");

                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    if (toolCall.ValueKind != JsonValueKind.Object)
                        throw new JsonException("Every streamed tool call must be an object.");

                    var parsedIndex = -1;
                    var hasExplicitIndex = toolCall.TryGetProperty("index", out var index) &&
                                           index.ValueKind == JsonValueKind.Number &&
                                           index.TryGetInt32(out parsedIndex);
                    var functionCall = new FunctionCall
                    {
                        Source = IdSource.OpenAI,
                        Index = hasExplicitIndex
                            ? parsedIndex
                            : chunk.FunctionCalls.Count,
                        Arguments = new Dictionary<string, object> { ["_missing"] = true },
                        Metadata = new Dictionary<string, object>
                        {
                            [StreamIndexExplicitMetadataKey] = hasExplicitIndex,
                            [RequiresProviderCallIdMetadataKey] = true
                        }
                    };

                    if (toolCall.TryGetProperty("id", out var id))
                    {
                        if (id.ValueKind != JsonValueKind.String)
                            throw new JsonException("A streamed tool-call ID must be a string.");
                        functionCall.Id = id.GetString() ?? string.Empty;
                    }

                    if (toolCall.TryGetProperty("function", out var function))
                    {
                        if (function.ValueKind != JsonValueKind.Object)
                            throw new JsonException("The streamed function payload must be an object.");
                        if (function.TryGetProperty("name", out var name))
                        {
                            if (name.ValueKind != JsonValueKind.String)
                                throw new JsonException("A streamed function name must be a string.");
                            functionCall.Name = name.GetString() ?? string.Empty;
                        }
                        if (function.TryGetProperty("arguments", out var arguments))
                        {
                            if (arguments.ValueKind != JsonValueKind.String)
                                throw new JsonException("Streamed function arguments must be a string.");
                            functionCall.Arguments = new Dictionary<string, object>
                            {
                                ["_partial"] = arguments.GetString() ?? string.Empty
                            };
                        }
                    }
                    else
                    {
                        throw new JsonException("A streamed tool call is missing its function payload.");
                    }

                    chunk.FunctionCalls.Add(functionCall);
                }
            }
            else if (legacyDelta.TryGetProperty("function_call", out var legacyFc))
            {
                var functionCall = new FunctionCall
                {
                    Source = IdSource.OpenAI,
                    Index = 0,
                    Arguments = new Dictionary<string, object> { ["_missing"] = true },
                    Metadata = new Dictionary<string, object>
                    {
                        [StreamIndexExplicitMetadataKey] = false,
                        [RequiresProviderCallIdMetadataKey] = false
                    }
                };

                if (legacyFc.TryGetProperty("name", out var name))
                {
                    if (name.ValueKind != JsonValueKind.String)
                        throw new JsonException("A streamed function name must be a string.");
                    functionCall.Name = name.GetString() ?? string.Empty;
                }

                if (legacyFc.TryGetProperty("arguments", out var args))
                {
                    if (args.ValueKind != JsonValueKind.String)
                        throw new JsonException("Streamed function arguments must be a string.");
                    functionCall.Arguments = new Dictionary<string, object>
                    {
                        ["_partial"] = args.GetString() ?? string.Empty
                    };
                }

                chunk.FunctionCalls.Add(functionCall);
            }
        }

        private static FunctionCall CreateStreamFunctionCall(JsonElement root, JsonElement? item = null)
        {
            var functionCall = CreateStreamFunctionCall(GetStreamOutputIndex(root), item);
            functionCall.Metadata ??= new Dictionary<string, object>();
            functionCall.Metadata[StreamIndexExplicitMetadataKey] =
                root.TryGetProperty("output_index", out var outputIndex) &&
                outputIndex.ValueKind == JsonValueKind.Number &&
                outputIndex.TryGetInt32(out _);
            var streamItemId = GetStreamItemId(root, item);
            if (!string.IsNullOrEmpty(streamItemId))
            {
                functionCall.Metadata[StreamItemIdMetadataKey] = streamItemId;
            }

            if (root.TryGetProperty("call_id", out var callId) &&
                callId.ValueKind == JsonValueKind.String)
            {
                functionCall.Id = callId.GetString() ?? string.Empty;
            }
            if (root.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String)
            {
                functionCall.Name = name.GetString() ?? string.Empty;
            }

            return functionCall;
        }

        private static FunctionCall CreateStreamFunctionCall(int outputIndex, JsonElement? item)
        {
            var functionCall = new FunctionCall
            {
                Source = IdSource.OpenAI,
                Index = outputIndex,
                Arguments = new Dictionary<string, object> { ["_missing"] = true },
                Metadata = new Dictionary<string, object>
                {
                    [StreamIndexExplicitMetadataKey] = true,
                    [RequiresProviderCallIdMetadataKey] = true
                }
            };

            if (item.HasValue && item.Value.TryGetProperty("id", out var itemId) &&
                itemId.ValueKind == JsonValueKind.String)
            {
                functionCall.Metadata[StreamItemIdMetadataKey] = itemId.GetString() ?? string.Empty;
            }

            return functionCall;
        }

        private static int GetStreamOutputIndex(JsonElement root)
        {
            return root.TryGetProperty("output_index", out var outputIndex) &&
                   outputIndex.TryGetInt32(out var parsedIndex)
                ? parsedIndex
                : -1;
        }

        private static string? GetStreamItemId(JsonElement root, JsonElement? item)
        {
            if (root.TryGetProperty("item_id", out var rootItemId) &&
                rootItemId.ValueKind == JsonValueKind.String)
            {
                return rootItemId.GetString();
            }

            if (item.HasValue && item.Value.TryGetProperty("id", out var nestedItemId) &&
                nestedItemId.ValueKind == JsonValueKind.String)
            {
                return nestedItemId.GetString();
            }

            return null;
        }

        // ExtractTextFromContent 메서드 제거 - Parsing.cs에 있는 것 사용

        #endregion
    }
}
