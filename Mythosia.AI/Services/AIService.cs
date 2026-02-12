using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        protected readonly string ApiKey;
        protected readonly HttpClient HttpClient;
        protected HashSet<ChatBlock> _chatRequests = new HashSet<ChatBlock>();

        // 기본 정책 (설정 가능)
        public FunctionCallingPolicy DefaultPolicy { get; set; } = FunctionCallingPolicy.Default;

        // 내부에서 사용할 정책 결정
        internal FunctionCallingPolicy CurrentPolicy { get; set; }

        public IReadOnlyCollection<ChatBlock> ChatRequests => _chatRequests;
        public ChatBlock ActivateChat { get; protected set; }

        /// <summary>
        /// When true, each request is processed independently without maintaining conversation history
        /// </summary>
        public bool StatelessMode { get; set; } = false;

        /// <summary>
        /// Quick toggle for function calling (like StatelessMode)
        /// </summary>
        public bool FunctionsDisabled { get; set; } = false;

        /// <summary>
        /// The AI provider for this service
        /// </summary>
        public abstract AIProvider Provider { get; }

        #region Model & Generation Settings

        public string Model { get; protected set; }

        /// <summary>
        /// Convenience property for ActivateChat.SystemMessage
        /// </summary>
        public string SystemMessage
        {
            get => ActivateChat?.SystemMessage ?? string.Empty;
            set { if (ActivateChat != null) ActivateChat.SystemMessage = value; }
        }

        public float TopP { get; set; } = 1.0f;
        public float Temperature { get; set; } = 0.7f;
        public float FrequencyPenalty { get; set; } = 0.0f;
        public float PresencePenalty { get; set; } = 0.0f;
        public uint MaxTokens { get; set; } = 1024;
        public bool Stream { get; set; }
        public uint MaxMessageCount { get; set; } = 20;

        #endregion

        #region Function Settings

        public List<FunctionDefinition> Functions { get; set; } = new List<FunctionDefinition>();
        public bool EnableFunctions { get; set; } = true;
        public FunctionCallMode FunctionCallMode { get; set; } = FunctionCallMode.Auto;
        public string ForceFunctionName { get; set; }
        public bool ShouldUseFunctions => Functions.Count > 0 && EnableFunctions && !FunctionsDisabled;

        #endregion

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

        public void AddNewChat()
        {
            AddNewChat(new ChatBlock());
        }

        public void SetActivateChat(string chatBlockId)
        {
            var selectedChatBlock = _chatRequests.FirstOrDefault(chat => chat.Id == chatBlockId);
            if (selectedChatBlock != null)
                ActivateChat = selectedChatBlock;
        }

        public void ChangeModel(AIModel model)
        {
            Model = model.ToDescription();
        }

        public void ChangeModel(string model)
        {
            Model = model;
        }

        /// <summary>
        /// Gets the latest messages from the active chat up to MaxMessageCount
        /// </summary>
        internal IEnumerable<Message> GetLatestMessages()
        {
            return ActivateChat.Messages.Skip(Math.Max(0, ActivateChat.Messages.Count - (int)MaxMessageCount));
        }

        #endregion

        #region Core Completion Methods

        public virtual async Task<string> GetCompletionAsync(string prompt)
        {
            var message = new Message(ActorRole.User, prompt);
            return await GetCompletionAsync(message);
        }

        public abstract Task<string> GetCompletionAsync(Message message);

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

        public AIService CopyFrom(AIService sourceService)
        {
            if (sourceService == null)
                throw new ArgumentNullException(nameof(sourceService));

            // Copy conversation data (Messages + SystemMessage)
            ActivateChat = sourceService.ActivateChat.Clone();

            // Copy service-level settings (previously carried by ChatBlock.Clone)
            Functions = new List<FunctionDefinition>(sourceService.Functions);
            EnableFunctions = sourceService.EnableFunctions;
            FunctionCallMode = sourceService.FunctionCallMode;
            ForceFunctionName = sourceService.ForceFunctionName;
            DefaultPolicy = sourceService.DefaultPolicy;

            Temperature = sourceService.Temperature;
            TopP = sourceService.TopP;
            MaxTokens = sourceService.MaxTokens;
            FrequencyPenalty = sourceService.FrequencyPenalty;
            PresencePenalty = sourceService.PresencePenalty;
            MaxMessageCount = sourceService.MaxMessageCount;
            Stream = sourceService.Stream;

            StatelessMode = sourceService.StatelessMode;
            FunctionsDisabled = sourceService.FunctionsDisabled;

            return this;
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