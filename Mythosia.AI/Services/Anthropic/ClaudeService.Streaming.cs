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
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class ClaudeService
    {
        #region Streaming Implementation

        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            if (StatelessMode)
            {
                await ProcessStatelessStreamAsync(message, messageReceivedAsync);
                return;
            }

            ActivateChat.Stream = true;
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
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Claude uses event-stream format
                if (line.StartsWith("event:"))
                {
                    // Skip event type line
                    continue;
                }

                if (!line.StartsWith("data:"))
                    continue;

                var jsonData = line.Substring("data:".Length).Trim();
                if (string.IsNullOrEmpty(jsonData))
                    continue;

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
                    // If there's content so far, save it
                    if (allContent.Length > 0)
                    {
                        ActivateChat.Messages.Add(new Message(ActorRole.Assistant, allContent.ToString()));
                    }
                    throw new AIServiceException("Failed to parse streaming response", ex.Message);
                }
            }

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, allContent.ToString()));
        }

        private async Task ProcessStatelessStreamAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            var tempChat = new ChatBlock(ActivateChat.Model)
            {
                SystemMessage = ActivateChat.SystemMessage,
                Temperature = ActivateChat.Temperature,
                TopP = ActivateChat.TopP,
                MaxTokens = ActivateChat.MaxTokens,
                Stream = true
            };
            tempChat.Messages.Add(message);

            var backup = ActivateChat;
            ActivateChat = tempChat;

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
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (line.StartsWith("event:"))
                        continue;

                    if (!line.StartsWith("data:"))
                        continue;

                    var jsonData = line.Substring("data:".Length).Trim();
                    if (string.IsNullOrEmpty(jsonData))
                        continue;

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

        // Override the new streaming method with options
        public override async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // For now, use the callback-based streaming and convert
            // This can be optimized later to directly handle streaming with metadata
            var channel = Channel.CreateUnbounded<StreamingContent>();

            var streamingTask = Task.Run(async () =>
            {
                try
                {
                    await StreamCompletionAsync(message, async content =>
                    {
                        var streamingContent = new StreamingContent
                        {
                            Type = StreamingContentType.Text,
                            Content = content
                        };

                        if (options.IncludeMetadata)
                        {
                            streamingContent.Metadata = new Dictionary<string, object>
                            {
                                ["provider"] = "Claude",
                                ["timestamp"] = DateTime.UtcNow
                            };
                        }

                        await channel.Writer.WriteAsync(streamingContent, cancellationToken);
                    });
                }
                catch (Exception ex)
                {
                    await channel.Writer.WriteAsync(new StreamingContent
                    {
                        Type = StreamingContentType.Error,
                        Metadata = new Dictionary<string, object> { ["error"] = ex.Message }
                    }, cancellationToken);
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            }, cancellationToken);

            await foreach (var content in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return content;
            }

            await streamingTask;
        }

        #endregion
    }
}