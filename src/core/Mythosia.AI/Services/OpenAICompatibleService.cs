using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
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

namespace Mythosia.AI.Services.Base
{
    /// <summary>
    /// Base class for providers that follow the OpenAI-compatible streaming format
    /// (SSE with "data:" prefix, "[DONE]" terminator, choices/delta structure).
    /// Used by ChatGPT, Grok, Qwen, and other compatible providers.
    /// Providers override <see cref="ParseStreamChunk"/> to handle provider-specific parsing.
    /// </summary>
    public abstract class OpenAICompatibleService : AIService
    {
        protected const string StreamItemIdMetadataKey = "stream_item_id";
        protected const string StreamIndexExplicitMetadataKey = "stream_index_explicit";
        protected const string RequiresProviderCallIdMetadataKey = "requires_provider_call_id";

        protected OpenAICompatibleService(string? apiKey, string baseUrl, HttpClient httpClient)
            : base(apiKey, baseUrl, httpClient)
        {
        }

        #region Streaming Implementation

        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            await foreach (var content in StreamAsync(message, StreamOptions.TextOnlyOptions))
            {
                if (content.Type == StreamingContentType.Text && content.Content != null)
                    await messageReceivedAsync(content.Content);
                else if (content.Type == StreamingContentType.Error)
                    throw new AIServiceException(
                        content.Content ?? $"{Provider} streaming request failed.",
                        content.Metadata == null ? string.Empty : JsonSerializer.Serialize(content.Metadata),
                        Provider);
            }
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options,
            bool useFunctions,
            FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (policy.EnableLogging)
                Console.WriteLine($"[{GetType().Name} Stream Round]");

            // 1. Create and send HTTP request. AIService.StreamCoreAsync supplies one linked token
            // for the complete round loop, so the same policy timeout covers both headers and the
            // SSE response body without resetting between function-calling rounds.
            OnStreamRoundStarting();
            var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
            var response = await HttpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

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

            // 2. Read stream and yield chunks in real-time
            var streamData = new OpenAIStreamData();
            var announcedFunctionCalls = new HashSet<int>();
            TokenUsage? lastUsage = null;
            Dictionary<string, object>? completionMetadata = null;
            var diagnostics = new StreamDiagnostics();
            bool doneMarkerReceived = false;
            bool completionEventReceived = false;
            string? finishReason = null;
            StreamingContent? streamFailure = null;

