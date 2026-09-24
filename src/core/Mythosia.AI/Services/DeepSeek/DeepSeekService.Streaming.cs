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

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
    {
        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            if (messageReceivedAsync == null) throw new ArgumentNullException(nameof(messageReceivedAsync));
            await foreach (var text in StreamAsync(message))
                await messageReceivedAsync(text).ConfigureAwait(false);
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamCoreAsync(
            Message message, StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ValidateDeepSeekToolSelection();
            var previousAnchor = _deepSeekRequestMessageId;
            _deepSeekRequestMessageId = message.Id;
            try
            {
                await foreach (var item in base.StreamCoreAsync(message, options, cancellationToken))
                    yield return item;
            }
            finally { _deepSeekRequestMessageId = previousAnchor; }
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options, bool useFunctions, FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await ReadDeepSeekErrorBodyAsync(response, cancellationToken).ConfigureAwait(false);
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Error,
                    Content = $"DeepSeek API error ({(int)response.StatusCode}): {body}",
                    Metadata = AIHttpErrorFactory.BuildErrorMetadata((int)response.StatusCode, body)
                };
                yield break;
            }

            var state = new DeepSeekStreamState();
            var diagnostics = new StreamDiagnostics();
            var done = false;
            await foreach (var line in ReadSseLinesAsync(response, diagnostics, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var json = line.Substring(5).Trim();
                if (json == "[DONE]") { done = !RequestUsesResponsesApi; break; }
                List<StreamingContent>? chunks = null;
                Exception? failure = null;
                try
                {
                    chunks = RequestUsesResponsesApi ? ParseDeepSeekResponsesStreamEvent(json, state, options) : ParseDeepSeekStreamEvent(json, state, options);
                    diagnostics.DataLinesProcessed++;
                }
                catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException || exception is KeyNotFoundException || exception is AIServiceException)
                {
                    diagnostics.ParseFailures++;
                    failure = exception;
                }
                if (failure != null)
                {
                    yield return DeepSeekStreamError("malformed_stream", failure.Message);
                    yield break;
                }
                foreach (var chunk in chunks!)
                {
                    if (chunk.Type == StreamingContentType.Text)
                        diagnostics.AccumulatedTextLength += chunk.Content?.Length ?? 0;
                    yield return chunk;
                }
                if (RequestUsesResponsesApi && state.FinishReason != null) { done = true; break; }
            }

            if (!done)
            {
                yield return DeepSeekStreamError("incomplete_stream", "DeepSeek stream ended without its successful terminal event; no incomplete assistant turn or tool batch was saved.");
                yield break;
            }

            FunctionCallBatch? calls = null;
            Exception? terminalFailure = null;
            try
            {
                if (state.FinishReason != "stop" && state.FinishReason != "tool_calls")
                    throw new AIServiceException($"DeepSeek ended the stream with finish_reason={state.FinishReason ?? "missing"}.");
                if (state.Calls.Count > 0)
                {
                    if (!useFunctions || RequestFunctionCallMode == FunctionCallMode.None)
                        throw new AIServiceException("DeepSeek returned tool calls when function execution was disabled.");
                    calls = FinalizeDeepSeekStreamCalls(state);
                    ValidateDeepSeekFunctionBatch(calls);
                }
                else if (state.FinishReason == "tool_calls")
                    throw new AIServiceException("DeepSeek ended with tool_calls but returned no tool-call payload.");
            }
            catch (Exception exception) when (exception is AIServiceException || exception is JsonException || exception is InvalidOperationException)
            {
                terminalFailure = exception;
            }
            if (terminalFailure != null)
            {
                yield return DeepSeekStreamError("invalid_terminal_response", terminalFailure.Message);
                yield break;
            }

            var reasoning = state.HasReasoning ? state.Reasoning.ToString() : null;
            if (calls != null)
            {
                if (options.IncludeFunctionCalls)
                {
                    foreach (var call in calls.Calls)
                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.FunctionCall,
                            FunctionCall = call.Clone(),
                            FunctionCallBatchId = calls.Id,
                            Metadata = new Dictionary<string, object>
                            {
                                ["function_name"] = call.Name, ["function_index"] = call.Index
                            }
                        };
                }
                // Existing execution policies validate the whole batch before starting handlers,
                // then finish all started handlers and retain matching call/result snapshots.
                var batches = await ProcessFunctionBatchForRoundAsync(state.Text.ToString(), calls,
                    CreateReasoningMetadata(reasoning), policy, cancellationToken).ConfigureAwait(false);
                foreach (var batch in batches)
                    foreach (var result in batch.Results)
                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.FunctionResult,
                            FunctionResult = result.Clone(),
                            FunctionCallBatchId = batch.FunctionCallBatchId,
                            Content = result.Content,
                            Metadata = new Dictionary<string, object>
                            {
                                ["function_name"] = result.Call.Name,
                                ["function_index"] = result.Call.Index,
                                ["status"] = result.IsError ? "error" : "completed"
                            }
                        };
            }
            else
            {
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, state.Text.ToString())
                {
                    Metadata = CreateReasoningMetadata(reasoning)
                });
            }

            // The base round loop consumes usage even when callers ask for text only.
            yield return new StreamingContent
            {
                Type = StreamingContentType.Completion,
                Usage = state.Usage,
                ResponseModel = state.Model,
                RawFinishReason = state.FinishReason,
                FinishReason = MapFinishReason(state.FinishReason),
                Metadata = options.IncludeMetadata ? new Dictionary<string, object>
                {
                    ["model"] = state.Model ?? RequestModel, ["finish_reason"] = state.FinishReason!
                } : null
            };
        }

        private static StreamingContent DeepSeekStreamError(string status, string message)
            => new StreamingContent
            {
                Type = StreamingContentType.Error, Content = message,
                Metadata = new Dictionary<string, object> { ["status"] = status }
            };

        private static async Task<string> ReadDeepSeekErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            // netstandard2.1 has no cancellation overload on ReadAsStringAsync. Keep the request
            // token active while reading an error body, and abort its stream on cancellation.
            using var registration = cancellationToken.Register(response.Dispose);
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("DeepSeek error-body reading was canceled.", exception, cancellationToken);
            }
        }

        private sealed class DeepSeekStreamState
        {
            public StringBuilder Text { get; } = new StringBuilder();
            public StringBuilder Reasoning { get; } = new StringBuilder();
            public bool HasReasoning { get; set; }
            public string? FinishReason { get; set; }
            public string? Model { get; set; }
            public TokenUsage? Usage { get; set; }
            public SortedDictionary<int, DeepSeekStreamCall> Calls { get; } = new SortedDictionary<int, DeepSeekStreamCall>();
            public long? ResponseSequence { get; set; }
            public Dictionary<int, DeepSeekStreamCall> ResponseCalls { get; } = new Dictionary<int, DeepSeekStreamCall>();
        }

        private sealed class DeepSeekStreamCall
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public StringBuilder Arguments { get; } = new StringBuilder();
        }

        private static List<StreamingContent> ParseDeepSeekStreamEvent(string json, DeepSeekStreamState state, StreamOptions options)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new AIServiceException("DeepSeek returned a streaming provider error.", error.GetRawText());
            state.Model = ReadDeepSeekString(root, "model") ?? state.Model;
            if (root.TryGetProperty("usage", out var usage)) state.Usage = ParseDeepSeekUsage(usage) ?? state.Usage;
            var output = new List<StreamingContent>();
            if (!root.TryGetProperty("choices", out var choices)) return output;
            if (choices.ValueKind != JsonValueKind.Array) throw new JsonException("DeepSeek choices must be an array.");
            if (choices.GetArrayLength() == 0) return output;
            var choice = choices[0];
            var terminalAlreadyReceived = state.FinishReason != null;
            var finish = ReadDeepSeekString(choice, "finish_reason");
            if (finish != null)
            {
                if (state.FinishReason != null && state.FinishReason != finish)
                    throw new JsonException("DeepSeek changed its terminal finish reason.");
                state.FinishReason = finish;
            }
            if (!choice.TryGetProperty("delta", out var delta)) return output;
            if (delta.ValueKind != JsonValueKind.Object) throw new JsonException("DeepSeek delta must be an object.");
            var reasoning = ReadDeepSeekString(delta, "reasoning_content");
            var text = ReadDeepSeekString(delta, "content");
            if (terminalAlreadyReceived &&
                (!string.IsNullOrEmpty(reasoning) || !string.IsNullOrEmpty(text) ||
                 (delta.TryGetProperty("tool_calls", out var terminalCalls) &&
                  terminalCalls.ValueKind == JsonValueKind.Array && terminalCalls.GetArrayLength() > 0)))
                throw new JsonException("DeepSeek received content after a terminal finish reason.");
            if (reasoning != null)
            {
                state.HasReasoning = true;
                state.Reasoning.Append(reasoning);
                if (options.IncludeReasoning)
                    output.Add(new StreamingContent { Type = StreamingContentType.Reasoning, Content = reasoning });
            }
            if (text != null)
            {
                state.Text.Append(text);
                if (text.Length > 0)
                    output.Add(new StreamingContent { Type = StreamingContentType.Text, Content = text });
            }
            if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind != JsonValueKind.Null)
            {
                if (toolCalls.ValueKind != JsonValueKind.Array) throw new JsonException("DeepSeek tool_calls must be an array.");
                var eventIndexes = new HashSet<int>();
                foreach (var call in toolCalls.EnumerateArray())
                {
                    if (!call.TryGetProperty("index", out var indexValue) || !indexValue.TryGetInt32(out var index) || index < 0)
                        throw new JsonException("DeepSeek streamed tool calls require a nonnegative integer index.");
                    if (!eventIndexes.Add(index)) throw new JsonException("DeepSeek repeated a tool-call index in one event.");
                    if (!state.Calls.TryGetValue(index, out var current))
                    {
                        current = new DeepSeekStreamCall();
                        state.Calls.Add(index, current);
                    }
                    var id = ReadDeepSeekString(call, "id");
                    if (id != null)
                    {
                        if (current.Id != null && current.Id != id) throw new JsonException("DeepSeek changed a streamed tool-call ID.");
                        current.Id = id;
                    }
                    var type = ReadDeepSeekString(call, "type");
                    if (type != null && type != "function") throw new JsonException("DeepSeek returned an unsupported tool type.");
                    if (call.TryGetProperty("function", out var function))
                    {
                        var name = ReadDeepSeekString(function, "name");
                        if (name != null)
                        {
                            if (current.Name != null && current.Name != name) throw new JsonException("DeepSeek changed a streamed function name.");
                            current.Name = name;
                        }
                        var arguments = ReadDeepSeekString(function, "arguments");
                        if (arguments != null) current.Arguments.Append(arguments);
                    }
                }
            }
            return output;
        }

        private FunctionCallBatch FinalizeDeepSeekStreamCalls(DeepSeekStreamState state)
        {
            // Reuse the completed Chat Completions parser to validate IDs, terminal reasons,
            // and object-shaped JSON arguments before any handler can execute.
            var json = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = state.FinishReason,
                        message = new
                        {
                            content = state.Text.ToString(),
                            tool_calls = state.Calls.Values.Select(call => new
                            {
                                id = call.Id, type = "function",
                                function = new { name = call.Name, arguments = call.Arguments.ToString() }
                            }).ToArray()
                        }
                    }
                }
            });
            var batch = ExtractFunctionCalls(json).functionCalls;
            var indexes = state.Calls.Keys.ToArray();
            for (var position = 0; position < batch.Calls.Count; position++)
                batch.Calls[position].Index = indexes[position];
            return batch;
        }
    }
}
