using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
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
    public partial class SonarService
    {
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
            var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"API request failed: {response.ReasonPhrase}", errorContent);
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            var allContent = new StringBuilder();
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                    continue;

                var jsonData = line.Substring("data:".Length).Trim();
                if (jsonData == "[DONE]") break;

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
                    throw new AIServiceException("Failed to parse streaming response", ex.Message);
                }
            }

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, allContent.ToString()));
        }

        private async Task ProcessStatelessStreamAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            var tempChat = new ChatBlock
            {
                SystemMessage = ActivateChat.SystemMessage
            };
            tempChat.Messages.Add(message);

            var backup = ActivateChat;
            ActivateChat = tempChat;
            Stream = true;

            try
            {
                var request = CreateMessageRequest();
                var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new AIServiceException($"API request failed: {response.ReasonPhrase}", errorContent);
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                        continue;

                    var jsonData = line.Substring("data:".Length).Trim();
                    if (jsonData == "[DONE]") break;

                    try
                    {
                        var content = StreamParseJson(jsonData);
                        if (!string.IsNullOrEmpty(content))
                        {
                            await messageReceivedAsync(content);
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore parse errors in stateless mode
                    }
                }
            }
            finally
            {
                ActivateChat = backup;
            }
        }

        // Override the virtual StreamAsync method for better Sonar-specific implementation
        public override async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (StatelessMode)
            {
                await foreach (var content in ProcessStatelessStreamAsync(message, options, cancellationToken))
                {
                    yield return content;
                }
                yield break;
            }

            Stream = true;
            ActivateChat.Messages.Add(message);

            var request = CreateMessageRequest();
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

            await foreach (var content in ProcessSonarStream(response, options, cancellationToken))
            {
                yield return content;
            }
        }

        private async IAsyncEnumerable<StreamingContent> ProcessStatelessStreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var tempChat = new ChatBlock
            {
                SystemMessage = ActivateChat.SystemMessage
            };
            tempChat.Messages.Add(message);

            var backup = ActivateChat;
            ActivateChat = tempChat;
            Stream = true;

            try
            {
                var request = CreateMessageRequest();
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

                await foreach (var content in ProcessSonarStream(response, options, cancellationToken))
                {
                    yield return content;
                }
            }
            finally
            {
                ActivateChat = backup;
            }
        }

        private async IAsyncEnumerable<StreamingContent> ProcessSonarStream(
            HttpResponseMessage response,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            var textBuffer = new StringBuilder();
            string? currentModel = null;

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                    continue;

                var jsonData = line.Substring("data:".Length).Trim();
                if (jsonData == "[DONE]")
                {
                    if (options.IncludeMetadata)
                    {
                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.Completion,
                            Content = null,
                            Metadata = new Dictionary<string, object>
                            {
                                ["total_length"] = textBuffer.Length,
                                ["model"] = currentModel ?? Model
                            }
                        };
                    }
                    break;
                }

                StreamingContent? parsedContent = null;
                try
                {
                    parsedContent = ParseSonarStreamChunk(jsonData, options, ref currentModel);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (parsedContent != null)
                {
                    if (parsedContent.Type == StreamingContentType.Text)
                    {
                        textBuffer.Append(parsedContent.Content);
                    }

                    if (!options.TextOnly || parsedContent.Content != null)
                    {
                        yield return parsedContent;
                    }
                }
            }

            if (textBuffer.Length > 0)
            {
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, textBuffer.ToString()));
            }
        }

        #endregion
    }
}