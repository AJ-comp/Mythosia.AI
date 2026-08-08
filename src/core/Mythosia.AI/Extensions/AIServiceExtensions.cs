using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System;
using System.Threading;
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
        public static void StartNewConversation(this AIService service, string model)
        {
            service.ChangeModel(model);
            service.AddNewChat(new ChatBlock
            {
                SystemMessage = service.ActivateChat.SystemMessage
            });
        }

        /// <summary>
        /// Switches to a different model while preserving conversation history
        /// </summary>
        public static void SwitchModel(this AIService service, string model)
        {
            service.ChangeModel(model);
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
            summary.AppendLine($"Model: {service.Model}");
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
        /// Registers a synchronous <see cref="AIRequestContext"/> provider that the service
        /// invokes automatically before every outbound request (including agent-path calls),
        /// so callers do not have to build and pass an <see cref="AIRequestContext"/>
        /// at every entry point. If a call also supplies an explicit context, the two
        /// are merged field-by-field (explicit wins on scalar fields; additional
        /// messages are concatenated).
        /// </summary>
        public static AIService WithSystemMessageProvider(this AIService service, Func<AIRequestContext?> provider)
        {
            service.SystemMessageProvider = provider == null
                ? null
                : (Func<CancellationToken, ValueTask<AIRequestContext?>>)(_ => new ValueTask<AIRequestContext?>(provider()));
            return service;
        }

        /// <summary>
        /// Registers an asynchronous <see cref="AIRequestContext"/> provider. Use this
        /// overload when the baseline context requires IO (database, cache, HTTP) so that
        /// the provider does not need to block on <c>.Result</c> or <c>.GetAwaiter().GetResult()</c>.
        /// The supplied <see cref="CancellationToken"/> is the caller's token for the
        /// current LLM call (for non-streaming paths it is <see cref="CancellationToken.None"/>).
        /// Merge semantics are identical to the synchronous overload.
        /// </summary>
        public static AIService WithSystemMessageProvider(this AIService service, Func<CancellationToken, ValueTask<AIRequestContext?>> provider)
        {
            service.SystemMessageProvider = provider;
            return service;
        }

        /// <summary>
        /// Registers diagnostic callbacks for SSE streaming on this service. Useful for
        /// observability and post-mortem analysis when a stream dies mid-flight against
        /// a self-hosted backend (vLLM, ollama, internal proxy). Each <c>On*</c> method
        /// on the builder is independent — only call the ones you need.
        ///
        /// Calling this method overwrites any previously registered diagnostics on the
        /// service. Pass <c>d =&gt; { }</c> to clear all callbacks.
        /// </summary>
        /// <example>
        /// <code>
        /// // Wire raw SSE lines to Debug logger and aggregate metrics on completion
        /// service.WithStreamDiagnostics(d =&gt; d
        ///     .OnRawLine(line =&gt; logger.LogDebug("SSE: {Line}", line))
        ///     .OnComplete(diag =&gt; metrics.Record(diag.LinesRead, diag.Elapsed)));
        /// </code>
        /// </example>
        public static T WithStreamDiagnostics<T>(this T service, Action<StreamDiagnosticsBuilder> configure)
            where T : AIService
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            var builder = new StreamDiagnosticsBuilder();
            configure(builder);

            service.StreamRawLineCallback = builder.RawLineCallback;
            service.StreamCompleteCallback = builder.CompleteCallback;

            return service;
        }

        /// <summary>
        /// Configures the temperature for the current chat
        /// </summary>
        public static AIService WithTemperature(this AIService service, float temperature)
        {
            service.Temperature = Math.Max(0, Math.Min(2, temperature));
            return service;
        }

        /// <summary>
        /// Configures the max tokens for the current chat
        /// </summary>
        public static AIService WithMaxTokens(this AIService service, uint maxTokens)
        {
            service.MaxTokens = maxTokens;
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

            // Resend the message. The three-argument overload, explicitly — the one-argument abstract
            // wins overload resolution otherwise and skips context-overflow recovery.
            return await service.GetCompletionAsync(lastUserMessage, null, null);
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

        #region Structured Output Policy

        /// <summary>
        /// 일회성 structured output 정책 오버라이드 (Fluent 스타일).
        /// 다음 GetCompletionAsync&lt;T&gt;() 호출에만 적용되며 호출 후 자동으로 초기화됩니다.
        /// </summary>
        public static AIService WithStructuredOutputPolicy(this AIService service, StructuredOutputPolicy policy)
        {
            service._currentStructuredOutputPolicy = policy;
            return service;
        }

        /// <summary>
        /// retry 없이 1회만 시도하는 structured output 정책
        /// </summary>
        public static AIService WithNoRetryStructuredOutput(this AIService service)
            => service.WithStructuredOutputPolicy(StructuredOutputPolicy.NoRetry);

        /// <summary>
        /// 최대 3회 재시도하는 엄격 structured output 정책
        /// </summary>
        public static AIService WithStrictStructuredOutput(this AIService service)
            => service.WithStructuredOutputPolicy(StructuredOutputPolicy.Strict);

        #endregion

        #region Function Calling Policy

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
            var policy = (service.CurrentPolicy ?? service.DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            policy.TimeoutSeconds = seconds;
            return service.WithPolicy(policy);
        }

        /// <summary>
        /// 커스텀 라운드 제한
        /// </summary>
        public static AIService WithMaxRounds(this AIService service, int rounds)
        {
            var policy = (service.CurrentPolicy ?? service.DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            policy.MaxRounds = rounds;
            return service.WithPolicy(policy);
        }

        #endregion

    }
}
