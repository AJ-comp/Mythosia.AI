using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Utilities;

namespace Mythosia.AI.Services.Base
{
    public abstract class AIService
    {
        protected readonly string ApiKey;
        protected readonly HttpClient HttpClient;
        protected HashSet<ChatBlock> _chatRequests = new HashSet<ChatBlock>();

        public IReadOnlyCollection<ChatBlock> ChatRequests => _chatRequests;
        public ChatBlock ActivateChat { get; private set; }

        /// <summary>
        /// When true, each request is processed independently without maintaining conversation history
        /// </summary>
        public bool StatelessMode { get; set; } = false;

        /// <summary>
        /// The AI provider for this service
        /// </summary>
        public abstract AIProvider Provider { get; }

        protected AIService(string apiKey, string baseUrl, HttpClient httpClient)
        {
            ApiKey = apiKey;
            HttpClient = httpClient;
            httpClient.BaseAddress = new Uri(baseUrl);
        }

        #region Chat Management

        public void AddNewChat(ChatBlock newChat)
        {
            _chatRequests.Add(newChat);
            ActivateChat = newChat;
        }

        public void SetActivateChat(string chatBlockId)
        {
            var selectedChatBlock = _chatRequests.FirstOrDefault(chat => chat.Id == chatBlockId);
            if (selectedChatBlock != null)
                ActivateChat = selectedChatBlock;
        }

        #endregion

        #region Core Completion Methods

        public virtual async Task<string> GetCompletionAsync(string prompt)
        {
            var message = new Message(ActorRole.User, prompt);
            return await GetCompletionAsync(message);
        }