            await foreach (var line in ReadSseLinesAsync(response, diagnostics, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                    continue;

                var jsonData = line.Substring("data:".Length).Trim();
                if (jsonData == "[DONE]")
                {
                    doneMarkerReceived = true;
                    break;
                }

                OpenAIStreamChunk chunk;
                try
                {
                    chunk = ParseStreamChunk(jsonData, options);
                    diagnostics.DataLinesProcessed++;
                }
                catch (Exception ex)
                {
                    diagnostics.ParseFailures++;
                    streamFailure = CreateStreamParseFailure(jsonData, ex, diagnostics);
                    if (streamFailure != null)
                        break;

                    continue;
                }

                if (chunk.Error != null)
                {
                    streamFailure = chunk.Error;
                    break;
                }

                if (chunk.Usage != null)
                    lastUsage = chunk.Usage;

                if (chunk.Model != null)
                    streamData.Model = chunk.Model;
                if (!string.IsNullOrEmpty(chunk.FinishReason))
                    finishReason = chunk.FinishReason;

                // Provider-specific terminal event. Capture metadata/usage and emit only after validation.
                if (chunk.IsCompletion)
                {
                    completionEventReceived = true;
                    if (chunk.Metadata != null)
                        completionMetadata = chunk.Metadata;
                }

                // Reasoning — yield immediately
                if (chunk.Reasoning != null && options.IncludeReasoning)
                {
                    streamData.ReasoningBuffer.Append(chunk.Reasoning);
                    yield return new StreamingContent
                    {
                        Type = StreamingContentType.Reasoning,
                        Content = chunk.Reasoning,
                        Metadata = chunk.Metadata
                    };
                }

                // Text — yield immediately
                if (chunk.Text != null)
                {
                    streamData.TextBuffer.Append(chunk.Text);
                    diagnostics.AccumulatedTextLength += chunk.Text.Length;
                    yield return new StreamingContent
                    {
                        Type = StreamingContentType.Text,
                        Content = chunk.Text,
                        Metadata = chunk.Metadata
                    };
                }

                // Function call — collect for post-processing
                foreach (var functionCallDelta in chunk.FunctionCalls)
                {
                    var functionCall = streamData.UpdateFunctionCall(functionCallDelta);

                    if (options.IncludeFunctionCalls &&
                        !string.IsNullOrEmpty(functionCall.Name) &&
                        !string.IsNullOrEmpty(functionCall.Id) &&
                        announcedFunctionCalls.Add(functionCall.Index))
                    {
                        yield return CreateFunctionCallStreamingContent(
                            functionCall,
                            streamData.BatchId);
                    }
                }
            }

            if (streamFailure == null)
            {
                streamFailure = ValidateStreamTermination(
                    doneMarkerReceived,
                    completionEventReceived,
                    diagnostics);
            }

            if (streamFailure != null)
            {
                OnStreamRoundFailed();
                yield return streamFailure;
                yield break;
            }

            // OpenAI can emit a complete, forced tool-call payload with finish_reason="stop"
            // (observed on gpt-4o). The payload is still safe to execute after the argument
            // finalization below. Other terminal reasons remain unsafe, and a tool finish reason
            // without a payload is still rejected.
            var acceptedStopTerminatedFunctionPayload =
                useFunctions && streamData.HasFunctionCalls && finishReason == "stop";

            if (useFunctions &&
                ((streamData.HasFunctionCalls &&
                  !string.IsNullOrEmpty(finishReason) &&
                  finishReason != "tool_calls" &&
                  finishReason != "function_call" &&
                  !acceptedStopTerminatedFunctionPayload) ||
                 (!streamData.HasFunctionCalls &&
                  (finishReason == "tool_calls" || finishReason == "function_call"))))
            {
                OnStreamRoundFailed();
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Error,
                    Content = "The provider's finish reason did not match its function-call payload; no tools were executed.",
                    Metadata = new Dictionary<string, object>
                    {
                        ["status"] = "function_terminal_mismatch",
                        ["finish_reason"] = finishReason ?? "missing",
                        ["function_count"] = streamData.GetFunctionCalls().Count()
                    }
                };
                yield break;
            }

            if (streamData.HasFunctionCalls && useFunctions &&
                !streamData.TryFinalizeFunctionArguments(out var invalidCall, out var argumentError))
            {
                OnStreamRoundFailed();
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Error,
                    Content = "The provider completed a function call with malformed JSON arguments; no tools were executed.",
                    Metadata = new Dictionary<string, object>
                    {
                        ["model"] = streamData.Model ?? Model,
                        ["status"] = "malformed_function_arguments",
                        ["function_name"] = invalidCall?.Name ?? string.Empty,
                        ["reason"] = argumentError
                    }
                };
                yield break;
            }

            // A provider can send a function name or call ID late in the stream. Announce calls
            // that were not safe to identify earlier only after their final identity is stable.
            if (streamData.HasFunctionCalls && useFunctions && options.IncludeFunctionCalls)
            {
                foreach (var functionCall in streamData.GetFunctionCalls())
                {
                    if (!announcedFunctionCalls.Add(functionCall.Index))
                        continue;

                    yield return CreateFunctionCallStreamingContent(
                        functionCall,
                        streamData.BatchId);
                }
            }

            StreamingContent? completionContent = null;
            // Emit one provider-level completion after the terminal contract has been validated.
            if (!options.TextOnly &&
                (doneMarkerReceived || completionEventReceived ||
                 completionMetadata != null || lastUsage != null))
            {
                completionContent = new StreamingContent
                {
                    Type = StreamingContentType.Completion
                };
                if (options.IncludeMetadata)
                {
                    var meta = completionMetadata ?? new Dictionary<string, object>();
                    meta["total_length"] = streamData.TextBuffer.Length;
                    meta["model"] = streamData.Model ?? Model;
                    if (acceptedStopTerminatedFunctionPayload)
                        meta["function_finish_reason_mismatch"] = "stop";
                    completionContent.Metadata = meta;
                }
                if (lastUsage != null)
                    completionContent.Usage = lastUsage;
            }

