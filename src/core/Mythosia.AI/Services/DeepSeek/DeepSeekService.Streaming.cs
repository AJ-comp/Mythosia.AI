using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
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

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
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
                throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, errorContent);
            }

            var allContent = new StringBuilder();
            var diagnostics = new StreamDiagnostics();
            var options = StreamOptions.TextOnlyOptions;

            await foreach (var line in ReadSseLinesAsync(response, diagnostics, default))
            {
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
                        diagnostics.AccumulatedTextLength += content.Length;
                        diagnostics.DataLinesProcessed++;
                        await messageReceivedAsync(content);
                    }
                }
                catch (JsonException ex)
                {
                    diagnostics.ParseFailures++;
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
                    throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, errorContent);
                }

                var diagnostics = new StreamDiagnostics();
                var options = StreamOptions.TextOnlyOptions;

                await foreach (var line in ReadSseLinesAsync(response, diagnostics, default))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                        continue;

                    var jsonData = line.Substring("data:".Length).Trim();
                    if (jsonData == "[DONE]") break;

                    try
                    {
                        var content = StreamParseJson(jsonData);
                        if (!string.IsNullOrEmpty(content))
                        {
                            diagnostics.AccumulatedTextLength += content.Length;
                            diagnostics.DataLinesProcessed++;
                            await messageReceivedAsync(content);
                        }
                    }
                    catch (JsonException)
                    {
                        diagnostics.ParseFailures++;
                    }
                }
            }
            finally
            {
                ActivateChat = backup;
            }
        }

        /// <summary>
        /// Replaces the base pipeline outright because DeepSeek has no function calling and so needs
        /// no round loop.
        /// <para>
        /// Consequence: context-overflow recovery, which lives in that loop, does not run here. An
        /// overflow surfaces as an error chunk — flagged <c>context_length_exceeded</c> so the caller
        /// can still identify it — but the conversation is not compacted and nothing is re-sent.
        /// </para>
        /// </summary>
        protected override async IAsyncEnumerable<StreamingContent> StreamCoreAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // DeepSeek doesn't support functions yet, so ignore function-related options

            if (StatelessMode)
            {
                await foreach (var content in ProcessStatelessStreamWithOptionsAsync(
                    message, options, cancellationToken))
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
                    Content = $"API error ({(int)response.StatusCode}): {error}",
                    Metadata = AIHttpErrorFactory.BuildErrorMetadata((int)response.StatusCode, error)
                };
                yield break;
            }

            await foreach (var content in ProcessDeepSeekStream(
                response, options, cancellationToken))
            {
                yield return content;
            }
        }

        private async IAsyncEnumerable<StreamingContent> ProcessStatelessStreamWithOptionsAsync(
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
                        Content = $"API error ({(int)response.StatusCode}): {error}",
                        Metadata = AIHttpErrorFactory.BuildErrorMetadata((int)response.StatusCode, error)
                    };
                    yield break;
                }

                await foreach (var content in ProcessDeepSeekStream(
                    response, options, cancellationToken))
                {
                    yield return content;
                }
            }
            finally
            {
                ActivateChat = backup;
            }
        }

        private async IAsyncEnumerable<StreamingContent> ProcessDeepSeekStream(
            HttpResponseMessage response,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var textBuffer = new StringBuilder();
            string? currentModel = null;
            TokenUsage? lastUsage = null;
            var diagnostics = new StreamDiagnostics();

            await foreach (var line in ReadSseLinesAsync(response, diagnostics, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                    continue;

                var jsonData = line.Substring("data:".Length).Trim();
                if (jsonData == "[DONE]")
                {
                    // Stream completed
                    if (!options.TextOnly)
                    {
                        if (lastUsage != null)
                            yield return CreateRoundUsageContent(1, isFinalRound: true, lastUsage);

                        var completionContent = new StreamingContent
                        {
                            Type = StreamingContentType.Completion
                        };
                        if (options.IncludeMetadata)
                        {
                            completionContent.Metadata = new Dictionary<string, object>
                            {
                                ["total_length"] = textBuffer.Length,
                                ["model"] = currentModel ?? Model
                            };
                        }
                        if (lastUsage != null)
                            completionContent.Usage = lastUsage;
                        yield return completionContent;
                    }
                    break;
                }

                StreamingContent? parsedContent = null;
                try
                {
                    parsedContent = ParseDeepSeekStreamChunk(jsonData, options, ref currentModel);
                }
                catch (JsonException)
                {
                    continue; // Skip malformed chunks
                }

                if (parsedContent == null)
                    continue;

                if (parsedContent.Usage != null)
                    lastUsage = parsedContent.Usage;

                if (parsedContent.Type == StreamingContentType.Text)
                {
                    textBuffer.Append(parsedContent.Content);

                    if (!options.TextOnly || parsedContent.Content != null)
                    {
                        yield return parsedContent;
                    }
                }
                else if (options.IncludeMetadata)
                {
                    yield return parsedContent;
                }
            }

            // Save completed message to history
            if (textBuffer.Length > 0)
            {
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, textBuffer.ToString()));
            }
        }

        #endregion
    }
}
