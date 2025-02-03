using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TiktokenSharp;

namespace Mythosia.AI
{
    /// <summary>
    /// SonarService is an AIService implementation for Perplexity's Sonar (chat completion) models.
    /// </summary>
    public class SonarService : AIService
    {
        /// <summary>
        /// Constructor for SonarService.
        /// </summary>
        /// <param name="apiKey">The Perplexity API key.</param>
        /// <param name="httpClient">A reusable HttpClient instance.</param>
        public SonarService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.perplexity.ai/", httpClient)
        {
            // Create a new ChatBlock with the desired model from your AIModel enum.
            // For example, we use "sonar" by default here.
            var chatBlock = new ChatBlock(AIModel.PerplexitySonar)
            {
                MaxTokens = 8000 // You can adjust this depending on your usage or model limit.
            };

            // Add the new chat block to the service and set it as active.
            AddNewChat(chatBlock);
        }

        /// <summary>
        /// Creates the HTTP request message to send to the Perplexity /chat/completions endpoint.
        /// </summary>
        /// <returns>An HttpRequestMessage containing the JSON-serialized request body.</returns>
        protected override HttpRequestMessage CreateMessageRequest()
        {
            // 1. Build a messages list, starting with system message (if it's not empty).
            var messageList = new List<object>();

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                messageList.Add(new
                {
                    role = "system",
                    content = ActivateChat.SystemMessage
                });
            }

            // 2. Append the latest user/assistant messages
            messageList.AddRange(
                ActivateChat.GetLatestMessages().Select(m => new
                {
                    role = m.Role.ToDescription(),
                    content = m.Content
                })
            );

            // 3. Construct the request body
            var requestBody = new
            {
                model = ActivateChat.Model,
                messages = messageList,
                stream = ActivateChat.Stream,
                temperature = ActivateChat.Temperature,
                top_p = ActivateChat.TopP,
                // Perplexity docs: frequency_penalty > 1.0 penalizes repeated text.
                frequency_penalty = 1.0f,
                presence_penalty = 0.0f,
                max_tokens = (int)ActivateChat.MaxTokens
            };

            // 4. Create the request message
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };

            // 5. Add headers
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        /// <summary>
        /// Extracts the response content from the final (non-streaming) JSON returned by Perplexity.
        /// </summary>
        /// <param name="responseContent">The response body as a string.</param>
        /// <returns>The assistant's generated text content.</returns>
        protected override string ExtractResponseContent(string responseContent)
        {
            // For the final response (non-SSE), we look at:
            // "choices": [ { "message": { "role": "assistant", "content": "..." } } ]
            using var jsonDoc = JsonDocument.Parse(responseContent);
            var root = jsonDoc.RootElement;

            if (root.TryGetProperty("choices", out JsonElement choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out JsonElement messageObj) &&
                messageObj.TryGetProperty("content", out JsonElement contentElem))
            {
                return contentElem.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// Parses a single JSON line from the streaming response (Server-Sent Events).
        /// </summary>
        /// <param name="jsonData">JSON line payload after the "data:" prefix.</param>
        /// <returns>The appended content from the "delta" field, or empty if not found.</returns>
        protected override string StreamParseJson(string jsonData)
        {
            // Each SSE chunk is expected to have a structure like:
            // { "choices": [ { "delta": { "content": "..." } } ] }
            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonData);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("choices", out JsonElement choicesArray) &&
                    choicesArray.GetArrayLength() > 0 &&
                    choicesArray[0].TryGetProperty("delta", out JsonElement deltaObj) &&
                    deltaObj.TryGetProperty("content", out JsonElement contentElem))
                {
                    return contentElem.GetString() ?? string.Empty;
                }
            }
            catch
            {
                // In case of parsing errors or unexpected SSE data, return empty content.
            }

            return string.Empty;
        }

        /// <summary>
        /// Perplexity currently does not support direct image generation via the chat/completions endpoint.
        /// </summary>
        /// <param name="prompt">Prompt for image generation.</param>
        /// <param name="size">Image size.</param>
        /// <returns>Throws NotSupportedException.</returns>
        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            throw new NotSupportedException("Perplexity Sonar does not support image generation.");
        }

        /// <summary>
        /// Perplexity currently does not support direct image generation via the chat/completions endpoint.
        /// </summary>
        /// <param name="prompt">Prompt for image generation.</param>
        /// <param name="size">Image size.</param>
        /// <returns>Throws NotSupportedException.</returns>
        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            throw new NotSupportedException("Perplexity Sonar does not support image generation.");
        }


        public override async Task<uint> GetInputTokenCountAsync()
        {
            // Note: This is an approximation based on a GPT-4 or GPT-3.5 tokenizer.
            // Perplexity may have a different tokenization logic internally.
            var encoding = TikToken.EncodingForModel("gpt-4o");

            var allMessagesBuilder = new StringBuilder(ActivateChat.SystemMessage).Append('\n');
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                allMessagesBuilder.Append(message.Role).Append('\n')
                                  .Append(message.Content).Append('\n');
            }

            var allMessages = allMessagesBuilder.ToString();
            return (uint)encoding.Encode(allMessages).Count;
        }


        public async override Task<uint> GetInputTokenCountAsync(string prompt)
        {
            var encoding = TikToken.EncodingForModel("gpt-4o");

            return (uint)encoding.Encode(prompt).Count;
        }
    }
}