            FunctionCallBatch? functionCalls = null;
            Message? pendingFunctionCallMessage = null;
            if (streamData.HasFunctionCalls && useFunctions)
            {
                functionCalls = streamData.CreateFunctionCallBatch();
                pendingFunctionCallMessage = new Message(ActorRole.Assistant, streamData.TextContent)
                {
                    FunctionCallBatch = functionCalls,
                    Metadata = acceptedStopTerminatedFunctionPayload
                        ? new Dictionary<string, object>
                        {
                            ["function_finish_reason_mismatch"] = "stop"
                        }
                        : null
                };
                // Capture provider-specific continuation data before invoking user handlers.
                EnrichStreamAssistantMessage(pendingFunctionCallMessage);
            }
            else if (streamData.HasContent)
            {
                var assistantMsg = new Message(ActorRole.Assistant, streamData.TextContent);
                EnrichStreamAssistantMessage(assistantMsg);
                ActivateChat.Messages.Add(assistantMsg);
            }

            // 4. Execute function if detected — yield FunctionResult to signal next round
            if (functionCalls != null)
            {
                FunctionCallResultBatch functionResults;
                try
                {
                    functionResults = await ProcessFunctionCallsAsync(
                        functionCalls,
                        policy,
                        cancellationToken);
                }
                catch
                {
                    OnStreamRoundFailed();
                    throw;
                }

                AddFunctionCallBatchToHistory(
                    streamData.TextContent,
                    functionCalls,
                    pendingFunctionCallMessage?.Metadata);
                AddFunctionResultBatchToHistory(functionResults);

                foreach (var functionResult in functionResults.Results)
                {
                    yield return new StreamingContent
                    {
                        Type = StreamingContentType.FunctionResult,
                        FunctionResult = functionResult.Clone(),
                        FunctionCallBatchId = functionCalls.Id,
                        Content = functionResult.Content,
                        Metadata = new Dictionary<string, object>
                        {
                            ["function_name"] = functionResult.Call.Name,
                            ["function_index"] = functionResult.Call.Index,
                            ["status"] = functionResult.IsError ? "error" : "completed",
                            ["result"] = functionResult.Content
                        }
                    };
                }
            }

