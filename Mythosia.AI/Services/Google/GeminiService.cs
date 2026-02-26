using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TiktokenSharp;

namespace Mythosia.AI.Services.Google
{
    public partial class GeminiService : AIService
    {
        public override AIProvider Provider => AIProvider.Google;

        protected override uint GetModelMaxOutputTokens()
        {
            return 65536;
        }

        /// <summary>
        /// Controls the thinking token budget for Gemini 2.5 models.
        /// Ignored when ThinkingLevel is set (Gemini 3 uses ThinkingLevel instead).
        /// -1: Dynamic (model decides automatically, default)
        /// 0: Disable thinking (Flash/Lite only, Pro minimum is 128)
        /// 128~32768: Specific token budget (Pro max: 32768, Flash/Lite max: 24576)
        /// </summary>
        public int ThinkingBudget { get; set; } = -1;

        /// <summary>
        /// Controls the thinking level for Gemini 3 models.
        /// Auto: Uses model default (High for Gemini 3).
        /// Gemini 3 Pro/Flash shared levels: Low, High (default)
        /// Gemini 3 Flash additional levels: Minimal, Medium
        /// Note: Do not set both ThinkingLevel and ThinkingBudget.
        /// </summary>
        public GeminiThinkingLevel ThinkingLevel { get; set; } = GeminiThinkingLevel.Auto;

        public GeminiService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://generativelanguage.googleapis.com/", httpClient)
        {
            Model = AIModel.Gemini2_5Pro.ToDescription();
            Temperature = 1.0f;
            TopP = 0.8f;
            MaxTokens = 2048;
        }

        #region Model Detection Helpers

        /// <summary>
        /// Returns true if the current model is a Gemini 3 series model.
        /// </summary>
        private bool IsGemini3Model()
        {
            return Model != null && Model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            bool useFunctions = ShouldUseFunctions;

            if (StatelessMode)
                return await ProcessStatelessRequestAsync(message, useFunctions);

            Stream = false;
            ActivateChat.Messages.Add(message);

            var request = useFunctions
                ? CreateFunctionMessageRequest()
                : CreateMessageRequest();

            var responseContent = await SendAndReadAsync(request);

            if (useFunctions)
                return await ProcessFunctionCallLoopAsync(responseContent);

            return AddAssistantResponseWithSignature(responseContent);
        }

        private async Task<string> ProcessFunctionCallLoopAsync(string responseContent)
        {
            var policy = CurrentPolicy ?? DefaultPolicy;
            CurrentPolicy = null;

            for (int round = 0; round < policy.MaxRounds; round++)
            {
                var (content, functionCall, thoughtSignature) = ExtractFunctionCallWithSignature(responseContent);

                if (functionCall == null)
                {
                    AddAssistantMessage(content, thoughtSignature);
                    return content;
                }

                AddFunctionCallMessage(content ?? "", functionCall, thoughtSignature);

                var result = await ProcessFunctionCallAsync(functionCall.Name, functionCall.Arguments);
                AddFunctionResultMessage(result, functionCall);

                var request = CreateFunctionMessageRequest();
                responseContent = await SendAndReadAsync(request);
            }

            return AddAssistantResponseWithSignature(responseContent);
        }

        private string AddAssistantResponseWithSignature(string responseContent)
        {
            var (text, _, sig) = ExtractResponseContentWithSignature(responseContent);
            AddAssistantMessage(text, sig);
            return text;
        }

        private void AddAssistantMessage(string content, string? thoughtSignature)
        {
            var msg = new Message(ActorRole.Assistant, content);
            if (thoughtSignature != null)
            {
                msg.Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.ThoughtSignature] = thoughtSignature
                };
            }
            ActivateChat.Messages.Add(msg);
        }

        private void AddFunctionCallMessage(string content, FunctionCall functionCall, string? thoughtSignature)
        {
            var metadata = new Dictionary<string, object>
            {
                [MessageMetadataKeys.MessageType] = "function_call",
                [MessageMetadataKeys.FunctionId] = functionCall.Id,
                [MessageMetadataKeys.FunctionSource] = functionCall.Source,
                [MessageMetadataKeys.FunctionName] = functionCall.Name,
                [MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(functionCall.Arguments)
            };

            if (thoughtSignature != null)
                metadata[MessageMetadataKeys.ThoughtSignature] = thoughtSignature;

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, content) { Metadata = metadata });
        }

        private void AddFunctionResultMessage(string result, FunctionCall functionCall)
        {
            ActivateChat.Messages.Add(new Message(ActorRole.Function, result)
            {
                Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.MessageType] = "function_result",
                    [MessageMetadataKeys.FunctionId] = functionCall.Id,
                    [MessageMetadataKeys.FunctionSource] = functionCall.Source,
                    [MessageMetadataKeys.FunctionName] = functionCall.Name
                }
            });
        }

        private async Task<string> SendAndReadAsync(HttpRequestMessage request)
        {
            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new AIServiceException(
                    $"Gemini API request failed ({(int)response.StatusCode}): {(string.IsNullOrEmpty(response.ReasonPhrase) ? errorContent : response.ReasonPhrase)}",
                    errorContent);
            }

            return await response.Content.ReadAsStringAsync();
        }

        private async Task<string> ProcessStatelessRequestAsync(Message message, bool useFunctions)
        {
            var tempChat = new ChatBlock
            {
                SystemMessage = ActivateChat.SystemMessage
            };
            tempChat.Messages.Add(message);

            var backup = ActivateChat;
            ActivateChat = tempChat;

            try
            {
                var request = useFunctions
                    ? CreateFunctionMessageRequest()
                    : CreateMessageRequest();

                var responseContent = await SendAndReadAsync(request);

                if (!useFunctions)
                    return ExtractResponseContent(responseContent);

                var (content, functionCall) = ExtractFunctionCall(responseContent);
                if (functionCall == null)
                    return content;

                var result = await ProcessFunctionCallAsync(functionCall.Name, functionCall.Arguments);
                return $"Function result: {result}";
            }
            finally
            {
                ActivateChat = backup;
            }
        }

        #endregion

        #region Request Creation

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var endpoint = Stream
                ? $"v1beta/models/{Model}:streamGenerateContent?alt=sse&key={ApiKey}"
                : $"v1beta/models/{Model}:generateContent?key={ApiKey}";

            var requestBody = BuildRequestBody();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            return new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
        }

        #endregion

        #region Vision Support

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        #endregion

        #region Gemini-Specific Features

        /// <summary>
        /// Downloads an image from URL for Gemini processing
        /// </summary>
        public async Task<Message> CreateMessageWithImageUrl(string prompt, string imageUrl)
        {
            using var imageResponse = await HttpClient.GetAsync(imageUrl);
            if (!imageResponse.IsSuccessStatusCode)
                throw new AIServiceException($"Failed to download image from {imageUrl}");

            var imageData = await imageResponse.Content.ReadAsByteArrayAsync();
            var contentType = imageResponse.Content.Headers.ContentType?.MediaType ?? DefaultImageMimeType;

            return new Message(ActorRole.User, new List<MessageContent>
            {
                new TextContent(prompt),
                new ImageContent(imageData, contentType)
            });
        }

        #endregion

        #region Not Supported Features

        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("Gemini", "Image Generation");
        }

        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("Gemini", "Image Generation");
        }

        #endregion
    }
}