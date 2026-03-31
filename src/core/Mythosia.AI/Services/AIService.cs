using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services;
using Mythosia.AI.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService : IAIService
    {
        protected readonly string ApiKey;
        protected readonly HttpClient HttpClient;
        protected List<ChatBlock> _chatRequests = new List<ChatBlock>();
        private readonly AsyncLocal<AIRequestContext?> _currentRequestContext = new AsyncLocal<AIRequestContext?>();

        public FunctionCallingPolicy DefaultPolicy { get; set; } = FunctionCallingPolicy.Default;

        protected internal FunctionCallingPolicy CurrentPolicy { get; set; }

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
        public abstract string Provider { get; }

        #region Model & Generation Settings

        public string Model { get; protected set; }

        /// <summary>
        /// Convenience property for ActivateChat.SystemMessage
        /// </summary>
        public string SystemMessage
        {
            get => ActivateChat.SystemMessage;
            set => ActivateChat.SystemMessage = value;
        }

        public float TopP { get; set; } = 1.0f;
        public float Temperature { get; set; } = 0.7f;
        public float FrequencyPenalty { get; set; } = 0.0f;
        public float PresencePenalty { get; set; } = 0.0f;
        public uint MaxTokens { get; set; } = 1024;
        public bool Stream { get; set; }
        public uint MaxMessageCount { get; set; } = 20;

        /// <summary>
        /// Returns the maximum output tokens allowed for the current model.
        /// Override in each service to provide model-specific limits.
        /// </summary>
        protected virtual uint GetModelMaxOutputTokens() => uint.MaxValue;

        /// <summary>
        /// Returns the effective max tokens, capped by the current model's limit.
        /// Use this instead of MaxTokens when building request bodies.
        /// </summary>
        protected uint GetEffectiveMaxTokens() => Math.Min(MaxTokens, GetModelMaxOutputTokens());

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
            if (!baseUrl.EndsWith("/"))
                baseUrl += "/";
            httpClient.BaseAddress = new Uri(baseUrl);
            AddNewChat(new ChatBlock());
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

        public void ChangeModel(string model)
        {
            Model = model;
        }

        /// <summary>
        /// Gets the latest messages from the active chat up to MaxMessageCount
        /// </summary>
        protected internal IEnumerable<Message> GetLatestMessages()
        {
            var messages = ActivateChat.Messages
                .Skip(Math.Max(0, ActivateChat.Messages.Count - (int)MaxMessageCount))
                .ToList();

            var requestMessageOverride = _currentRequestContext.Value?.RequestMessageOverride;
            if (requestMessageOverride != null && messages.Count > 0)
            {
                messages[messages.Count - 1] = requestMessageOverride;
            }

            var additionalMessages = _currentRequestContext.Value?.AdditionalMessages;
            if (additionalMessages != null && additionalMessages.Count > 0)
                messages.AddRange(additionalMessages);

            return messages;
        }

        /// <summary>
        /// Ensures the message list starts with a User message.
        /// Some APIs (Gemini, Claude) require conversations to begin with a user turn.
        /// If the first message is not from a user, a synthetic context message is prepended.
        /// </summary>
        protected static void EnsureUserFirstMessage(List<Message> messages)
        {
            if (messages.Count == 0) return;
            if (messages[0].Role == ActorRole.User) return;

            messages.Insert(0, new Message(ActorRole.User, "(Continuing from previous conversation context)"));
        }

        /// <summary>
        /// Gets messages for non-function path, converting function-related messages to plain text.
        /// Original messages in ChatBlock are never modified.
        /// </summary>
        protected internal IEnumerable<Message> GetLatestMessagesWithFunctionFallback()
        {
            foreach (var message in GetLatestMessages())
            {
                if (message.Role == ActorRole.Assistant &&
                    message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
                {
                    var funcName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "unknown";
                    var funcArgs = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";
                    yield return new Message(ActorRole.Assistant, $"[Called {funcName}({funcArgs})]");
                    continue;
                }

                if (message.Role == ActorRole.Function)
                {
                    var funcName = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "function";
                    yield return new Message(ActorRole.User, $"[Function {funcName} returned: {message.Content}]");
                    continue;
                }

                yield return message;
            }
        }

        #endregion

        #region Core Completion Methods

        public virtual async Task<string> GetCompletionAsync(string prompt)
        {
            await ApplySummaryPolicyIfNeededAsync();
            var message = new Message(ActorRole.User, prompt);
            return await GetCompletionAsync(message);
        }

        public virtual async Task<string> GetCompletionAsync(string prompt, AIRequestContext context)
        {
            await ApplySummaryPolicyIfNeededAsync();
            var message = new Message(ActorRole.User, prompt);
            return await GetCompletionAsync(message, context);
        }

        public virtual async Task<string> GetCompletionAsync(string prompt, AIRequestProfile profile)
        {
            await ApplySummaryPolicyIfNeededAsync();
            var message = new Message(ActorRole.User, prompt);
            return await GetCompletionAsync(message, profile);
        }

        public virtual async Task<string> GetCompletionAsync(string prompt, AIRequestProfile profile, AIRequestContext context)
        {
            await ApplySummaryPolicyIfNeededAsync();
            var message = new Message(ActorRole.User, prompt);
            return await GetCompletionAsync(message, profile, context);
        }

        public abstract Task<string> GetCompletionAsync(Message message);

        public virtual async Task<string> GetCompletionAsync(Message message, AIRequestContext context)
        {
            if (context == null)
                return await GetCompletionAsync(message);

            var restoreContext = ApplyRequestContext(context);
            try
            {
                return await GetCompletionAsync(message);
            }
            finally
            {
                restoreContext();
            }
        }

        public virtual async Task<string> GetCompletionAsync(Message message, AIRequestProfile profile)
        {
            if (profile == null)
                return await GetCompletionAsync(message);

            var restore = ApplyRequestProfile(profile);
            try
            {
                return await GetCompletionAsync(message);
            }
            finally
            {
                restore();
            }
        }

        public virtual async Task<string> GetCompletionAsync(Message message, AIRequestProfile profile, AIRequestContext context)
        {
            if (profile == null)
                return await GetCompletionAsync(message, context);

            if (context == null)
                return await GetCompletionAsync(message, profile);

            var restoreProfile = ApplyRequestProfile(profile);
            var restoreContext = ApplyRequestContext(context);
            try
            {
                return await GetCompletionAsync(message);
            }
            finally
            {
                restoreContext();
                restoreProfile();
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

        public AIService CopyFrom(AIService sourceService)
        {
            if (sourceService == null)
                throw new ArgumentNullException(nameof(sourceService));

            ActivateChat = sourceService.ActivateChat.Clone();

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

            if (sourceService.ConversationPolicy != null)
            {
                var sourcePolicy = sourceService.ConversationPolicy;
                ConversationPolicy = new SummaryConversationPolicy
                {
                    TriggerTokens = sourcePolicy.TriggerTokens,
                    TriggerCount = sourcePolicy.TriggerCount,
                    KeepRecentTokens = sourcePolicy.KeepRecentTokens,
                    KeepRecentCount = sourcePolicy.KeepRecentCount,
                    CurrentSummary = sourcePolicy.CurrentSummary
                };
            }
            else
            {
                ConversationPolicy = null;
            }

            return this;
        }

        #endregion

        #region Request Profiles

        protected virtual Action ApplyRequestProfile(AIRequestProfile profile)
        {
            if (profile == null)
                return delegate { };

            var backupStream = Stream;
            var backupStatelessMode = StatelessMode;
            var backupFunctionsDisabled = FunctionsDisabled;
            var backupTemperature = Temperature;
            var backupMaxTokens = MaxTokens;
            var restoreProvider = ApplyProviderSpecificRequestProfile(profile);

            if (profile.Stateless.HasValue)
                StatelessMode = profile.Stateless.Value;

            if (profile.DisableFunctions.HasValue)
                FunctionsDisabled = profile.DisableFunctions.Value;

            if (profile.Temperature.HasValue)
                Temperature = profile.Temperature.Value;

            if (profile.MaxTokens.HasValue)
                MaxTokens = profile.MaxTokens.Value;

            return () =>
            {
                restoreProvider();
                Stream = backupStream;
                StatelessMode = backupStatelessMode;
                FunctionsDisabled = backupFunctionsDisabled;
                Temperature = backupTemperature;
                MaxTokens = backupMaxTokens;
            };
        }

        protected virtual Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            return delegate { };
        }

        protected virtual Action ApplyRequestContext(AIRequestContext context)
        {
            var backup = _currentRequestContext.Value;
            _currentRequestContext.Value = context;
            return () => _currentRequestContext.Value = backup;
        }

        protected internal string GetEffectiveSystemMessageWithRequestContext()
        {
            var systemMessage = GetEffectiveSystemMessage();
            var structuredOutputInstruction = GetStructuredOutputInstruction();

            if (string.IsNullOrEmpty(structuredOutputInstruction))
                return systemMessage;

            return string.IsNullOrEmpty(systemMessage)
                ? structuredOutputInstruction.TrimStart('\n')
                : systemMessage + structuredOutputInstruction;
        }

        internal Message ResolveRequestMessage(Message message)
        {
            return message;
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