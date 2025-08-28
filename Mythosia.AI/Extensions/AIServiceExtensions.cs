using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System;
using System.Threading.Tasks;

namespace Mythosia.AI.Extensions
{
    /// <summary>
    /// Extension methods for AIService to provide additional convenience features
    /// </summary>
    public static class AIServiceExtensions
    {
        /// <summary>
        /// Performs a one-time question without affecting the conversation history
        /// </summary>
        public static async Task<string> AskOnceAsync(this AIService service, string prompt)
        {
            var backup = service.StatelessMode;
            service.StatelessMode = true;
            try
            {
                return await service.GetCompletionAsync(prompt);
            }
            finally
            {
                service.StatelessMode = backup;
            }
        }

        /// <summary>
        /// Performs a one-time multimodal question without affecting the conversation history
        /// </summary>
        public static async Task<string> AskOnceAsync(this AIService service, Message message)
        {
            var backup = service.StatelessMode;
            service.StatelessMode = true;
            try
            {
                return await service.GetCompletionAsync(message);
            }
            finally
            {
                service.StatelessMode = backup;
            }
        }

        /// <summary>
        /// Performs a one-time question with an image
        /// </summary>
        public static async Task<string> AskOnceWithImageAsync(
            this AIService service,
            string prompt,
            string imagePath)
        {
            var backup = service.StatelessMode;
            service.StatelessMode = true;
            try
            {
                return await service.GetCompletionWithImageAsync(prompt, imagePath);
            }
            finally
            {
                service.StatelessMode = backup;
            }
        }

        /// <summary>
        /// Starts a new conversation, clearing the current history
        /// </summary>
        public static void StartNewConversation(this AIService service)
        {
            service.ActivateChat.Messages.Clear();
        }

        /// <summary>
        /// Starts a new conversation with a different model
        /// </summary>
        public static void StartNewConversation(this AIService service, AIModel model)
        {
            var newChat = new ChatBlock(model)
            {
                SystemMessage = service.ActivateChat.SystemMessage,
                Temperature = service.ActivateChat.Temperature,
                TopP = service.ActivateChat.TopP,
                MaxTokens = service.ActivateChat.MaxTokens
            };
            service.AddNewChat(newChat);
        }

        /// <summary>
        /// Switches to a different model while preserving conversation history
        /// </summary>
        public static void SwitchModel(this AIService service, AIModel model)
        {
            service.ActivateChat.ChangeModel(model);
        }

        /// <summary>
        /// Gets the last assistant response from the conversation
        /// </summary>
        public static string? GetLastAssistantResponse(this AIService service)
        {
            var messages = service.ActivateChat.Messages;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role == ActorRole.Assistant)
                {
                    return messages[i].GetDisplayText();
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the conversation summary
        /// </summary>
        public static string GetConversationSummary(this AIService service)
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"Model: {service.ActivateChat.Model}");
            summary.AppendLine($"Messages: {service.ActivateChat.Messages.Count}");
            summary.AppendLine($"Stateless Mode: {service.StatelessMode}");

            if (!string.IsNullOrEmpty(service.ActivateChat.SystemMessage))
            {
                summary.AppendLine($"System: {service.ActivateChat.SystemMessage}");
            }

            return summary.ToString();
        }

        /// <summary>
        /// Adds a system message to the current chat
        /// </summary>
        public static AIService WithSystemMessage(this AIService service, string systemMessage)
        {
            service.ActivateChat.SystemMessage = systemMessage;
            return service;
        }

        /// <summary>
        /// Configures the temperature for the current chat
        /// </summary>
        public static AIService WithTemperature(this AIService service, float temperature)
        {
            service.ActivateChat.Temperature = Math.Max(0, Math.Min(2, temperature));
            return service;
        }

        /// <summary>
        /// Configures the max tokens for the current chat
        /// </summary>
        public static AIService WithMaxTokens(this AIService service, uint maxTokens)
        {
            service.ActivateChat.MaxTokens = maxTokens;
            return service;
        }

        /// <summary>
        /// Enables or disables stateless mode
        /// </summary>
        public static AIService WithStatelessMode(this AIService service, bool enabled = true)
        {
            service.StatelessMode = enabled;
            return service;
        }

        /// <summary>
        /// Creates a fluent chain for building and sending a message
        /// </summary>
        public static MessageChain BeginMessage(this AIService service)
        {
            return new MessageChain(service);
        }

        /// <summary>
        /// Retries the last user message if the previous response was unsatisfactory
        /// </summary>
        public static async Task<string> RetryLastMessageAsync(this AIService service)
        {
            var messages = service.ActivateChat.Messages;
            if (messages.Count < 2)
                throw new InvalidOperationException("No messages to retry");

            // Remove the last assistant response
            if (messages[messages.Count - 1].Role == ActorRole.Assistant)
            {
                messages.RemoveAt(messages.Count - 1);
            }

            // Get the last user message
            Message? lastUserMessage = null;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role == ActorRole.User)
                {
                    lastUserMessage = messages[i];
                    messages.RemoveAt(i);
                    break;
                }
            }

            if (lastUserMessage == null)
                throw new InvalidOperationException("No user message found to retry");

            // Resend the message
            return await service.GetCompletionAsync(lastUserMessage);
        }

        /// <summary>
        /// Adds context from previous messages to a new prompt
        /// </summary>
        public static async Task<string> GetCompletionWithContextAsync(
            this AIService service,
            string prompt,
            int contextMessages = 5)
        {
            var messages = service.ActivateChat.Messages;
            var contextBuilder = new System.Text.StringBuilder();

            // Get the last N messages as context
            var startIndex = Math.Max(0, messages.Count - contextMessages);
            for (int i = startIndex; i < messages.Count; i++)
            {
                contextBuilder.AppendLine($"{messages[i].Role}: {messages[i].GetDisplayText()}");
            }

            contextBuilder.AppendLine($"\nBased on the above context, {prompt}");

            return await service.GetCompletionAsync(contextBuilder.ToString());
        }


        #region Policy

        /// <summary>
        /// 일회성 정책 오버라이드 (Fluent 스타일)
        /// </summary>
        public static AIService WithPolicy(this AIService service, FunctionCallingPolicy policy)
        {
            service.CurrentPolicy = policy;
            return service;
        }

        /// <summary>
        /// 빠른 실행 정책
        /// </summary>
        public static AIService WithFastPolicy(this AIService service)
            => service.WithPolicy(FunctionCallingPolicy.Fast);

        /// <summary>
        /// 복잡한 작업 정책
        /// </summary>
        public static AIService WithComplexPolicy(this AIService service)
            => service.WithPolicy(FunctionCallingPolicy.Complex);

        /// <summary>
        /// 커스텀 타임아웃
        /// </summary>
        public static AIService WithTimeout(this AIService service, int seconds)
        {
            var policy = new FunctionCallingPolicy
            {
                MaxRounds = service.DefaultPolicy.MaxRounds,
                TimeoutSeconds = seconds
            };
            return service.WithPolicy(policy);
        }

        /// <summary>
        /// 커스텀 라운드 제한
        /// </summary>
        public static AIService WithMaxRounds(this AIService service, int rounds)
        {
            var policy = new FunctionCallingPolicy
            {
                MaxRounds = rounds,
                TimeoutSeconds = service.DefaultPolicy.TimeoutSeconds
            };
            return service.WithPolicy(policy);
        }

        #endregion

    }
}