using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        #region Streaming Implementation

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options,
            bool useFunctions,
            FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (policy.EnableLogging)
                Console.WriteLine($"[Claude Stream Round]");

            var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
            using var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Error,
                    Content = $"API error ({(int)response.StatusCode}): {error}",
                    Metadata = AIHttpErrorFactory.BuildErrorMetadata((int)response.StatusCode, error)
                };
                yield break;
            }

            // ── Phase 1: Read stream and yield text/thinking chunks in real-time ──
            var textBuffer = new StringBuilder();
            var thinkingBuffer = new StringBuilder();
            var collectedToolUses = new List<FunctionCall>();
            var toolUsesByIndex = new Dictionary<int, ToolUseData>();
            var announcedToolUseIds = new HashSet<string>(StringComparer.Ordinal);
            var assistantContent = new ClaudeStreamContentAccumulator();
            var functionCallBatchId = Guid.NewGuid().ToString();
            bool refusalDetected = false;
            bool streamFailed = false;
            bool messageCompleted = false;
            string? finalStopReason = null;
            string? currentModel = null;
            int? accInputTokens = null;
            int? accOutputTokens = null;
            int? accCachedInputTokens = null;
            int? accCacheCreationTokens = null;

            var diagnostics = new StreamDiagnostics();

            await foreach (var line in ReadSseLinesAsync(response, diagnostics, cancellationToken))
            {
                if (!line.StartsWith(SseDataPrefix) && !line.StartsWith(SseEventPrefix))
                    continue;

                if (line.StartsWith(SseEventPrefix))
                {
                    continue;
                }

                var jsonData = line.Substring(SseDataPrefix.Length).Trim();
                if (string.IsNullOrEmpty(jsonData))
                    continue;

                var parseResult = TryParseClaudeStreamChunk(
                    jsonData,
                    toolUsesByIndex,
                    assistantContent,
                    options,
                    policy);
                if (parseResult == null)
                {
                    diagnostics.ParseFailures++;
                    streamFailed = true;
                    collectedToolUses.Clear();
                    toolUsesByIndex.Clear();
                    yield return new StreamingContent
                    {
                        Type = StreamingContentType.Error,
                        Content = "Claude emitted a malformed streaming response; no tools were executed and the partial response was not saved.",
                        Metadata = new Dictionary<string, object>
                        {
                            ["model"] = currentModel ?? Model,
                            ["error_type"] = "malformed_stream"
                        }
                    };
                    break;
                }
                diagnostics.DataLinesProcessed++;

                    if (currentModel == null && parseResult.Model != null)
                        currentModel = parseResult.Model;
                    if (parseResult.InputTokens.HasValue)
                        accInputTokens = parseResult.InputTokens;
                    if (parseResult.OutputTokens.HasValue)
                        accOutputTokens = parseResult.OutputTokens;
                    if (parseResult.CachedInputTokens.HasValue)
                        accCachedInputTokens = parseResult.CachedInputTokens;
                    if (parseResult.CacheCreationTokens.HasValue)
                        accCacheCreationTokens = parseResult.CacheCreationTokens;

                    if (!string.IsNullOrWhiteSpace(parseResult.StreamErrorMessage))
                    {
                        streamFailed = true;
                        collectedToolUses.Clear();

                        var metadata = new Dictionary<string, object>
                        {
                            ["model"] = currentModel ?? Model
                        };
                        if (!string.IsNullOrWhiteSpace(parseResult.StreamErrorType))
                            metadata["error_type"] = parseResult.StreamErrorType;
                        if (!string.IsNullOrWhiteSpace(parseResult.StreamErrorDetails))
                            metadata["error_details"] = parseResult.StreamErrorDetails;

                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.Error,
                            Content = parseResult.StreamErrorMessage,
                            Metadata = metadata
                        };
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(parseResult.StopReason))
                        finalStopReason = parseResult.StopReason;

                    if (IsTruncationStopReason(parseResult.StopReason) ||
                        string.Equals(parseResult.StopReason, "pause_turn", StringComparison.Ordinal))
                    {
                        streamFailed = true;
                        collectedToolUses.Clear();

                        var terminalError = new StreamingContent
                        {
                            Type = StreamingContentType.Error,
                            Content = IsTruncationStopReason(parseResult.StopReason)
                                ? "Claude stopped before completing the response; the partial response was not saved."
                                : "Claude paused a server-tool turn, which this client-side tool loop cannot resume automatically.",
                            Metadata = new Dictionary<string, object>
                            {
                                ["stop_reason"] = parseResult.StopReason!,
                                ["model"] = currentModel ?? Model
                            }
                        };
                        if (accInputTokens.HasValue || accOutputTokens.HasValue)
                        {
                            var input = accInputTokens ?? 0;
                            var output = accOutputTokens ?? 0;
                            terminalError.Usage = new TokenUsage
                            {
                                InputTokens = input,
                                OutputTokens = output,
                                TotalTokens = input + output,
                                CachedInputTokens = accCachedInputTokens ?? 0,
                                CacheCreationTokens = accCacheCreationTokens ?? 0
                            };
                        }

                        yield return terminalError;
                        break;
                    }

                    if (string.Equals(parseResult.StopReason, "refusal", StringComparison.Ordinal))
                    {
                        refusalDetected = true;
                        collectedToolUses.Clear();

                        var metadata = new Dictionary<string, object>
                        {
                            ["stop_reason"] = "refusal",
                            ["model"] = currentModel ?? Model
                        };
                        if (!string.IsNullOrWhiteSpace(parseResult.RefusalCategory))
                            metadata["category"] = parseResult.RefusalCategory;
                        if (!string.IsNullOrWhiteSpace(parseResult.RefusalExplanation))
                            metadata["explanation"] = parseResult.RefusalExplanation;

                        var refusal = new StreamingContent
                        {
                            Type = StreamingContentType.Error,
                            Content = "Claude declined to process the request (stop_reason=refusal).",
                            Metadata = metadata
                        };
                        if (accInputTokens.HasValue || accOutputTokens.HasValue)
                        {
                            var input = accInputTokens ?? 0;
                            var output = accOutputTokens ?? 0;
                            refusal.Usage = new TokenUsage
                            {
                                InputTokens = input,
                                OutputTokens = output,
                                TotalTokens = input + output,
                                CachedInputTokens = accCachedInputTokens ?? 0,
                                CacheCreationTokens = accCacheCreationTokens ?? 0
                            };
                        }

                        yield return refusal;
                        break;
                    }

                    // Tool use started
                    var startedToolUse = parseResult.StartedToolUse;
                    if (startedToolUse != null && options.IncludeFunctionCalls &&
                        !string.IsNullOrEmpty(startedToolUse.Id) &&
                        !string.IsNullOrEmpty(startedToolUse.Name) &&
                        announcedToolUseIds.Add(startedToolUse.Id))
                    {
                        var functionCall = new FunctionCall
                        {
                            Id = startedToolUse.Id,
                            Source = IdSource.Claude,
                            Name = startedToolUse.Name ?? string.Empty,
                            Index = announcedToolUseIds.Count - 1
                        };
                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.FunctionCall,
                            FunctionCall = functionCall,
                            FunctionCallBatchId = functionCallBatchId,
                            Metadata = new Dictionary<string, object>
                            {
                                ["function_name"] = functionCall.Name,
                                ["function_id"] = functionCall.Id,
                                ["function_index"] = functionCall.Index,
                                ["tool_use_id"] = functionCall.Id,
                                ["status"] = "started"
                            }
                        };

                        if (policy.EnableLogging)
                            Console.WriteLine($"  → Tool use detected: {startedToolUse.Name}");
                    }

                    // Thinking content — yield immediately
                    if (!string.IsNullOrEmpty(parseResult.ThinkingContent))
                    {
                        thinkingBuffer.Append(parseResult.ThinkingContent);
                        if (options.IncludeReasoning)
                        {
                            yield return new StreamingContent
                            {
                                Type = StreamingContentType.Reasoning,
                                Content = parseResult.ThinkingContent,
                                Metadata = options.IncludeMetadata ? new Dictionary<string, object>
                                {
                                    ["model"] = currentModel ?? Model
                                } : null
                            };
                        }
                    }

                    // Text content — yield immediately
                    if (!string.IsNullOrEmpty(parseResult.TextContent))
                    {
                        textBuffer.Append(parseResult.TextContent);
                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.Text,
                            Content = parseResult.TextContent,
                            Metadata = options.IncludeMetadata ? new Dictionary<string, object>
                            {
                                ["model"] = currentModel ?? Model
                            } : null
                        };
                    }

                    // Tool use completed — collect for post-processing
                    var completedToolUseData = parseResult.CompletedToolUse;
                    if (completedToolUseData != null)
                    {
                        if (!TryCollectCompletedToolUse(completedToolUseData, out var completedToolUse, out var argumentError))
                        {
                            streamFailed = true;
                            collectedToolUses.Clear();
                            toolUsesByIndex.Clear();
                            yield return new StreamingContent
                            {
                                Type = StreamingContentType.Error,
                                Content = argumentError,
                                Metadata = new Dictionary<string, object>
                                {
                                    ["model"] = currentModel ?? Model,
                                    ["error_type"] = "invalid_tool_arguments",
                                    ["function_name"] = completedToolUseData.Name ?? "unknown",
                                    ["tool_use_id"] = completedToolUseData.Id ?? string.Empty
                                }
                            };
                            break;
                        }

                        completedToolUse!.Index = completedToolUseData.Index;
                        collectedToolUses.Add(completedToolUse);
                    }

                    // Message complete — always yield unless TextOnly
                    if (parseResult.MessageComplete)
                    {
                        messageCompleted = true;

                        if (toolUsesByIndex.Count > 0)
                        {
                            streamFailed = true;
                            collectedToolUses.Clear();
                            toolUsesByIndex.Clear();
                            yield return new StreamingContent
                            {
                                Type = StreamingContentType.Error,
                                Content = "Claude completed the stream with an unfinished tool-use block; no tools were executed.",
                                Metadata = new Dictionary<string, object>
                                {
                                    ["model"] = currentModel ?? Model,
                                    ["error_type"] = "incomplete_tool_use"
                                }
                            };
                            break;
                        }

                        if (collectedToolUses.Count > 0 &&
                            !string.Equals(finalStopReason, "tool_use", StringComparison.Ordinal))
                        {
                            streamFailed = true;
                            collectedToolUses.Clear();
                            yield return new StreamingContent
                            {
                                Type = StreamingContentType.Error,
                                Content = "Claude completed a stream containing tool calls without stop_reason=tool_use; no tools were executed.",
                                Metadata = new Dictionary<string, object>
                                {
                                    ["model"] = currentModel ?? Model,
                                    ["stop_reason"] = finalStopReason ?? "missing"
                                }
                            };
                            break;
                        }

                        if (collectedToolUses.Count == 0 &&
                            string.Equals(finalStopReason, "tool_use", StringComparison.Ordinal))
                        {
                            streamFailed = true;
                            yield return new StreamingContent
                            {
                                Type = StreamingContentType.Error,
                                Content = "Claude reported stop_reason=tool_use without a usable tool call; no tool was executed.",
                                Metadata = new Dictionary<string, object>
                                {
                                    ["model"] = currentModel ?? Model,
                                    ["stop_reason"] = finalStopReason!
                                }
                            };
                            break;
                        }

                        if (!options.TextOnly)
                        {
                            var completionContent = new StreamingContent
                            {
                                Type = StreamingContentType.Completion
                            };
                            if (options.IncludeMetadata)
                            {
                                completionContent.Metadata = new Dictionary<string, object>
                                {
                                    ["total_length"] = textBuffer.Length,
                                    ["model"] = currentModel ?? Model,
                                    ["stop_reason"] = finalStopReason ?? "missing"
                                };
                            }
                            if (accInputTokens.HasValue || accOutputTokens.HasValue)
                            {
                                var input = accInputTokens ?? 0;
                                var output = accOutputTokens ?? 0;
                                completionContent.Usage = new TokenUsage
                                {
                                    InputTokens = input,
                                    OutputTokens = output,
                                    TotalTokens = input + output,
                                    CachedInputTokens = accCachedInputTokens ?? 0,
                                    CacheCreationTokens = accCacheCreationTokens ?? 0
                                };
                            }
                            yield return completionContent;
                        }
                        break;
                    }
            }

            // ── Phase 2: Post-processing (function execution) ──
            if (refusalDetected || streamFailed)
                yield break;

            // A tool block is not authorization to execute by itself. Anthropic only commits the
            // round after message_stop, and tool execution is valid only with stop_reason=tool_use.
            // This prevents side effects after a truncated connection or malformed SSE sequence.
            if (!messageCompleted)
            {
                collectedToolUses.Clear();
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Error,
                    Content = "Claude stream ended before message_stop; no tools were executed and the partial response was not saved.",
                    Metadata = new Dictionary<string, object>
                    {
                        ["model"] = currentModel ?? Model,
                        ["stop_reason"] = finalStopReason ?? "missing"
                    }
                };
                yield break;
            }

            if (collectedToolUses.Count > 0)
            {
                collectedToolUses.Sort((left, right) => left.Index.CompareTo(right.Index));
                for (var index = 0; index < collectedToolUses.Count; index++)
                    collectedToolUses[index].Index = index;

                if (policy.EnableLogging)
                    Console.WriteLine($"  → Processing {collectedToolUses.Count} tool use(s)");

                var functionCalls = new FunctionCallBatch(collectedToolUses)
                {
                    Id = functionCallBatchId
                };
                var originalContent = assistantContent.Serialize();
                if (!string.IsNullOrWhiteSpace(originalContent))
                {
                    functionCalls.Metadata = new Dictionary<string, object>
                    {
                        [MessageMetadataKeys.OriginalContent] = originalContent
                    };
                }

                FunctionCallResultBatch? functionResults = null;
                AIServiceException? batchException = null;
                try
                {
                    functionResults = await ProcessToolUseBatchAsync(
                        functionCalls,
                        textBuffer.ToString(),
                        policy,
                        cancellationToken);
                }
                catch (AIServiceException exception)
                {
                    batchException = exception;
                }

                if (batchException != null)
                {
                    yield return new StreamingContent
                    {
                        Type = StreamingContentType.Error,
                        Content = "Claude emitted an invalid tool-use batch; no tools were executed.",
                        Metadata = new Dictionary<string, object>
                        {
                            ["model"] = currentModel ?? Model,
                            ["error_type"] = "invalid_tool_batch",
                            ["error_details"] = batchException.Message
                        }
                    };
                    yield break;
                }

                foreach (var functionResult in functionResults!.Results)
                {
                    yield return new StreamingContent
                    {
                        Type = StreamingContentType.FunctionResult,
                        Content = functionResult.Content,
                        FunctionResult = functionResult,
                        FunctionCallBatchId = functionCalls.Id,
                        Metadata = new Dictionary<string, object>
                        {
                            ["function_name"] = functionResult.Call.Name,
                            ["function_id"] = functionResult.Call.Id,
                            ["function_index"] = functionResult.Call.Index,
                            ["function_arguments"] = functionResult.Call.Arguments != null
                                ? JsonSerializer.Serialize(functionResult.Call.Arguments)
                                : "{}",
                            ["status"] = "completed",
                            ["result"] = functionResult.Content,
                            ["is_error"] = functionResult.IsError
                        }
                    };
                }
            }
            else if (textBuffer.Length > 0)
            {
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, textBuffer.ToString()));
            }
        }

        // Legacy callback-based method (for compatibility)
        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            await foreach (var content in StreamAsync(message, StreamOptions.TextOnlyOptions))
            {
                if (content.Type == StreamingContentType.Text && content.Content != null)
                    await messageReceivedAsync(content.Content);
                else if (content.Type == StreamingContentType.Error)
                    throw new AIServiceException(
                        content.Content ?? "Claude streaming request failed.",
                        content.Metadata == null ? string.Empty : JsonSerializer.Serialize(content.Metadata),
                        nameof(AIProvider.Anthropic));
            }
        }

        #endregion

        #region Helper Classes and Methods

        private class ToolUseData
        {
            public int Index { get; set; }
            public string? Id { get; set; }
            public string? Name { get; set; }
            public StringBuilder Arguments { get; } = new StringBuilder();
        }

        private class ClaudeStreamParseResult
        {
            public string? TextContent { get; set; }
            public string? ThinkingContent { get; set; }
            public ToolUseData? StartedToolUse { get; set; }
            public ToolUseData? CompletedToolUse { get; set; }
            public bool MessageComplete { get; set; }
            public string? Model { get; set; }
            public int? InputTokens { get; set; }
            public int? OutputTokens { get; set; }
            public int? CachedInputTokens { get; set; }
            public int? CacheCreationTokens { get; set; }
            public string? StopReason { get; set; }
            public string? RefusalCategory { get; set; }
            public string? RefusalExplanation { get; set; }
            public string? StreamErrorType { get; set; }
            public string? StreamErrorMessage { get; set; }
            public string? StreamErrorDetails { get; set; }
        }

        private sealed class ClaudeStreamContentAccumulator
        {
            private readonly SortedDictionary<int, ClaudeStreamBlockAccumulator> _blocks =
                new SortedDictionary<int, ClaudeStreamBlockAccumulator>();

            public void StartBlock(int index, JsonElement contentBlock)
            {
                _blocks[index] = new ClaudeStreamBlockAccumulator(contentBlock);
            }

            public void ApplyDelta(int index, JsonElement delta)
            {
                if (_blocks.TryGetValue(index, out var block))
                    block.ApplyDelta(delta);
            }

            public string? Serialize()
            {
                if (_blocks.Count == 0) return null;
                return JsonSerializer.Serialize(_blocks.Values.Select(block => block.Build()).ToList());
            }
        }

        private sealed class ClaudeStreamBlockAccumulator
        {
            private readonly string _startJson;
            private readonly string _type;
            private readonly StringBuilder _text = new StringBuilder();
            private readonly StringBuilder _thinking = new StringBuilder();
            private readonly StringBuilder _signature = new StringBuilder();
            private readonly StringBuilder _input = new StringBuilder();
            private readonly List<string> _citations = new List<string>();
            private bool _inputTouched;

            public ClaudeStreamBlockAccumulator(JsonElement contentBlock)
            {
                _startJson = contentBlock.GetRawText();
                _type = contentBlock.TryGetProperty("type", out var type) ? type.GetString() ?? string.Empty : string.Empty;

                AppendString(contentBlock, "text", _text);
                AppendString(contentBlock, "thinking", _thinking);
                AppendString(contentBlock, "signature", _signature);

                if (contentBlock.TryGetProperty("input", out var input) &&
                    input.ValueKind != JsonValueKind.Null && input.GetRawText() != "{}")
                {
                    _input.Append(input.GetRawText());
                    _inputTouched = true;
                }

                if (contentBlock.TryGetProperty("citations", out var citations) &&
                    citations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var citation in citations.EnumerateArray())
                        _citations.Add(citation.GetRawText());
                }
            }

            public void ApplyDelta(JsonElement delta)
            {
                if (!delta.TryGetProperty("type", out var typeElement)) return;

                switch (typeElement.GetString())
                {
                    case "text_delta":
                        AppendString(delta, "text", _text);
                        break;
                    case "thinking_delta":
                        AppendString(delta, "thinking", _thinking);
                        break;
                    case "signature_delta":
                        AppendString(delta, "signature", _signature);
                        break;
                    case "input_json_delta":
                        AppendString(delta, "partial_json", _input);
                        _inputTouched = true;
                        break;
                    case "citations_delta":
                        if (delta.TryGetProperty("citation", out var citation))
                            _citations.Add(citation.GetRawText());
                        break;
                }
            }

            public Dictionary<string, object> Build()
            {
                var block = JsonSerializer.Deserialize<Dictionary<string, object>>(_startJson) ??
                            new Dictionary<string, object>();

                if (_type == "text") block["text"] = _text.ToString();
                if (_type == "thinking")
                {
                    block["thinking"] = _thinking.ToString();
                    if (_signature.Length > 0 || block.ContainsKey("signature"))
                        block["signature"] = _signature.ToString();
                }

                if (_inputTouched)
                {
                    try
                    {
                        block["input"] = JsonSerializer.Deserialize<JsonElement>(_input.ToString());
                    }
                    catch (JsonException)
                    {
                        block["input"] = new Dictionary<string, object>();
                    }
                }

                if (_citations.Count > 0)
                {
                    block["citations"] = _citations
                        .Select(citation => JsonSerializer.Deserialize<JsonElement>(citation))
                        .ToList();
                }

                return block;
            }

            private static void AppendString(JsonElement source, string propertyName, StringBuilder target)
            {
                if (source.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
                    target.Append(value.GetString());
            }
        }

        private ClaudeStreamParseResult? TryParseClaudeStreamChunk(
            string jsonData,
            Dictionary<int, ToolUseData> toolUsesByIndex,
            ClaudeStreamContentAccumulator contentAccumulator,
            StreamOptions options,
            FunctionCallingPolicy policy)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;
                var result = new ClaudeStreamParseResult();

                // Extract model info
                if (root.TryGetProperty("model", out var modelElem))
                {
                    result.Model = modelElem.GetString();
                }

                // Type-based processing
                if (root.TryGetProperty("type", out var typeElement))
                {
                    var type = typeElement.GetString();

                    switch (type)
                    {
                        case "message_start":
                            // Message start - can extract metadata and input token count
                            if (root.TryGetProperty("message", out var msgStart))
                            {
                                if (msgStart.TryGetProperty("model", out var msgModel))
                                {
                                    result.Model = msgModel.GetString();
                                }
                                if (msgStart.TryGetProperty("usage", out var startUsage))
                                {
                                    if (startUsage.TryGetProperty("input_tokens", out var inputTokens))
                                        result.InputTokens = inputTokens.GetInt32();
                                    if (startUsage.TryGetProperty("cache_read_input_tokens", out var cacheRead))
                                        result.CachedInputTokens = cacheRead.GetInt32();
                                    if (startUsage.TryGetProperty("cache_creation_input_tokens", out var cacheCreation))
                                        result.CacheCreationTokens = cacheCreation.GetInt32();
                                }
                            }
                            break;

                        case "content_block_start":
                            // Content block start
                            if (root.TryGetProperty("content_block", out var blockElement))
                            {
                                var startBlockIndex = -1;
                                var hasStartIndex = root.TryGetProperty("index", out var startIndex) &&
                                                    startIndex.ValueKind == JsonValueKind.Number &&
                                                    startIndex.TryGetInt32(out startBlockIndex);
                                if (hasStartIndex)
                                    contentAccumulator.StartBlock(startBlockIndex, blockElement);

                                if (blockElement.TryGetProperty("type", out var blockType))
                                {
                                    var blockTypeStr = blockType.GetString();

                                    if (blockTypeStr == "tool_use")
                                    {
                                        if (!hasStartIndex)
                                            throw new InvalidOperationException("Claude tool-use block is missing a valid content index.");
                                        if (toolUsesByIndex.ContainsKey(startBlockIndex))
                                            throw new InvalidOperationException($"Claude started duplicate tool-use block index {startBlockIndex}.");

                                        var startedToolUseData = new ToolUseData
                                        {
                                            Index = startBlockIndex,
                                            Id = blockElement.TryGetProperty("id", out var idElem) &&
                                                 idElem.ValueKind == JsonValueKind.String
                                            ? idElem.GetString()
                                            : null
                                        };

                                        if (blockElement.TryGetProperty("name", out var nameElem))
                                        {
                                            startedToolUseData.Name = nameElem.GetString();
                                        }

                                        if (blockElement.TryGetProperty("input", out var inputElement) &&
                                            inputElement.ValueKind != JsonValueKind.Null &&
                                            inputElement.GetRawText() != "{}")
                                        {
                                            startedToolUseData.Arguments.Append(inputElement.GetRawText());
                                        }

                                        toolUsesByIndex.Add(startBlockIndex, startedToolUseData);
                                        result.StartedToolUse = startedToolUseData;
                                    }
                                    // thinking block start - no special action needed, deltas will follow
                                }
                            }
                            break;

                        case "content_block_delta":
                            // Content delta
                            if (root.TryGetProperty("delta", out var deltaElement))
                            {
                                var deltaBlockIndex = -1;
                                var hasDeltaIndex = root.TryGetProperty("index", out var deltaIndex) &&
                                                    deltaIndex.ValueKind == JsonValueKind.Number &&
                                                    deltaIndex.TryGetInt32(out deltaBlockIndex);
                                if (hasDeltaIndex)
                                    contentAccumulator.ApplyDelta(deltaBlockIndex, deltaElement);

                                if (deltaElement.TryGetProperty("type", out var deltaType))
                                {
                                    var deltaTypeStr = deltaType.GetString();

                                    if (deltaTypeStr == "text_delta")
                                    {
                                        if (deltaElement.TryGetProperty("text", out var textElem))
                                        {
                                            result.TextContent = textElem.GetString();
                                        }
                                    }
                                    else if (deltaTypeStr == "thinking_delta")
                                    {
                                        if (deltaElement.TryGetProperty("thinking", out var thinkingElem))
                                        {
                                            result.ThinkingContent = thinkingElem.GetString();
                                        }
                                    }
                                    else if (deltaTypeStr == "input_json_delta")
                                    {
                                        if (!hasDeltaIndex || !toolUsesByIndex.TryGetValue(deltaBlockIndex, out var deltaToolUseData))
                                        {
                                            throw new InvalidOperationException(
                                                "Claude emitted tool arguments for an unknown content block.");
                                        }

                                        if (deltaElement.TryGetProperty("partial_json", out var jsonElem))
                                        {
                                            deltaToolUseData.Arguments.Append(jsonElem.GetString());
                                        }
                                    }
                                }
                            }
                            break;

                        case "content_block_stop":
                            // Content block complete
                            if (!root.TryGetProperty("index", out var stopIndex) ||
                                stopIndex.ValueKind != JsonValueKind.Number ||
                                !stopIndex.TryGetInt32(out var completedIndex))
                            {
                                throw new InvalidOperationException("Claude content-block stop is missing a valid index.");
                            }

                            if (toolUsesByIndex.TryGetValue(completedIndex, out var completedToolUse))
                            {
                                toolUsesByIndex.Remove(completedIndex);
                                result.CompletedToolUse = completedToolUse;
                            }
                            break;

                        case "message_delta":
                            // Message delta (usage info - output_tokens)
                            if (root.TryGetProperty("delta", out var messageDelta))
                            {
                                if (messageDelta.TryGetProperty("stop_reason", out var stopReason) &&
                                    stopReason.ValueKind == JsonValueKind.String)
                                {
                                    result.StopReason = stopReason.GetString();
                                }

                                if (messageDelta.TryGetProperty("stop_details", out var stopDetails) &&
                                    stopDetails.ValueKind == JsonValueKind.Object)
                                {
                                    if (stopDetails.TryGetProperty("category", out var category) &&
                                        category.ValueKind == JsonValueKind.String)
                                        result.RefusalCategory = category.GetString();
                                    if (stopDetails.TryGetProperty("explanation", out var explanation) &&
                                        explanation.ValueKind == JsonValueKind.String)
                                        result.RefusalExplanation = explanation.GetString();
                                }
                            }

                            if (root.TryGetProperty("usage", out var usageElem) &&
                                usageElem.TryGetProperty("output_tokens", out var outputTokens))
                            {
                                result.OutputTokens = outputTokens.GetInt32();
                            }
                            break;

                        case "message_stop":
                            // Message complete
                            result.MessageComplete = true;
                            break;

                        case "error":
                            // Error handling
                            if (root.TryGetProperty("error", out var errorElem))
                            {
                                result.StreamErrorDetails = errorElem.GetRawText();
                                if (errorElem.TryGetProperty("type", out var errorType) &&
                                    errorType.ValueKind == JsonValueKind.String)
                                    result.StreamErrorType = errorType.GetString();
                                if (errorElem.TryGetProperty("message", out var errorMessage) &&
                                    errorMessage.ValueKind == JsonValueKind.String)
                                    result.StreamErrorMessage = errorMessage.GetString();
                                result.StreamErrorMessage ??= "Claude returned a streaming API error.";

                                if (policy.EnableLogging)
                                {
                                    Console.WriteLine($"[Claude Stream Error] {errorElem.GetRawText()}");
                                }
                            }
                            break;
                    }
                }

                return result;
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
            {
                if (policy.EnableLogging)
                    Console.WriteLine($"[Claude Parse Error] {ex.Message}");
                return null;
            }
        }

        private static bool TryCollectCompletedToolUse(
            ToolUseData toolUseData,
            out FunctionCall? functionCall,
            out string? error)
        {
            functionCall = null;
            error = null;

            if (string.IsNullOrEmpty(toolUseData.Id))
            {
                error = $"Claude returned tool use '{toolUseData.Name}' without an ID; no tool was executed.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(toolUseData.Name))
            {
                error = $"Claude returned tool use '{toolUseData.Id}' without a name; no tool was executed.";
                return false;
            }

            var arguments = new Dictionary<string, object>();
            if (toolUseData.Arguments.Length > 0)
            {
                try
                {
                    arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        toolUseData.Arguments.ToString());
                    if (arguments == null)
                    {
                        error = $"Claude returned null arguments for tool '{toolUseData.Name}'; no tool was executed.";
                        return false;
                    }
                }
                catch (JsonException)
                {
                    error = $"Claude returned invalid JSON arguments for tool '{toolUseData.Name}'; no tool was executed.";
                    return false;
                }
            }

            functionCall = new FunctionCall
            {
                Id = toolUseData.Id,
                Source = IdSource.Claude,
                Name = toolUseData.Name,
                Arguments = arguments
            };
            return true;
        }

        #endregion
    }
}
