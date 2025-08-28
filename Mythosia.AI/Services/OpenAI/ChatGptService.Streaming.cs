using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class ChatGptService
    {
        #region Streaming Implementation

        // override 키워드 제거 - virtual 메서드를 재정의
        public override async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Check if functions are available and should be used
            bool useFunctions = options.IncludeFunctionCalls &&
                               ActivateChat.ShouldUseFunctions &&
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

            // Add message to history and create request
            ActivateChat.Stream = true;
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
                var error = await response.Content.ReadAsStringAsync();
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Error,
                    Content = null,
                    Metadata = new Dictionary<string, object>
                    {
                        ["error"] = error,
                        ["status_code"] = (int)response.StatusCode
                    }
                };
                yield break;
            }

            // Process streaming response
            await foreach (var content in ProcessOpenAIStream(
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
            var tempChat = new ChatBlock(ActivateChat.Model)
            {
                SystemMessage = ActivateChat.SystemMessage,
                Temperature = ActivateChat.Temperature,
                TopP = ActivateChat.TopP,
                MaxTokens = ActivateChat.MaxTokens,
                Stream = true,
                Functions = useFunctions ? ActivateChat.Functions : new List<FunctionDefinition>(),
                EnableFunctions = useFunctions
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
                    var error = await response.Content.ReadAsStringAsync();
                    yield return new StreamingContent
                    {
                        Type = StreamingContentType.Error,
                        Content = null,
                        Metadata = new Dictionary<string, object>
                        {
                            ["error"] = error,
                            ["status_code"] = (int)response.StatusCode
                        }
                    };
                    yield break;
                }

                await foreach (var content in ProcessOpenAIStream(
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

        private async IAsyncEnumerable<StreamingContent> ProcessOpenAIStream(
           HttpResponseMessage response,
           StreamOptions options,
           bool functionsEnabled,
           [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            var textBuffer = new StringBuilder();
            var functionCallData = new FunctionCallData();
            string? currentModel = null;
            string? responseId = null;

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                    continue;

                var jsonData = line.Substring("data:".Length).Trim();
                if (jsonData == "[DONE]")
                {
                    // Stream completed
                    if (options.IncludeMetadata)
                    {
                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.Completion,
                            Content = null,
                            Metadata = new Dictionary<string, object>
                            {
                                ["total_length"] = textBuffer.Length,
                                ["model"] = currentModel ?? ActivateChat.Model
                            }
                        };
                    }
                    break;
                }

                StreamingContent? parsedContent = null;
                try
                {
                    parsedContent = ParseOpenAIStreamChunk(
                        jsonData,
                        options,
                        functionCallData,
                        ref currentModel,
                        ref responseId);
                }
                catch (JsonException)
                {
                    continue; // Skip malformed chunks
                }

                if (parsedContent == null)
                    continue;

                // Handle different content types
                if (parsedContent.Type == StreamingContentType.Text)
                {
                    // Regular text content
                    textBuffer.Append(parsedContent.Content);

                    if (!options.TextOnly || parsedContent.Content != null)
                    {
                        yield return parsedContent;
                    }
                }
                else if (parsedContent.Type == StreamingContentType.Completion)
                {
                    // Completion event (from response.done)
                    if (options.IncludeMetadata)
                    {
                        // Ensure metadata exists
                        if (parsedContent.Metadata == null)
                            parsedContent.Metadata = new Dictionary<string, object>();

                        // Add total_length if not present
                        if (!parsedContent.Metadata.ContainsKey("total_length"))
                            parsedContent.Metadata["total_length"] = textBuffer.Length;

                        // Add model if available
                        if (currentModel != null && !parsedContent.Metadata.ContainsKey("model"))
                            parsedContent.Metadata["model"] = currentModel;

                        yield return parsedContent;
                    }
                    break; // End the stream
                }
                else if (parsedContent.Type == StreamingContentType.FunctionCall)
                {
                    // Handle function calls
                    if (functionsEnabled)
                    {
                        // Check if function call is complete
                        if (functionCallData.IsComplete && functionCallData.Name != null)
                        {
                            // Execute function
                            var functionResult = await ExecuteFunctionCallAsync(
                                functionCallData,
                                options,
                                cancellationToken);

                            // Yield function result status
                            yield return functionResult;

                            // Add function result to messages
                            ActivateChat.Messages.Add(new Message(ActorRole.Function,
                                functionResult.Metadata?["result"]?.ToString() ?? "")
                            {
                                Metadata = new Dictionary<string, object>
                                {
                                    ["function_name"] = functionCallData.Name
                                }
                            });

                            // Reset for potential next function or response
                            functionCallData = new FunctionCallData();

                            // Request completion based on function result
                            await foreach (var responseContent in StreamAsync(
                                new Message(ActorRole.User, "Please provide a response based on the function result."),
                                options,
                                cancellationToken))
                            {
                                yield return responseContent;
                            }

                            yield break; // End this stream
                        }
                        else if (options.IncludeMetadata)
                        {
                            // Yield function call status
                            yield return parsedContent;
                        }
                    }
                    // If functions not enabled, skip function call chunks
                }
                else if (parsedContent.Type == StreamingContentType.Status)
                {
                    // Status updates
                    if (options.IncludeMetadata)
                    {
                        yield return parsedContent;
                    }
                }
                else if (parsedContent.Type == StreamingContentType.Error)
                {
                    // Error occurred
                    yield return parsedContent;
                    break; // Stop processing on error
                }
                else if (options.IncludeMetadata)
                {
                    // Other metadata-only content
                    yield return parsedContent;
                }
            }

            // Save completed message to history
            if (textBuffer.Length > 0)
            {
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, textBuffer.ToString()));
            }
        }

        // Old callback-based streaming for backward compatibility
        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            var options = StreamOptions.TextOnlyOptions;

            await foreach (var content in StreamAsync(message, options))
            {
                if (content.Type == StreamingContentType.Text && content.Content != null)
                {
                    await messageReceivedAsync(content.Content);
                }
            }
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
                // Parse arguments
                var arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    functionCallData.Arguments.ToString()) ?? new Dictionary<string, object>();

                // Execute function
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
    }
}