using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
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

        public override async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            bool useFunctions = options.IncludeFunctionCalls &&
                               ShouldUseFunctions &&
                               !FunctionsDisabled;

            if (StatelessMode)
            {
                await foreach (var content in ProcessStatelessStreamAsync(
                    message, options, useFunctions, cancellationToken))
                {
                    yield return content;
                }
                yield break;
            }

            Stream = true;
            ActivateChat.Messages.Add(message);

            var request = useFunctions ?
                CreateFunctionMessageRequest() :
                CreateMessageRequest();

            var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                yield return CreateErrorContent(response);
                yield break;
            }

            await foreach (var content in ProcessGeminiStream(
                response, options, useFunctions, cancellationToken))
            {
                yield return content;
            }
        }

        private async IAsyncEnumerable<StreamingContent> ProcessStatelessStreamAsync(
            Message message,
            StreamOptions options,
            bool useFunctions,
            [EnumeratorCancellation] CancellationToken cancellationToken)
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
                var request = useFunctions ?
                    CreateFunctionMessageRequest() :
                    CreateMessageRequest();

                var response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    yield return CreateErrorContent(response);
                    yield break;
                }

                await foreach (var content in ProcessGeminiStream(
                    response, options, useFunctions, cancellationToken))
                {
                    yield return content;
                }
            }
            finally
            {
                ActivateChat = backup;
            }
        }

        private async IAsyncEnumerable<StreamingContent> ProcessGeminiStream(
            HttpResponseMessage response,
            StreamOptions options,
            bool functionsEnabled,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            var textBuffer = new StringBuilder();
            var functionCallData = new FunctionCallData();

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (!TryExtractSseData(line, out var jsonData))
                    continue;

                if (jsonData == SseDoneSignal)
                {
                    if (options.IncludeMetadata)
                        yield return CreateCompletionContent(textBuffer.Length);
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

                    if (functionsEnabled && functionCallData.IsComplete && functionCallData.Name != null)
                    {
                        await foreach (var content in HandleStreamFunctionCall(
                            functionCallData, options, functionsEnabled, cancellationToken))
                        {
                            yield return content;
                        }
                        yield break;
                    }
                }
                else if (parsedContent.Type == StreamingContentType.Text)
                {
                    textBuffer.Append(parsedContent.Content);

                    if (!options.TextOnly || parsedContent.Content != null)
                        yield return parsedContent;
                }
                else if (options.IncludeMetadata)
                {
                    yield return parsedContent;
                }
            }

            if (textBuffer.Length > 0)
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, textBuffer.ToString()));
        }

        private async IAsyncEnumerable<StreamingContent> HandleStreamFunctionCall(
            FunctionCallData functionCallData,
            StreamOptions options,
            bool functionsEnabled,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var argsJson = functionCallData.Arguments.ToString();
            var streamFuncId = Guid.NewGuid().ToString();

            AddStreamFunctionCallMessage(streamFuncId, functionCallData, argsJson);

            var functionResult = await ExecuteFunctionCallAsync(functionCallData, options, cancellationToken);
            yield return functionResult;

            AddStreamFunctionResultMessage(streamFuncId, functionCallData,
                functionResult.Metadata?["result"]?.ToString() ?? "");

            Stream = true;
            var followUpRequest = CreateFunctionMessageRequest();
            var followUpResponse = await HttpClient.SendAsync(
                followUpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!followUpResponse.IsSuccessStatusCode)
                yield break;

            await foreach (var responseContent in ProcessGeminiStream(
                followUpResponse, options, functionsEnabled, cancellationToken))
            {
                yield return responseContent;
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

        private static StreamingContent CreateCompletionContent(int totalLength)
        {
            return new StreamingContent
            {
                Type = StreamingContentType.Completion,
                Content = null,
                Metadata = new Dictionary<string, object>
                {
                    ["total_length"] = totalLength
                }
            };
        }

        private static StreamingContent CreateErrorContent(HttpResponseMessage response)
        {
            return new StreamingContent
            {
                Type = StreamingContentType.Error,
                Content = null,
                Metadata = new Dictionary<string, object>
                {
                    ["error"] = response.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                    ["status_code"] = (int)response.StatusCode
                }
            };
        }

        #endregion
    }
}