        public virtual async Task<string> GetCompletionAsync(Message message)
        {
            if (StatelessMode)
            {
                return await ProcessStatelessRequestAsync(message);
            }

            ActivateChat.Stream = false;
            ActivateChat.Messages.Add(message);

            var request = CreateMessageRequest();
            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var result = ExtractResponseContent(responseContent);

                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, result));
                return result;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"API request failed: {response.ReasonPhrase}", errorContent);
            }
        }

        private async Task<string> ProcessStatelessRequestAsync(Message message)
        {
            // Create temporary chat block
            var tempChat = new ChatBlock(ActivateChat.Model)
            {
                SystemMessage = ActivateChat.SystemMessage,
                Temperature = ActivateChat.Temperature,
                TopP = ActivateChat.TopP,
                MaxTokens = ActivateChat.MaxTokens
            };
            tempChat.Messages.Add(message);

            // Backup and swap
            var backup = ActivateChat;
            ActivateChat = tempChat;

            try
            {
                var request = CreateMessageRequest();
                var response = await HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return ExtractResponseContent(responseContent);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new AIServiceException($"API request failed: {response.ReasonPhrase}", errorContent);
                }
            }
            finally
            {
                ActivateChat = backup;
            }
        }

        #endregion

        #region Convenience Methods

        public virtual async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            var mimeType = MimeTypes.GetFromPath(imagePath);

            var message = new Message(ActorRole.User, new List<MessageContent>
            {
                new TextContent(prompt),
                new ImageContent(imageBytes, mimeType)
            });

            return await GetCompletionAsync(message);
        }

        public virtual async Task<string> GetCompletionWithImageUrlAsync(string prompt, string imageUrl)
        {
            var message = new Message(ActorRole.User, new List<MessageContent>
            {
                new TextContent(prompt),
                new ImageContent(imageUrl)
            });

            return await GetCompletionAsync(message);
        }

        #endregion

        #region Streaming Methods (Callback-based)

        public virtual async Task StreamCompletionAsync(string prompt, Action<string> messageReceived)
        {
            await StreamCompletionAsync(prompt, content =>
            {
                messageReceived(content);
                return Task.CompletedTask;
            });
        }

        public virtual async Task StreamCompletionAsync(string prompt, Func<string, Task> messageReceivedAsync)
        {
            var message = new Message(ActorRole.User, prompt);
            await StreamCompletionAsync(message, messageReceivedAsync);
        }

        public virtual async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
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
            using var reader = new System.IO.StreamReader(stream);

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
                using var reader = new System.IO.StreamReader(stream);

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

        #endregion

        #region IAsyncEnumerable Streaming Methods

        /// <summary>
        /// Streams completion as IAsyncEnumerable
        /// </summary>
        public async IAsyncEnumerable<string> StreamAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var message = new Message(ActorRole.User, prompt);
            await foreach (var chunk in StreamAsync(message, cancellationToken))
            {
                yield return chunk;
            }
        }

        /// <summary>
        /// Streams completion as IAsyncEnumerable
        /// </summary>
        public async IAsyncEnumerable<string> StreamAsync(
            Message message,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Create a channel for communication
            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

            // Start the streaming task
            var streamingTask = Task.Run(async () =>
            {
                try
                {
                    await StreamCompletionAsync(message, async content =>
                    {
                        await channel.Writer.WriteAsync(content, cancellationToken);
                    });
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                    return;
                }

                channel.Writer.TryComplete();
            }, cancellationToken);

            // Read from the channel
            try
            {
                await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return chunk;
                }
            }
            finally
            {
                // Ensure the streaming task completes
                try
                {
                    await streamingTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelled
                }
            }
        }

        /// <summary>
        /// Streams as one-off query without affecting conversation history
        /// </summary>
        public async IAsyncEnumerable<string> StreamOnceAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var message = new Message(ActorRole.User, prompt);
            await foreach (var chunk in StreamOnceAsync(message, cancellationToken))
            {
                yield return chunk;
            }
        }

        /// <summary>
        /// Streams as one-off query without affecting conversation history
        /// </summary>
        public async IAsyncEnumerable<string> StreamOnceAsync(
            Message message,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var originalMode = StatelessMode;
            StatelessMode = true;

            try
            {
                await foreach (var chunk in StreamAsync(message, cancellationToken))
                {
                    yield return chunk;
                }
            }
            finally
            {
                StatelessMode = originalMode;
            }
        }

        #endregion

        #region Static Quick Methods

        public static async Task<string> QuickAskAsync(string apiKey, string prompt, AIModel model = AIModel.Gpt4oMini)
        {
            using var httpClient = new HttpClient();
            var service = CreateService(model, apiKey, httpClient);
            service.StatelessMode = true;
            return await service.GetCompletionAsync(prompt);
        }

        public static async Task<string> QuickAskWithImageAsync(
            string apiKey,
            string prompt,
            string imagePath,
            AIModel model = AIModel.Gpt4Vision)
        {
            using var httpClient = new HttpClient();
            var service = CreateService(model, apiKey, httpClient);
            service.StatelessMode = true;
            return await service.GetCompletionWithImageAsync(prompt, imagePath);
        }

        private static AIService CreateService(AIModel model, string apiKey, HttpClient httpClient)
        {
            var provider = GetProviderFromModel(model);
            return provider switch
            {
                AIProvider.OpenAI => new ChatGptService(apiKey, httpClient),
                AIProvider.Anthropic => new ClaudeService(apiKey, httpClient),
                AIProvider.Google => new GeminiService(apiKey, httpClient),
                AIProvider.DeepSeek => new DeepSeekService(apiKey, httpClient),
                AIProvider.Perplexity => new SonarService(apiKey, httpClient),
                _ => throw new NotSupportedException($"Provider {provider} not supported")
            };
        }

        private static AIProvider GetProviderFromModel(AIModel model)
        {
            var modelName = model.ToString();
            if (modelName.StartsWith("Claude")) return AIProvider.Anthropic;
            if (modelName.StartsWith("Gpt") || modelName.StartsWith("GPT")) return AIProvider.OpenAI;
            if (modelName.StartsWith("Gemini")) return AIProvider.Google;
            if (modelName.StartsWith("DeepSeek")) return AIProvider.DeepSeek;
            if (modelName.StartsWith("Perplexity")) return AIProvider.Perplexity;

            throw new ArgumentException($"Cannot determine provider for model {model}");
        }

        #endregion

        #region Abstract Methods

        /// <summary>
        /// Creates the HTTP request message for the AI service
        /// </summary>
        protected abstract HttpRequestMessage CreateMessageRequest();

        /// <summary>
        /// Extracts the response content from the API response
        /// </summary>
        protected abstract string ExtractResponseContent(string responseContent);

        /// <summary>
        /// Parses streaming JSON data
        /// </summary>
        protected abstract string StreamParseJson(string jsonData);

        /// <summary>
        /// Gets the token count for the current conversation
        /// </summary>
        public abstract Task<uint> GetInputTokenCountAsync();

        /// <summary>
        /// Gets the token count for a specific prompt
        /// </summary>
        public abstract Task<uint> GetInputTokenCountAsync(string prompt);

        /// <summary>
        /// Generates an image from a text prompt
        /// </summary>
        public abstract Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024");

        /// <summary>
        /// Generates an image URL from a text prompt
        /// </summary>
        public abstract Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024");

        #endregion
    }
}