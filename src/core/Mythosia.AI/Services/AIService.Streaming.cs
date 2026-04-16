using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
            await foreach (var chunk in StreamAsync(message, cancellationToken: cancellationToken))
            {
                yield return chunk;
            }
        }

        /// <summary>
        /// Simple text streaming with Message input
        /// </summary>
        public async IAsyncEnumerable<string> StreamAsync(
            Message message,
            AIRequestContext? context = null,
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
            await foreach (var content in StreamAsync(message, options, cancellationToken: cancellationToken))
            {
                yield return content;
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
            AIRequestContext? context = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Action restoreContext = context != null ? ApplyRequestContext(context) : () => { };
            try
            {
                await foreach (var content in StreamCoreAsync(message, options, cancellationToken))
                    yield return content;
            }
            finally
            {
                restoreContext();
            }
        }

        /// <summary>
        /// Core streaming loop. Override this method to replace the full streaming pipeline
        /// (round loop, StatelessMode, summary policy). Most providers should override
        /// <see cref="StreamRoundAsync"/> instead.
        /// </summary>
        protected virtual async IAsyncEnumerable<StreamingContent> StreamCoreAsync(
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

                int accInputTokens = 0;
                int accOutputTokens = 0;
                int accCachedInputTokens = 0;
                int accCacheCreationTokens = 0;
                int accReasoningTokens = 0;

                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    bool hasFunctionResult = false;
                    TokenUsage? roundUsage = null;

                    await foreach (var content in StreamRoundAsync(
                        options, useFunctions, policy, cancellationToken))
                    {
                        if (content.Type == StreamingContentType.FunctionResult)
                            hasFunctionResult = true;

                        // Providers can attach usage to different chunk types.
                        // Keep the last usage seen in the round and count it once.
                        if (content.Usage != null)
                            roundUsage = CopyTokenUsage(content.Usage);

                        if (content.Type == StreamingContentType.Completion)
                        {
                            // Only yield Completion on the final round (no more function calls)
                            continue;
                        }

                        yield return content;
                    }

                    if (roundUsage != null)
                    {
                        accInputTokens += roundUsage.InputTokens;
                        accOutputTokens += roundUsage.OutputTokens;
                        accCachedInputTokens += roundUsage.CachedInputTokens;
                        accCacheCreationTokens += roundUsage.CacheCreationTokens;
                        accReasoningTokens += roundUsage.ReasoningTokens;

                        // Update last known input tokens for summary trigger.
                        // Each round's InputTokens represents the full conversation context,
                        // so the latest value is the most accurate measure.
                        LastKnownInputTokens = roundUsage.InputTokens;

                        if (!options.TextOnly)
                        {
                            yield return CreateRoundUsageContent(
                                round + 1,
                                isFinalRound: !hasFunctionResult,
                                roundUsage);
                        }
                    }

                    if (!hasFunctionResult)
                    {
                        // Final round — yield Completion with accumulated usage
                        if (!options.TextOnly)
                        {
                            var finalCompletion = new StreamingContent
                            {
                                Type = StreamingContentType.Completion
                            };
                            if (options.IncludeMetadata)
                            {
                                finalCompletion.Metadata = new Dictionary<string, object>
                                {
                                    ["model"] = Model,
                                    ["total_rounds"] = round + 1
                                };
                            }
                            if (accInputTokens > 0 || accOutputTokens > 0)
                            {
                                finalCompletion.Usage = new TokenUsage
                                {
                                    InputTokens = accInputTokens,
                                    OutputTokens = accOutputTokens,
                                    TotalTokens = accInputTokens + accOutputTokens,
                                    CachedInputTokens = accCachedInputTokens,
                                    CacheCreationTokens = accCacheCreationTokens,
                                    ReasoningTokens = accReasoningTokens
                                };
                            }
                            yield return finalCompletion;
                        }

                        // Apply summary after streaming completes (prepares context for next turn)
                        await ApplySummaryPolicyIfNeededAsync();
                        yield break;
                    }
                }
            }
            finally
            {
                if (originalChat != null)
                    ActivateChat = originalChat;
            }
        }

        protected static StreamingContent CreateRoundUsageContent(
            int roundIndex,
            bool isFinalRound,
            TokenUsage usage)
        {
            return new StreamingContent
            {
                Type = StreamingContentType.RoundUsage,
                RoundIndex = roundIndex,
                IsFinalRound = isFinalRound,
                Usage = CopyTokenUsage(usage)
            };
        }

        protected static TokenUsage CopyTokenUsage(TokenUsage usage)
        {
            return new TokenUsage
            {
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                TotalTokens = usage.InputTokens + usage.OutputTokens,
                CachedInputTokens = usage.CachedInputTokens,
                CacheCreationTokens = usage.CacheCreationTokens,
                ReasoningTokens = usage.ReasoningTokens
            };
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
                await foreach (var chunk in StreamAsync(message, cancellationToken: cancellationToken))
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
