using Mythosia.AI.Exceptions;
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

namespace Mythosia.AI.Services.Perplexity
{
    public partial class PerplexityService
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
            ValidateAgentClientToolSelection();
            var previousAnchor = _agentRequestMessageId;
            _agentRequestMessageId = message.Id;
            try
            {
                await foreach (var item in base.StreamCoreAsync(message, options, cancellationToken)) yield return item;
            }
            finally { _agentRequestMessageId = previousAnchor; }
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options, bool useFunctions, FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            useFunctions = useFunctions && RequestFunctionCallMode != FunctionCallMode.None;
            using var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await ReadAgentBodyAsync(response, cancellationToken).ConfigureAwait(false);
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Error,
                    Content = $"Perplexity Agent API error ({(int)response.StatusCode}): {body}",
                    Metadata = AIHttpErrorFactory.BuildErrorMetadata((int)response.StatusCode, body)
                };
                yield break;
            }

            var text = new StringBuilder();
            var reasoning = new StringBuilder();
            ParsedAgentResponse? completed = null;
            var diagnostics = new StreamDiagnostics();
            await foreach (var line in ReadSseLinesAsync(response, diagnostics, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var json = line.Substring(5).Trim();
                if (json == "[DONE]") break;
                StreamingContent? chunk = null;
                Exception? failure = null;
                try
                {
                    using var document = JsonDocument.Parse(json);
                    var root = document.RootElement;
                    var type = ReadAgentString(root, "type");
                    diagnostics.DataLinesProcessed++;
                    if (type == "response.completed")
                    {
                        if (!root.TryGetProperty("response", out var terminal)) throw new AIServiceException("Agent completion event is missing the response.");
                        completed = ParseAgentResponse(terminal.GetRawText());
                        ValidateAgentFunctionBatch(completed.Calls, useFunctions);
                        if (!completed.Text.StartsWith(text.ToString(), StringComparison.Ordinal))
                            throw new AIServiceException("Agent terminal text does not match its streamed text.");
                    }
                    else if (type == "error" || type == "response.failed" || type == "response.incomplete" || type == "response.cancelled")
                        throw new AIServiceException($"Perplexity Agent stream ended with {type}.", json);
                    else if (type == "response.output_text.delta")
                    {
                        var delta = ReadAgentString(root, "delta") ?? string.Empty;
                        text.Append(delta);
                        diagnostics.AccumulatedTextLength += delta.Length;
                        if (delta.Length > 0) chunk = new StreamingContent { Type = StreamingContentType.Text, Content = delta };
                    }
                    else if (type == "response.reasoning_summary_text.delta" || type == "response.reasoning_text.delta")
                    {
                        var delta = ReadAgentString(root, "delta") ?? string.Empty;
                        reasoning.Append(delta);
                        if (options.IncludeReasoning && delta.Length > 0)
                            chunk = new StreamingContent { Type = StreamingContentType.Reasoning, Content = delta };
                    }
                    else if (options.IncludeMetadata && (type == "response.created" || type == "response.in_progress" ||
                        type == "response.output_item.added" || type == "response.output_item.done"))
                    {
                        chunk = new StreamingContent { Type = StreamingContentType.Status, Metadata = new Dictionary<string, object> { ["status"] = type! } };
                        if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
                        {
                            var itemType = ReadAgentString(item, "type");
                            if (itemType != null) chunk.Metadata["item_type"] = itemType;
                        }
                    }
                }
                catch (Exception exception) when (exception is JsonException || exception is AIServiceException || exception is InvalidOperationException || exception is OverflowException)
                {
                    diagnostics.ParseFailures++;
                    failure = exception;
                }
                if (failure != null)
                {
                    yield return AgentStreamError(failure.Message);
                    yield break;
                }
                if (chunk != null) yield return chunk;
                if (completed != null) break;
            }
            if (completed == null)
            {
                yield return AgentStreamError("Perplexity Agent stream ended before response.completed. No incomplete assistant message or tool calls were saved.");
                yield break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (completed.Text.Length > text.Length)
                yield return new StreamingContent { Type = StreamingContentType.Text, Content = completed.Text.Substring(text.Length) };
            if (options.IncludeReasoning && completed.Reasoning.StartsWith(reasoning.ToString(), StringComparison.Ordinal) &&
                completed.Reasoning.Length > reasoning.Length)
                yield return new StreamingContent { Type = StreamingContentType.Reasoning, Content = completed.Reasoning.Substring(reasoning.Length) };
            foreach (var citation in completed.Citations)
            {
                RecordCitation(citation);
                if (!options.TextOnly) yield return new StreamingContent { Type = StreamingContentType.Citation, Citation = citation.Clone() };
            }
            LastResponseId = completed.Id;
            if (completed.Calls.Calls.Count > 0)
            {
                if (options.IncludeFunctionCalls)
                    foreach (var call in completed.Calls.Calls)
                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.FunctionCall, FunctionCall = call.Clone(),
                            FunctionCallBatchId = completed.Calls.Id,
                            Metadata = options.IncludeMetadata ? new Dictionary<string, object> { ["function_name"] = call.Name, ["function_index"] = call.Index } : null
                        };
                var batches = await ProcessFunctionBatchForRoundAsync(completed.Text, completed.Calls,
                    CreateAgentOutputMetadata(completed), policy, cancellationToken).ConfigureAwait(false);
                foreach (var batch in batches)
                    foreach (var result in batch.Results)
                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.FunctionResult, FunctionResult = result.Clone(),
                            FunctionCallBatchId = batch.FunctionCallBatchId, Content = result.Content
                        };
            }
            else
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, completed.Text) { Metadata = CreateAgentOutputMetadata(completed) });

            // The shared round loop aggregates usage independently of metadata visibility.
            yield return new StreamingContent
            {
                Type = StreamingContentType.Completion,
                Usage = completed.Usage,
                ResponseModel = completed.Model,
                RawFinishReason = completed.Status,
                FinishReason = MapFinishReason(completed.Status)
            };
        }

        private static StreamingContent AgentStreamError(string message) => new StreamingContent
        {
            Type = StreamingContentType.Error, Content = message,
            Metadata = new Dictionary<string, object> { ["status"] = "invalid_agent_response" }
        };

        internal static async Task<string> ReadAgentBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
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
                throw new OperationCanceledException("Agent response reading was canceled.", exception, cancellationToken);
            }
        }
    }
}
