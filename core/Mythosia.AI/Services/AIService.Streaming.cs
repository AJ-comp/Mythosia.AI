using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        #region Redesigned Streaming Methods

        /// <summary>
        /// Simple text streaming (most common use case)
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
        /// Simple text streaming with Message input
        /// </summary>
        public async IAsyncEnumerable<string> StreamAsync(
            Message message,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Use text-only options for simple streaming
            var options = StreamOptions.TextOnlyOptions;

            await foreach (var content in StreamAsync(message, options, cancellationToken))
            {
                if (content.Content != null)
                    yield return content.Content;
            }
        }

        public async IAsyncEnumerable<string> StreamAsync(
            Message message,
            AIRequestContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var options = StreamOptions.TextOnlyOptions;

            await foreach (var content in StreamAsync(message, options, context, cancellationToken))
            {
                if (content.Content != null)
                    yield return content.Content;
            }
        }

        /// <summary>
        /// Advanced streaming with options
        /// </summary>
        public async IAsyncEnumerable<StreamingContent> StreamAsync(
            string prompt,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var message = new Message(ActorRole.User, prompt);
            await foreach (var content in StreamAsync(message, options, cancellationToken))
            {
                yield return content;
            }
        }

        public async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            AIRequestContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                await foreach (var content in StreamAsync(message, options, cancellationToken))
                {
                    yield return content;
                }

                yield break;
            }

            var restoreContext = ApplyRequestContext(context);
            try
            {
                await foreach (var content in StreamAsync(message, options, cancellationToken))
                {
                    yield return content;
                }
            }
            finally
            {
                restoreContext();
            }
        }

        /// <summary>
        /// Core streaming implementation using Template Method pattern.
        /// Manages the round loop, StatelessMode, and conversation summary policy.
        /// Providers override <see cref="StreamRoundAsync"/> to handle a single round.
        /// Providers that do not support function calling rounds (e.g., DeepSeek, Sonar)
        /// may override this method directly.
        /// </summary>
        public virtual async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var policy = CurrentPolicy ?? DefaultPolicy;
            CurrentPolicy = null;

            bool useFunctions = options.IncludeFunctionCalls &&
                               ShouldUseFunctions &&
                               !FunctionsDisabled;

            ChatBlock originalChat = null;
            if (StatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = new ChatBlock { SystemMessage = ActivateChat.SystemMessage };
            }

            try
            {
                Stream = true;
                ActivateChat.Messages.Add(message);

                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    await ApplySummaryPolicyIfNeededAsync();

                    bool hasFunctionResult = false;
                    await foreach (var content in StreamRoundAsync(
                        options, useFunctions, policy, cancellationToken))
                    {
                        if (content.Type == StreamingContentType.FunctionResult)
                            hasFunctionResult = true;

                        yield return content;
                    }

                    if (!hasFunctionResult)
                        yield break;
                }
            }
            finally
            {
                if (originalChat != null)
                    ActivateChat = originalChat;
            }
        }

        /// <summary>
        /// Executes a single streaming round: sends an HTTP request, reads the SSE stream,
        /// yields chunks, and handles function execution if detected.
        /// Yield a <see cref="StreamingContentType.FunctionResult"/> to signal the template
        /// to continue to the next round; otherwise the stream ends.
        /// </summary>
        protected virtual async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options,
            bool useFunctions,
            FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Default implementation using callback-based streaming (for providers without round support)
            var channel = Channel.CreateUnbounded<StreamingContent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

            var streamingTask = Task.Run(async () =>
            {
                try
                {
                    // Build a temporary message from the last user message
                    var lastMessage = ActivateChat.Messages[ActivateChat.Messages.Count - 1];
                    await StreamCompletionAsync(lastMessage, async content =>
                    {
                        await channel.Writer.WriteAsync(new StreamingContent
                        {
                            Type = StreamingContentType.Text,
                            Content = content
                        }, cancellationToken);
                    });
                }
                catch (Exception ex)
                {
                    await channel.Writer.WriteAsync(new StreamingContent
                    {
                        Type = StreamingContentType.Error,
                        Content = ex.Message,
                        Metadata = new Dictionary<string, object> { ["error"] = ex.Message }
                    }, cancellationToken);
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            }, cancellationToken);

            await foreach (var content in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return content;
            }

            try
            {
                await streamingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
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

        #region Legacy Callback-based Streaming

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

        public abstract Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync);

        #endregion
    }
}