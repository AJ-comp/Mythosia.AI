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

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
    {
        private const string SseDataPrefix = "data:";
        private const string SseDoneSignal = "[DONE]";

        #region Streaming Implementation

        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            await foreach (var chunk in StreamAsync(message, cancellationToken: default))
            {
                await messageReceivedAsync(chunk);
            }
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options,
            bool useFunctions,
            FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (policy.EnableLogging)
                Console.WriteLine($"[Gemini Stream Round]");

            var request = useFunctions
                ? CreateFunctionMessageRequest(options.IncludeReasoning)
                : CreateMessageRequest(options.IncludeReasoning);

            var response = await HttpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                yield return CreateErrorContent(response);
                yield break;
            }

            var textBuffer = new StringBuilder();
            var functionCalls = new GeminiFunctionCallCollector(IsGemini3Model());
            var streamState = new GeminiStreamState();

            await foreach (var content in ReadGeminiStreamChunks(
                response, options, functionCalls, streamState, cancellationToken))
            {
                if (content.Type == StreamingContentType.Text)
                    textBuffer.Append(content.Content);

                yield return content;
            }

            if (!streamState.IsSuccessful)
                yield break;

            if (functionCalls.Calls.Count > 0 && useFunctions)
            {
                var batch = functionCalls.ToBatch();
                FunctionCallResultBatch? results = null;
                AIServiceException? batchException = null;

                try
                {
                    results = await ProcessFunctionCallsAsync(
                        batch,
                        policy,
                        cancellationToken);
                    AddFunctionCallBatchToHistory(textBuffer.ToString(), batch);
                }
                catch (AIServiceException exception)
                {
                    batchException = exception;
                }

                if (batchException != null)
                {
                    yield return CreateGeminiStreamError(
                        "Gemini emitted an invalid function-call batch; no functions were executed.",
                        "malformed_function_call",
                        batchException.Message);
                    yield break;
                }

                AddFunctionResultBatchToHistory(results!);

                foreach (var result in results!.Results)
                {
                    var resultContent = new StreamingContent
                    {
                        Type = StreamingContentType.FunctionResult,
                        Content = result.Content,
                        FunctionResult = result,
                        FunctionCallBatchId = batch.Id
                    };

                    if (options.IncludeMetadata)
                    {
                        resultContent.Metadata = new Dictionary<string, object>
                        {
                            ["function_calling"] = false,
                            ["function_name"] = result.Call.Name,
                            ["function_index"] = result.Call.Index,
                            ["status"] = result.IsError ? "error" : "completed",
                            ["result"] = result.Content
                        };
                    }

                    yield return resultContent;
                }
            }
            else
            {
                // A successful no-tool terminal response is still an assistant turn even when
                // Gemini emits only thought parts and no visible text. The non-streaming path
                // already records that empty terminal turn; streaming must preserve the same
                // conversation state so the history cannot end on a stale function result.
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, textBuffer.ToString()));
            }
        }

        // StatelessMode is now handled directly in StreamAsync via ChatBlock backup/restore.

        /// <summary>
        /// Reads a single Gemini SSE stream and yields parsed chunks.
        /// Does not execute functions — the caller handles function execution in the round loop.
        /// </summary>
        private async IAsyncEnumerable<StreamingContent> ReadGeminiStreamChunks(
            HttpResponseMessage response,
            StreamOptions options,
            GeminiFunctionCallCollector functionCalls,
            GeminiStreamState streamState,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            TokenUsage? lastUsage = null;
            var diagnostics = new StreamDiagnostics();

            await foreach (var line in ReadSseLinesAsync(response, diagnostics, cancellationToken))
            {
                if (!TryExtractSseData(line, out var jsonData))
                    continue;

                if (jsonData == SseDoneSignal)
                    break;

                diagnostics.DataLinesProcessed++;

                IReadOnlyList<StreamingContent>? parsedContents = null;
                StreamingContent? envelopeError = null;
                Exception? parseException = null;
                try
                {
                    using var document = JsonDocument.Parse(jsonData);
                    envelopeError = InspectGeminiStreamEnvelope(document.RootElement, streamState);
                    if (envelopeError == null)
                        parsedContents = ParseGeminiStreamChunk(jsonData, options, functionCalls);
                }
                catch (Exception exception) when (
                    exception is JsonException ||
                    exception is InvalidOperationException ||
                    exception is KeyNotFoundException)
                {
                    parseException = exception;
                }

                if (envelopeError != null)
                {
                    yield return envelopeError;
                    yield break;
                }

                if (parseException != null)
                {
                    diagnostics.ParseFailures++;
                    streamState.Failed = true;
                    yield return CreateGeminiStreamError(
                        "Gemini emitted a malformed streaming response; the partial stream was not saved.",
                        "malformed_stream",
                        parseException.Message);
                    yield break;
                }

                if (parsedContents == null)
                    continue;

                foreach (var parsedContent in parsedContents)
                {
                    if (parsedContent.Usage != null)
                        lastUsage = CopyTokenUsage(parsedContent.Usage);

                    if (parsedContent.Type == StreamingContentType.FunctionCall)
                    {
                        yield return parsedContent;
                    }
                    else if (parsedContent.Type == StreamingContentType.Text)
                    {
                        if (!options.TextOnly || parsedContent.Content != null)
                            yield return parsedContent;
                    }
                    else if (parsedContent.Type == StreamingContentType.Reasoning)
                    {
                        if (!options.TextOnly)
                            yield return parsedContent;
                    }
                    else if (!options.TextOnly && (options.IncludeMetadata || parsedContent.Usage != null))
                    {
                        yield return parsedContent;
                    }
                }
            }

            if (streamState.Failed)
                yield break;

            if (!streamState.TerminalSeen)
            {
                streamState.Failed = true;
                yield return CreateGeminiStreamError(
                    "Gemini stream ended before a successful terminal finish reason was received; the partial stream was not saved.",
                    "incomplete_stream");
                yield break;
            }

            if (!options.TextOnly)
            {
                var completionContent = new StreamingContent
                {
                    Type = StreamingContentType.Completion,
                    Usage = lastUsage
                };
                if (options.IncludeMetadata)
                {
                    completionContent.Metadata = new Dictionary<string, object>
                    {
                        ["finish_reason"] = streamState.FinishReason ?? SuccessfulFinishReason
                    };
                }
                yield return completionContent;
            }
        }

        private sealed class GeminiFunctionCallCollector
        {
            private readonly bool _requireProviderCallId;
            private readonly List<FunctionCall> _calls = new List<FunctionCall>();
            private readonly Dictionary<string, FunctionCall> _callsById =
                new Dictionary<string, FunctionCall>(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _rawPartsById =
                new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _chunkById =
                new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<JsonElement> _responseParts = new List<JsonElement>();
            private int _chunkSequence;

            public string BatchId { get; } = Guid.NewGuid().ToString();

            public IReadOnlyList<FunctionCall> Calls => _calls;

            public GeminiFunctionCallCollector(bool requireProviderCallId)
            {
                _requireProviderCallId = requireProviderCallId;
            }

            public void BeginChunk()
            {
                _chunkSequence++;
            }

            public bool TryCollectPart(JsonElement part, out FunctionCall? functionCall)
            {
                functionCall = null;
                if (!part.TryGetProperty("functionCall", out _))
                {
                    _responseParts.Add(part.Clone());
                    return false;
                }

                var parsedCall = ParseGeminiFunctionCallPart(
                    part,
                    _calls.Count,
                    _responseParts.Count,
                    _requireProviderCallId);

                if (_callsById.TryGetValue(parsedCall.Id, out var existingCall))
                {
                    var rawPart = part.GetRawText();
                    if (_chunkById[parsedCall.Id] != _chunkSequence &&
                        string.Equals(_rawPartsById[parsedCall.Id], rawPart, StringComparison.Ordinal))
                    {
                        // Defensive idempotency for a transport/provider retry of an already
                        // complete snapshot. Two parts in one envelope still mean two calls and
                        // must not collapse into one execution.
                        return true;
                    }

                    throw new InvalidOperationException(
                        $"Gemini emitted duplicate function-call ID '{existingCall.Id}'.");
                }

                _responseParts.Add(part.Clone());
                _calls.Add(parsedCall);
                _callsById.Add(parsedCall.Id, parsedCall);
                _rawPartsById.Add(parsedCall.Id, part.GetRawText());
                _chunkById.Add(parsedCall.Id, _chunkSequence);
                functionCall = parsedCall;
                return true;
            }

            public FunctionCallBatch ToBatch()
            {
                var batch = new FunctionCallBatch(_calls)
                {
                    Id = BatchId
                };

                if (_calls.Count > 0)
                {
                    batch.Metadata = new Dictionary<string, object>
                    {
                        [GeminiResponsePartsMetadataKey] = new List<JsonElement>(_responseParts)
                    };
                }

                return batch;
            }
        }

        #endregion

        #region SSE Helpers

        private static bool TryExtractSseData(string? line, out string jsonData)
        {
            jsonData = string.Empty;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(SseDataPrefix))
                return false;

            jsonData = line.Substring(SseDataPrefix.Length).Trim();
            return true;
        }

        private static StreamingContent CreateErrorContent(HttpResponseMessage response)
        {
            var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new StreamingContent
            {
                Type = StreamingContentType.Error,
                Content = $"API error ({(int)response.StatusCode}): {error}",
                Metadata = AIHttpErrorFactory.BuildErrorMetadata((int)response.StatusCode, error)
            };
        }

        #endregion
    }
}
