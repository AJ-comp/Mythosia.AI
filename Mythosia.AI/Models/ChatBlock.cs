using System;
using System.Collections.Generic;
using System.Linq;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Extensions;

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
            Model = model.ToDescription();
        }

        public void ChangeModel(string model)
        {
            Model = model;
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

        /// <summary>
        /// Creates a deep copy of the ChatBlock
        /// </summary>
        public ChatBlock Clone()
        {
            var clone = new ChatBlock(Model)
            {
                SystemMessage = SystemMessage,
                TopP = TopP,
                Temperature = Temperature,
                FrequencyPenalty = FrequencyPenalty,
                MaxTokens = MaxTokens,
                MaxMessageCount = MaxMessageCount
            };

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
    }

    public enum RequestBodyType
    {
        Message,
        TokenCount
    }
}