            if (completionContent != null)
                yield return completionContent;
        }

        /// <summary>
        /// Parses a single SSE JSON chunk into a provider-neutral stream chunk.
        /// Each provider overrides this to handle its specific JSON format.
        /// </summary>
        protected abstract OpenAIStreamChunk ParseStreamChunk(string jsonData, StreamOptions options);

        /// <summary>
        /// Lets providers turn a malformed SSE data event into an explicit terminal error.
        /// Returning null preserves the tolerant behavior used by legacy compatible providers.
        /// </summary>
        protected virtual StreamingContent? CreateStreamParseFailure(
            string jsonData,
            Exception exception,
            StreamDiagnostics diagnostics)
        {
            return null;
        }

        /// <summary>
        /// Lets providers require an explicit successful terminal event before committing a
        /// streamed assistant message or executing a collected function call.
        /// </summary>
        protected virtual StreamingContent? ValidateStreamTermination(
            bool doneMarkerReceived,
            bool completionEventReceived,
            StreamDiagnostics diagnostics)
        {
            return null;
        }

        /// <summary>
        /// Lets providers discard state collected during a failed streaming round.
        /// </summary>
        protected virtual void OnStreamRoundFailed()
        {
        }

        /// <summary>
        /// Allows a provider to reset state scoped to a single streaming round.
        /// </summary>
        protected virtual void OnStreamRoundStarting()
        {
        }

        /// <summary>
        /// Allows a provider to attach state collected while streaming before the assistant
        /// message is added to the conversation history.
        /// </summary>
        protected virtual void EnrichStreamAssistantMessage(Message assistantMessage)
        {
        }

        private static StreamingContent CreateFunctionCallStreamingContent(
            FunctionCall functionCall,
            string batchId)
        {
            return new StreamingContent
            {
                Type = StreamingContentType.FunctionCall,
                FunctionCall = functionCall.Clone(),
                FunctionCallBatchId = batchId,
                Metadata = new Dictionary<string, object>
                {
                    ["function_name"] = functionCall.Name,
                    ["function_index"] = functionCall.Index,
                    ["status"] = "started"
                }
            };
        }

        /// <summary>
        /// Parses OpenAI-compatible usage JSON (handles both prompt_tokens/input_tokens variants).
        /// </summary>
        protected static TokenUsage? ParseOpenAICompatibleUsage(JsonElement usage)
        {
            // Streaming providers commonly emit `"usage": null` on non-terminal chunks and
            // omit the property entirely on others. Only an object is an actual usage record.
            if (usage.ValueKind != JsonValueKind.Object)
                return null;

            var tokenUsage = new TokenUsage();

            // Input tokens
            if (usage.TryGetProperty("input_tokens", out var input))
                tokenUsage.InputTokens = input.GetInt32();
            else if (usage.TryGetProperty("prompt_tokens", out var prompt))
                tokenUsage.InputTokens = prompt.GetInt32();

            // Output tokens
            if (usage.TryGetProperty("output_tokens", out var output))
                tokenUsage.OutputTokens = output.GetInt32();
            else if (usage.TryGetProperty("completion_tokens", out var completion))
                tokenUsage.OutputTokens = completion.GetInt32();

            // Total tokens
            if (usage.TryGetProperty("total_tokens", out var total))
                tokenUsage.TotalTokens = total.GetInt32();
            else
                tokenUsage.TotalTokens = tokenUsage.InputTokens + tokenUsage.OutputTokens;

            // Cache: Responses API input_tokens_details and legacy Chat Completions details.
            if (usage.TryGetProperty("input_tokens_details", out var inputDetails))
            {
                if (inputDetails.TryGetProperty("cached_tokens", out var cached))
                    tokenUsage.CachedInputTokens = cached.GetInt32();
                if (inputDetails.TryGetProperty("cache_write_tokens", out var cacheWrite))
                    tokenUsage.CacheCreationTokens = cacheWrite.GetInt32();
            }
            else if (usage.TryGetProperty("prompt_tokens_details", out var promptDetails) &&
                     promptDetails.TryGetProperty("cached_tokens", out var cached))
            {
                tokenUsage.CachedInputTokens = cached.GetInt32();
            }

            // Cache: DeepSeek prompt_cache_hit_tokens
            if (usage.TryGetProperty("prompt_cache_hit_tokens", out var cacheHit))
                tokenUsage.CachedInputTokens = cacheHit.GetInt32();

            // Reasoning: Responses API output details and legacy Chat Completions details.
            if (usage.TryGetProperty("output_tokens_details", out var outputDetails) &&
                outputDetails.TryGetProperty("reasoning_tokens", out var responseReasoning))
            {
                tokenUsage.ReasoningTokens = responseReasoning.GetInt32();
            }
            else if (usage.TryGetProperty("completion_tokens_details", out var completionDetails) &&
                     completionDetails.TryGetProperty("reasoning_tokens", out var legacyReasoning))
            {
                tokenUsage.ReasoningTokens = legacyReasoning.GetInt32();
            }

            return tokenUsage;
        }

        #endregion

        #region Helper Classes

        protected class OpenAIStreamChunk
        {
            public string? Text { get; set; }
            public string? Reasoning { get; set; }
            public bool IsCompletion { get; set; }
            public string? FinishReason { get; set; }
            public StreamingContent? Error { get; set; }
            public List<FunctionCall> FunctionCalls { get; } = new List<FunctionCall>();
            public string? Model { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
            public TokenUsage? Usage { get; set; }
        }

        protected class OpenAIStreamData
        {
            private readonly SortedDictionary<int, FunctionCallAccumulator> _functionCalls =
                new SortedDictionary<int, FunctionCallAccumulator>();
            private readonly Dictionary<string, int> _functionCallIndexesById =
                new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _functionCallIndexesByStreamKey =
                new Dictionary<string, int>(StringComparer.Ordinal);

            public StringBuilder TextBuffer { get; } = new StringBuilder();
            public StringBuilder ReasoningBuffer { get; } = new StringBuilder();
            public string BatchId { get; } = Guid.NewGuid().ToString();
            public string? Model { get; set; }
            public bool HasContent => TextBuffer.Length > 0;
            public bool HasFunctionCalls => _functionCalls.Count > 0;
            public string TextContent => TextBuffer.ToString();

            public FunctionCall UpdateFunctionCall(FunctionCall functionCallDelta)
            {
                var index = functionCallDelta.Index;
                var streamKey = GetStreamKey(functionCallDelta);
                var hasExplicitIndex = HasExplicitIndex(functionCallDelta);
                if (!string.IsNullOrEmpty(streamKey) &&
                    _functionCallIndexesByStreamKey.TryGetValue(streamKey, out var streamKeyIndex))
                {
                    index = streamKeyIndex;
                }
                else if (hasExplicitIndex)
                {
                    index = functionCallDelta.Index;
                }
                else if (!string.IsNullOrEmpty(functionCallDelta.Id) &&
                    _functionCallIndexesById.TryGetValue(functionCallDelta.Id, out var mappedIndex))
                {
                    index = mappedIndex;
                }
                else if (index < 0)
                {
                    index = GetNextFunctionCallIndex();
                }
                else if (_functionCalls.TryGetValue(index, out var positionalAccumulator) &&
                         RepresentsDifferentCall(positionalAccumulator.Call, functionCallDelta))
                {
                    // Some compatible endpoints omit indexes and emit one newly identified call
                    // per SSE event. The per-event array position then resets to zero; do not merge
                    // a new provider ID into the preceding call merely because both used position 0.
                    index = GetNextFunctionCallIndex();
                }

                if (!_functionCalls.TryGetValue(index, out var accumulator))
                {
                    accumulator = new FunctionCallAccumulator(index, functionCallDelta.Source);
                    _functionCalls[index] = accumulator;
                }

                accumulator.Apply(functionCallDelta);
                if (!string.IsNullOrEmpty(accumulator.Call.Id))
                    _functionCallIndexesById[accumulator.Call.Id] = index;
                if (!string.IsNullOrEmpty(streamKey))
                    _functionCallIndexesByStreamKey[streamKey] = index;

                return accumulator.Call;
            }

            private static string? GetStreamKey(FunctionCall functionCall)
            {
                if (functionCall.Metadata?.TryGetValue(StreamItemIdMetadataKey, out var value) == true)
                    return value?.ToString();

                return null;
            }

            private static bool HasExplicitIndex(FunctionCall functionCall)
            {
                return functionCall.Metadata?.TryGetValue(
                           StreamIndexExplicitMetadataKey,
                           out var value) == true &&
                       value is bool hasExplicitIndex &&
                       hasExplicitIndex;
            }

            private int GetNextFunctionCallIndex()
            {
                return _functionCalls.Count == 0 ? 0 : _functionCalls.Keys.Max() + 1;
            }

            private static bool RepresentsDifferentCall(
                FunctionCall current,
                FunctionCall incoming)
            {
                if (!string.IsNullOrEmpty(current.Id) &&
                    !string.IsNullOrEmpty(incoming.Id))
                {
                    return !string.Equals(current.Id, incoming.Id, StringComparison.Ordinal);
                }

                return string.IsNullOrEmpty(incoming.Id) &&
                       !string.IsNullOrEmpty(current.Name) &&
                       !string.IsNullOrEmpty(incoming.Name) &&
                       !string.Equals(current.Name, incoming.Name, StringComparison.Ordinal);
            }

            public IEnumerable<FunctionCall> GetFunctionCalls()
            {
                return _functionCalls.Values.Select(value => value.Call);
            }

            public FunctionCallBatch CreateFunctionCallBatch()
            {
                return new FunctionCallBatch(_functionCalls.Values.Select(value => value.Call))
                {
                    Id = BatchId
                };
            }

            public bool TryFinalizeFunctionArguments(
                out FunctionCall? invalidCall,
                out string error)
            {
                foreach (var accumulator in _functionCalls.Values)
                {
                    if (!accumulator.TryFinalize(out error))
                    {
                        invalidCall = accumulator.Call;
                        return false;
                    }
                }

                invalidCall = null;
                error = string.Empty;
                return true;
            }

            private sealed class FunctionCallAccumulator
            {
                private readonly StringBuilder _arguments = new StringBuilder();
                private bool _argumentsReceived;
                private Dictionary<string, object>? _parsedArguments;

                public FunctionCallAccumulator(int index, IdSource source)
                {
                    Call = new FunctionCall
                    {
                        Index = index,
                        Source = source
                    };
                }

                public FunctionCall Call { get; }

                public void Apply(FunctionCall delta)
                {
                    if (!string.IsNullOrEmpty(Call.Id) &&
                        !string.IsNullOrEmpty(delta.Id) &&
                        !string.Equals(Call.Id, delta.Id, StringComparison.Ordinal))
                    {
                        throw new AIServiceException(
                            $"The provider changed function-call ID '{Call.Id}' to '{delta.Id}' " +
                            $"at index {Call.Index}.");
                    }

                    if (!string.IsNullOrEmpty(Call.Name) &&
                        !string.IsNullOrEmpty(delta.Name) &&
                        !string.Equals(Call.Name, delta.Name, StringComparison.Ordinal))
                    {
                        throw new AIServiceException(
                            $"The provider changed function name '{Call.Name}' to '{delta.Name}' " +
                            $"at index {Call.Index}.");
                    }

                    if (!string.IsNullOrEmpty(delta.Id))
                        Call.Id = delta.Id;
                    if (!string.IsNullOrEmpty(delta.Name))
                        Call.Name = delta.Name;
                    Call.Source = delta.Source;

                    if (delta.Metadata != null)
                    {
                        Call.Metadata ??= new Dictionary<string, object>();
                        foreach (var item in delta.Metadata)
                            Call.Metadata[item.Key] = item.Value;
                    }

                    if (delta.Arguments?.ContainsKey("_missing") == true)
                        return;

                    if (delta.Arguments?.TryGetValue("_complete", out var complete) == true)
                    {
                        _argumentsReceived = true;
                        _arguments.Clear();
                        _arguments.Append(complete?.ToString() ?? string.Empty);
                        _parsedArguments = null;
                    }
                    else if (delta.Arguments?.TryGetValue("_partial", out var partial) == true)
                    {
                        _argumentsReceived = true;
                        var argumentFragment = partial?.ToString() ?? string.Empty;
                        var accumulatedArguments = _arguments.ToString();
                        if (argumentFragment.StartsWith(
                                accumulatedArguments,
                                StringComparison.Ordinal))
                        {
                            // Some compatible endpoints repeat cumulative snapshots rather than
                            // emitting true deltas. Keep the latest identical/extended snapshot.
                            _arguments.Clear();
                            _arguments.Append(argumentFragment);
                        }
                        else
                        {
                            _arguments.Append(argumentFragment);
                        }
                        _parsedArguments = null;
                    }
                    else if (delta.Arguments != null)
                    {
                        _argumentsReceived = true;
                        _arguments.Clear();
                        _parsedArguments = new Dictionary<string, object>(delta.Arguments);
                    }

                }

                public bool TryFinalize(out string error)
                {
                    error = string.Empty;
                    if (Call.Metadata?.TryGetValue(
                            RequiresProviderCallIdMetadataKey,
                            out var requiresProviderIdValue) == true &&
                        requiresProviderIdValue is bool requiresProviderId &&
                        requiresProviderId &&
                        string.IsNullOrWhiteSpace(Call.Id))
                    {
                        error = "The provider completed a function call without a call ID.";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(Call.Id))
                        Call.Id = $"call_{Guid.NewGuid():N}";

                    if (!_argumentsReceived)
                    {
                        error = "Function arguments were not provided as a JSON object.";
                        return false;
                    }

                    if (_parsedArguments != null)
                    {
                        Call.Arguments = _parsedArguments;
                        return true;
                    }

                    var rawArguments = _arguments.ToString();
                    try
                    {
                        using var document = JsonDocument.Parse(rawArguments);
                        if (document.RootElement.ValueKind != JsonValueKind.Object)
                        {
                            error = "Function arguments must be a JSON object.";
                            return false;
                        }

                        Call.Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(rawArguments)
                            ?? new Dictionary<string, object>();
                        return true;
                    }
                    catch (JsonException exception)
                    {
                        error = exception.Message;
                        return false;
                    }
                }
            }
        }

        #endregion
    }
}
