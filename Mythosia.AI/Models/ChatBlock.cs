using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Models
{
    public class ChatBlock
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string Model { get; private set; }
        public string SystemMessage { get; set; } = string.Empty;
        public IList<Message> Messages { get; } = new List<Message>();

        public float TopP { get; set; } = 1.0f;
        public float Temperature { get; set; } = 0.7f;
        public float FrequencyPenalty { get; set; } = 0.0f;
        public float PresencePenalty { get; set; } = 0.0f;
        public uint MaxTokens { get; set; } = 1024;
        public bool Stream { get; set; }
        public uint MaxMessageCount { get; set; } = 20;

        /// <summary>
        /// Functions available for AI to call
        /// </summary>
        public List<FunctionDefinition> Functions { get; set; } = new List<FunctionDefinition>();

        /// <summary>
        /// Whether to enable function calling (default: true)
        /// </summary>
        public bool EnableFunctions { get; set; } = true;

        /// <summary>
        /// Function call mode when functions are enabled
        /// </summary>
        public FunctionCallMode FunctionCallMode { get; set; } = FunctionCallMode.Auto;

        /// <summary>
        /// Force specific function to be called
        /// </summary>
        public string ForceFunctionName { get; set; }

        /// <summary>
        /// Checks if functions are available and enabled
        /// </summary>
        public bool ShouldUseFunctions => Functions.Count > 0 && EnableFunctions;


        public ChatBlock(AIModel model)
        {
            Model = model.ToDescription();
        }

        public ChatBlock(string model)
        {
            Model = model;
        }

        public void ChangeModel(AIModel model)
        {
            var newModelString = model.ToDescription();
            ChangeModelInternal(newModelString);
        }

        public void ChangeModel(string model)
        {
            ChangeModelInternal(model);
        }

        private void ChangeModelInternal(string newModel)
        {
            var oldModel = Model;
            Model = newModel;

            // Remove Function messages when changing models
            // This prevents compatibility issues between different model APIs
            if (oldModel != newModel)
            {
                RemoveFunctionMessages();
            }
        }

        /// <summary>
        /// Removes all Function-related messages from the conversation
        /// </summary>
        public void RemoveFunctionMessages()
        {
            var functionMessages = Messages.Where(m =>
                m.Role == ActorRole.Function ||
                (m.Role == ActorRole.Assistant &&
                 m.Metadata?.GetValueOrDefault("type")?.ToString() == "function_call")
            ).ToList();

            foreach (var msg in functionMessages)
            {
                Messages.Remove(msg);
            }

            if (functionMessages.Count > 0)
            {
                Console.WriteLine($"[Model Change] Removed {functionMessages.Count} function-related messages for compatibility");
            }
        }

        /// <summary>
        /// Gets the latest messages up to MaxMessageCount
        /// </summary>
        internal IEnumerable<Message> GetLatestMessages()
        {
            return Messages.Skip(Math.Max(0, Messages.Count - (int)MaxMessageCount));
        }

        /// <summary>
        /// Clears all messages from the conversation
        /// </summary>
        public void ClearMessages()
        {
            Messages.Clear();
        }

        /// <summary>
        /// Removes the last message if it exists
        /// </summary>
        public void RemoveLastMessage()
        {
            if (Messages.Count > 0)
            {
                Messages.RemoveAt(Messages.Count - 1);
            }
        }

        /// <summary>
        /// Gets the total estimated token count for the conversation
        /// </summary>
        public uint EstimateTotalTokens()
        {
            uint tokens = 0;

            // System message tokens
            if (!string.IsNullOrEmpty(SystemMessage))
            {
                tokens += (uint)(SystemMessage.Length / 4); // Rough estimate
            }

            // Message tokens
            foreach (var message in GetLatestMessages())
            {
                tokens += message.EstimateTokens();
            }

            return tokens;
        }

        public ChatBlock CloneWithoutMessages()
        {
            var clone = new ChatBlock(Model)
            {
                SystemMessage = SystemMessage,
                TopP = TopP,
                Temperature = Temperature,
                FrequencyPenalty = FrequencyPenalty,
                PresencePenalty = PresencePenalty,
                MaxTokens = MaxTokens,
                MaxMessageCount = MaxMessageCount,
                Functions = new List<FunctionDefinition>(Functions),
                EnableFunctions = EnableFunctions,
                FunctionCallMode = FunctionCallMode,
                ForceFunctionName = ForceFunctionName
            };

            // Messages는 비워둠
            return clone;
        }

        /// <summary>
        /// Creates a deep copy of the ChatBlock
        /// </summary>
        public ChatBlock Clone()
        {
            var clone = CloneWithoutMessages();

            foreach (var message in Messages)
            {
                clone.Messages.Add(message.Clone());
            }

            return clone;
        }

        /// <summary>
        /// Gets a summary of the conversation
        /// </summary>
        public string GetConversationSummary()
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"Model: {Model}");
            summary.AppendLine($"Messages: {Messages.Count}");

            if (!string.IsNullOrEmpty(SystemMessage))
            {
                summary.AppendLine($"System: {SystemMessage}");
            }

            foreach (var message in Messages.TakeLast(5)) // Last 5 messages
            {
                summary.AppendLine($"{message.Role}: {message.GetDisplayText().Truncate(50)}");
            }

            return summary.ToString();
        }

        #region Request Body Generation (moved from old version)

        public object ToRequestBody(AIProvider provider, RequestBodyType bodyType = RequestBodyType.Message)
        {
            return provider switch
            {
                AIProvider.OpenAI => ToChatGptRequestBody(),
                AIProvider.Anthropic => ToClaudeRequestBody(bodyType),
                AIProvider.Google => ToGeminiRequestBody(),
                AIProvider.DeepSeek => ToDeepSeekRequestBody(),
                AIProvider.Perplexity => ToPerplexityRequestBody(),
                _ => throw new NotSupportedException($"Provider {provider} not supported")
            };
        }

        private object ToChatGptRequestBody()
        {
            var messagesList = new List<object>();

            if (!string.IsNullOrEmpty(SystemMessage))
            {
                messagesList.Add(new { role = "system", content = SystemMessage });
            }

            foreach (var message in GetLatestMessages())
            {
                messagesList.Add(message.ToRequestFormat(AIProvider.OpenAI));
            }

            return new
            {
                model = Model,
                messages = messagesList,
                top_p = TopP,
                temperature = Temperature,
                frequency_penalty = FrequencyPenalty,
                max_tokens = MaxTokens,
                stream = Stream
            };
        }

        private object ToClaudeRequestBody(RequestBodyType requestBodyType)
        {
            var messagesList = new List<object>();

            foreach (var message in GetLatestMessages())
            {
                messagesList.Add(message.ToRequestFormat(AIProvider.Anthropic));
            }

            if (requestBodyType == RequestBodyType.TokenCount)
            {
                return new
                {
                    model = Model,
                    system = SystemMessage,
                    messages = messagesList
                };
            }
            else
            {
                return new
                {
                    model = Model,
                    system = SystemMessage,
                    messages = messagesList,
                    top_p = TopP,
                    temperature = Temperature,
                    stream = Stream,
                    max_tokens = MaxTokens
                };
            }
        }

        private object ToGeminiRequestBody()
        {
            var contentsList = new List<object>();

            if (!string.IsNullOrEmpty(SystemMessage))
            {
                contentsList.Add(new
                {
                    role = "user",
                    parts = new[] { new { text = SystemMessage } }
                });
            }

            foreach (var message in GetLatestMessages())
            {
                contentsList.Add(message.ToRequestFormat(AIProvider.Google));
            }

            return new
            {
                contents = contentsList,
                generationConfig = new
                {
                    temperature = Temperature,
                    topP = TopP,
                    topK = 40,
                    maxOutputTokens = (int)MaxTokens
                }
            };
        }

        private object ToDeepSeekRequestBody()
        {
            var messagesList = new List<object>();

            if (!string.IsNullOrEmpty(SystemMessage))
            {
                messagesList.Add(new { role = "system", content = SystemMessage });
            }

            foreach (var message in GetLatestMessages())
            {
                messagesList.Add(new { role = message.Role.ToDescription(), content = message.Content });
            }

            return new
            {
                model = Model,
                messages = messagesList,
                temperature = Temperature,
                max_tokens = MaxTokens,
                stream = Stream
            };
        }

        private object ToPerplexityRequestBody()
        {
            var messagesList = new List<object>();

            if (!string.IsNullOrEmpty(SystemMessage))
            {
                messagesList.Add(new { role = "system", content = SystemMessage });
            }

            foreach (var message in GetLatestMessages())
            {
                messagesList.Add(new { role = message.Role.ToDescription(), content = message.Content });
            }

            return new
            {
                model = Model,
                messages = messagesList,
                stream = Stream,
                temperature = Temperature,
                top_p = TopP,
                frequency_penalty = 1.0f,
                presence_penalty = 0.0f,
                max_tokens = (int)MaxTokens
            };
        }

        #endregion

        /// <summary>
        /// Adds a function to this chat
        /// </summary>
        public ChatBlock AddFunction(FunctionDefinition function)
        {
            Functions.Add(function);
            return this;
        }

        /// <summary>
        /// Removes all functions
        /// </summary>
        public ChatBlock ClearFunctions()
        {
            Functions.Clear();
            return this;
        }
    }

    public enum RequestBodyType
    {
        Message,
        TokenCount
    }
}