using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.IO;
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
        protected OpenAICompatibleService(string apiKey, string baseUrl, HttpClient httpClient)
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

            // 1. Create and send HTTP request. The connection/headers phase is bounded by the
            // resolved request timeout (single control point); the SSE body read below is governed
            // by the caller's token so long legitimate streams are not cut off.
            var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
            HttpResponseMessage response;
            using (var connectCts = CreateRequestTimeoutCts(policy, cancellationToken))
            {
                response = await HttpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, connectCts.Token);
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Error,
                    Content = $"API error ({(int)response.StatusCode}): {error}",
                    Metadata = new Dictionary<string, object> { ["error"] = error }
                };
                yield break;
            }

            // 2. Read stream and yield chunks in real-time
            var streamData = new OpenAIStreamData();
            bool functionCallEventSent = false;
            TokenUsage lastUsage = null;
            Dictionary<string, object> completionMetadata = null;
            var diagnostics = new StreamDiagnostics();

            await foreach (var line in ReadSseLinesAsync(response, diagnostics, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                    continue;

                var jsonData = line.Substring("data:".Length).Trim();
                if (jsonData == "[DONE]")
                {
                    if (!options.TextOnly)
                    {
                        var completionContent = new StreamingContent
                        {
                            Type = StreamingContentType.Completion
                        };
                        if (options.IncludeMetadata)
                        {
                            var meta = completionMetadata ?? new Dictionary<string, object>();
                            meta["total_length"] = streamData.TextBuffer.Length;
                            meta["model"] = streamData.Model ?? Model;
                            completionContent.Metadata = meta;
                        }
                        if (lastUsage != null)
                            completionContent.Usage = lastUsage;
                        yield return completionContent;
                    }
                    // Clear so the post-loop fallback doesn't fire
                    completionMetadata = null;
                    lastUsage = null;
                    break;
                }

                OpenAIStreamChunk chunk;
                try
                {
                    chunk = ParseStreamChunk(jsonData, options);
                    diagnostics.DataLinesProcessed++;
                }
                catch
                {
                    diagnostics.ParseFailures++;
                    continue;
                }

                if (chunk.Usage != null)
                    lastUsage = chunk.Usage;

                if (chunk.Model != null)
                    streamData.Model = chunk.Model;

                // Provider-specific completion event (e.g., OpenAI response.done)
                // Capture metadata/usage but don't yield — [DONE] will handle it
                if (chunk.IsCompletion)
                {
                    if (chunk.Metadata != null)
                        completionMetadata = chunk.Metadata;
                    continue;
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
                if (chunk.FunctionCall != null)
                {
                    streamData.UpdateFunctionCall(chunk.FunctionCall);

                    if (!functionCallEventSent && options.IncludeFunctionCalls &&
                        streamData.FunctionCall?.Name != null)
                    {
                        functionCallEventSent = true;
                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.FunctionCall,
                            Metadata = new Dictionary<string, object>
                            {
                                ["function_name"] = streamData.FunctionCall.Name,
                                ["status"] = "started"
                            }
                        };
                    }
                }
            }

            // Handle streams that end without [DONE] (e.g., OpenAI Responses API)
            if (!options.TextOnly && (completionMetadata != null || lastUsage != null))
            {
                var completionContent = new StreamingContent
                {
                    Type = StreamingContentType.Completion
                };
                if (options.IncludeMetadata)
                {
                    var meta = completionMetadata ?? new Dictionary<string, object>();
                    meta["total_length"] = streamData.TextBuffer.Length;
                    meta["model"] = streamData.Model ?? Model;
                    completionContent.Metadata = meta;
                }
                if (lastUsage != null)
                    completionContent.Usage = lastUsage;
                yield return completionContent;
            }

            // 3. Save assistant message
            if (streamData.HasContent || streamData.FunctionCall != null)
            {
                var assistantMsg = new Message(ActorRole.Assistant, streamData.TextContent);

                if (streamData.FunctionCall != null)
                {
                    assistantMsg.Metadata = new Dictionary<string, object>
                    {
                        [MessageMetadataKeys.MessageType] = "function_call",
                        [MessageMetadataKeys.FunctionId] = streamData.FunctionCall.Id,
                        [MessageMetadataKeys.FunctionSource] = streamData.FunctionCall.Source,
                        [MessageMetadataKeys.FunctionName] = streamData.FunctionCall.Name,
                        [MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(streamData.FunctionCall.Arguments)
                    };
                }

                ActivateChat.Messages.Add(assistantMsg);
            }

            // 4. Execute function if detected — yield FunctionResult to signal next round
            if (streamData.FunctionCall != null && useFunctions)
            {
                if (policy.EnableLogging)
                    Console.WriteLine($"  Executing function: {streamData.FunctionCall.Name}");

                var functionResult = await ProcessFunctionCallAsync(
                    streamData.FunctionCall.Name,
                    streamData.FunctionCall.Arguments);

                var resultMetadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.MessageType] = "function_result",
                    [MessageMetadataKeys.FunctionId] = streamData.FunctionCall.Id,
                    [MessageMetadataKeys.FunctionSource] = streamData.FunctionCall.Source,
                    [MessageMetadataKeys.FunctionName] = streamData.FunctionCall.Name
                };

                ActivateChat.Messages.Add(new Message(ActorRole.Function, functionResult)
                {
                    Metadata = resultMetadata
                });

                yield return new StreamingContent
                {
                    Type = StreamingContentType.FunctionResult,
                    Metadata = new Dictionary<string, object>
                    {
                        ["function_name"] = streamData.FunctionCall.Name,
                        ["status"] = "completed",
                        ["result"] = functionResult
                    }
                };
            }
        }

        /// <summary>
        /// Parses a single SSE JSON chunk into a provider-neutral stream chunk.
        /// Each provider overrides this to handle its specific JSON format.
        /// </summary>
        protected abstract OpenAIStreamChunk ParseStreamChunk(string jsonData, StreamOptions options);

        /// <summary>
        /// Parses OpenAI-compatible usage JSON (handles both prompt_tokens/input_tokens variants).
        /// </summary>
        protected static TokenUsage ParseOpenAICompatibleUsage(JsonElement usage)
        {
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

            // Cache: OpenAI prompt_tokens_details.cached_tokens
            if (usage.TryGetProperty("prompt_tokens_details", out var promptDetails) &&
                promptDetails.TryGetProperty("cached_tokens", out var cached))
                tokenUsage.CachedInputTokens = cached.GetInt32();

            // Cache: DeepSeek prompt_cache_hit_tokens
            if (usage.TryGetProperty("prompt_cache_hit_tokens", out var cacheHit))
                tokenUsage.CachedInputTokens = cacheHit.GetInt32();

            // Reasoning: OpenAI completion_tokens_details.reasoning_tokens
            if (usage.TryGetProperty("completion_tokens_details", out var completionDetails) &&
                completionDetails.TryGetProperty("reasoning_tokens", out var reasoning))
                tokenUsage.ReasoningTokens = reasoning.GetInt32();

            return tokenUsage;
        }

        #endregion

        #region Helper Classes

        protected class OpenAIStreamChunk
        {
            public string Text { get; set; }
            public string Reasoning { get; set; }
            public bool IsCompletion { get; set; }
            public FunctionCall FunctionCall { get; set; }
            public string Model { get; set; }
            public Dictionary<string, object> Metadata { get; set; }
            public TokenUsage Usage { get; set; }
        }

        protected class OpenAIStreamData
        {
            public StringBuilder TextBuffer { get; } = new StringBuilder();
            public StringBuilder ReasoningBuffer { get; } = new StringBuilder();
            public StringBuilder FunctionArgsBuffer { get; } = new StringBuilder();
            public FunctionCall FunctionCall { get; set; }
            public string Model { get; set; }
            public bool HasContent => TextBuffer.Length > 0;
            public string TextContent => TextBuffer.ToString();

            public void UpdateFunctionCall(FunctionCall fc)
            {
                if (fc == null) return;

                if (!string.IsNullOrEmpty(fc.Name))
                {
                    FunctionCall = fc;
                    FunctionArgsBuffer.Clear();
                }

                if (fc.Arguments?.ContainsKey("_partial") == true)
                {
                    FunctionArgsBuffer.Append(fc.Arguments["_partial"]);

                    var fullArgs = FunctionArgsBuffer.ToString();
                    if (fullArgs.StartsWith("{") && fullArgs.EndsWith("}"))
                    {
                        try
                        {
                            if (FunctionCall != null)
                                FunctionCall.Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(fullArgs);
                        }
                        catch { }
                    }
                }
                else if (fc.Arguments != null)
                {
                    if (FunctionCall == null) FunctionCall = fc;
                    else FunctionCall.Arguments = fc.Arguments;
                }

                if (FunctionCall != null && string.IsNullOrEmpty(FunctionCall.Id))
                {
                    FunctionCall.Id = $"call_{Guid.NewGuid().ToString().Substring(0, 20)}";
                }
            }
        }

        #endregion
    }
}
