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
    public partial class GeminiService
    {
        private const string SseDataPrefix = "data:";
        private const string SseDoneSignal = "[DONE]";

        #region Streaming Implementation

        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            if (StatelessMode)
            {
                await ProcessStatelessStreamAsync(message, messageReceivedAsync);
                return;
            }

            Stream = true;
            ActivateChat.Messages.Add(message);

            var request = CreateMessageRequest();
            var response = await SendStreamingRequestAsync(request);

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            var allContent = new StringBuilder();
            await foreach (var jsonData in ReadSseLines(reader))
            {
                try
                {
                    var content = StreamParseJson(jsonData);
                    if (!string.IsNullOrEmpty(content))
                    {
                        allContent.Append(content);
                        await messageReceivedAsync(content);
                    }
                }
                catch (JsonException ex)
                {
                    ActivateChat.Messages.Add(new Message(ActorRole.Assistant, allContent.ToString()));
                    throw new AIServiceException("Failed to parse Gemini streaming response", ex.Message);
                }
            }

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, allContent.ToString()));
        }

        private async Task ProcessStatelessStreamAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            Stream = true;
            var tempChat = new ChatBlock
            {
                SystemMessage = ActivateChat.SystemMessage
            };
            tempChat.Messages.Add(message);

            var backup = ActivateChat;
            ActivateChat = tempChat;

            try
            {
                var request = CreateMessageRequest();
                var response = await SendStreamingRequestAsync(request);

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                await foreach (var jsonData in ReadSseLines(reader))
                {
                    try
                    {
                        var content = StreamParseJson(jsonData);
                        if (!string.IsNullOrEmpty(content))
                            await messageReceivedAsync(content);
                    }
                    catch (JsonException)
                    {
                    }
                }
            }
            finally
            {
                ActivateChat = backup;
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

            // Read stream and yield chunks in real-time
            var textBuffer = new StringBuilder();
            var functionCallData = new FunctionCallData();

            await foreach (var content in ReadGeminiStreamChunks(
                response, options, functionCallData, cancellationToken))
            {
                if (content.Type == StreamingContentType.Text)
                    textBuffer.Append(content.Content);

                yield return content;
            }

            // Execute function if detected — yield FunctionResult to signal next round
            if (functionCallData.IsComplete && functionCallData.Name != null && useFunctions)
            {
                var funcId = Guid.NewGuid().ToString();
                var argsJson = functionCallData.Arguments.ToString();

                if (policy.EnableLogging)
                    Console.WriteLine($"  Executing function: {functionCallData.Name}");

                AddStreamFunctionCallMessage(funcId, functionCallData, argsJson);

                var functionResult = await ExecuteFunctionCallAsync(
                    functionCallData, options, cancellationToken);
                yield return functionResult;

                AddStreamFunctionResultMessage(funcId, functionCallData,
                    functionResult.Metadata?["result"]?.ToString() ?? "");
            }
            else if (textBuffer.Length > 0)
            {
                // Text-only response — save assistant message
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
            FunctionCallData functionCallData,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (!TryExtractSseData(line, out var jsonData))
                    continue;

                if (jsonData == SseDoneSignal)
                {
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
                                ["total_length"] = 0
                            };
                        }
                        yield return completionContent;
                    }
                    break;
                }

                StreamingContent? parsedContent;
                try
                {
                    parsedContent = ParseGeminiStreamChunk(jsonData, options, functionCallData);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (parsedContent == null)
                    continue;

                if (parsedContent.Type == StreamingContentType.FunctionCall)
                {
                    yield return parsedContent;

                    // Function call detected — stop reading stream, let the round loop handle execution
                    if (functionCallData.IsComplete && functionCallData.Name != null)
                        yield break;
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
                else if (options.IncludeMetadata)
                {
                    yield return parsedContent;
                }
            }
        }

        private void AddStreamFunctionCallMessage(string functionId, FunctionCallData functionCallData, string argsJson)
        {
            var fcMetadata = new Dictionary<string, object>
            {
                [MessageMetadataKeys.MessageType] = "function_call",
                [MessageMetadataKeys.FunctionId] = functionId,
                [MessageMetadataKeys.FunctionSource] = IdSource.Gemini,
                [MessageMetadataKeys.FunctionName] = functionCallData.Name,
                [MessageMetadataKeys.FunctionArguments] = argsJson
            };

            if (functionCallData.ThoughtSignature != null)
                fcMetadata[MessageMetadataKeys.ThoughtSignature] = functionCallData.ThoughtSignature;

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "") { Metadata = fcMetadata });
        }

        private void AddStreamFunctionResultMessage(string functionId, FunctionCallData functionCallData, string result)
        {
            ActivateChat.Messages.Add(new Message(ActorRole.Function, result)
            {
                Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.MessageType] = "function_result",
                    [MessageMetadataKeys.FunctionId] = functionId,
                    [MessageMetadataKeys.FunctionSource] = IdSource.Gemini,
                    [MessageMetadataKeys.FunctionName] = functionCallData.Name
                }
            });
        }

        private async Task<StreamingContent> ExecuteFunctionCallAsync(
            FunctionCallData functionCallData,
            StreamOptions options,
            CancellationToken cancellationToken)
        {
            var content = new StreamingContent
            {
                Type = StreamingContentType.FunctionResult
            };

            try
            {
                var arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    functionCallData.Arguments.ToString()) ?? new Dictionary<string, object>();

                var result = await ProcessFunctionCallAsync(
                    functionCallData.Name ?? "",
                    arguments);

                if (options.IncludeMetadata)
                {
                    content.Metadata = new Dictionary<string, object>
                    {
                        ["function_calling"] = false,
                        ["function_name"] = functionCallData.Name ?? "",
                        ["status"] = "completed",
                        ["result"] = result
                    };
                }
            }
            catch (Exception ex)
            {
                content.Type = StreamingContentType.Error;
                content.Metadata = new Dictionary<string, object>
                {
                    ["function_calling"] = false,
                    ["function_name"] = functionCallData.Name ?? "",
                    ["status"] = "error",
                    ["error"] = ex.Message
                };
            }

            return content;
        }

        #endregion

        #region SSE Helpers

        private async Task<HttpResponseMessage> SendStreamingRequestAsync(HttpRequestMessage request)
        {
            var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new AIServiceException(
                    $"Gemini streaming request failed ({(int)response.StatusCode}): {(string.IsNullOrEmpty(response.ReasonPhrase) ? errorContent : response.ReasonPhrase)}",
                    errorContent);
            }

            return response;
        }

        private static bool TryExtractSseData(string? line, out string jsonData)
        {
            jsonData = string.Empty;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(SseDataPrefix))
                return false;

            jsonData = line.Substring(SseDataPrefix.Length).Trim();
            return true;
        }

        private static async IAsyncEnumerable<string> ReadSseLines(StreamReader reader)
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!TryExtractSseData(line, out var jsonData))
                    continue;

                if (jsonData == SseDoneSignal)
                    yield break;

                yield return jsonData;
            }
        }

        private static StreamingContent CreateErrorContent(HttpResponseMessage response)
        {
            var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new StreamingContent
            {
                Type = StreamingContentType.Error,
                Content = $"API error ({(int)response.StatusCode}): {error}",
                Metadata = new Dictionary<string, object>
                {
                    ["error"] = error,
                    ["status_code"] = (int)response.StatusCode
                }
            };
        }

        #endregion
    }
}