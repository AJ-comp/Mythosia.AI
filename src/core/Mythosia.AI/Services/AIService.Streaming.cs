using Mythosia.AI.Models;
using Mythosia.AI.Exceptions;
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
                if (content.Type == StreamingContentType.Error)
                {
                    throw new AIServiceException(
                        content.Content ?? $"{Provider} streaming request failed.",
                        content.Metadata == null ? string.Empty : System.Text.Json.JsonSerializer.Serialize(content.Metadata),
                        Provider);
                }

                if (content.Type == StreamingContentType.Text && content.Content != null)
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
            var effectiveContext = await BuildEffectiveContextAsync(context, cancellationToken).ConfigureAwait(false);
            Action restoreContext = effectiveContext != null ? ApplyRequestContext(effectiveContext) : () => { };
            try
            {
                // Context-overflow recovery is not here — it lives inside StreamCoreAsync's round
                // loop, where a failed round can be replayed on its own. Recovering at this level
                // would have to discard every round already streamed, and cannot help at all once
                // any chunk has gone out.
                await foreach (var content in StreamCoreAsync(message, options, cancellationToken))
                    yield return content;
            }
            finally
            {
                restoreContext();
            }
        }

        /// <summary>
        /// True when a streaming error chunk carries the provider-agnostic context-overflow flag
        /// set by <see cref="Exceptions.AIHttpErrorFactory.BuildErrorMetadata"/>.
        /// </summary>
        private static bool IsContextOverflowChunk(StreamingContent content)
            => content.Type == StreamingContentType.Error &&
               content.Metadata != null &&
               content.Metadata.TryGetValue(AIHttpErrorFactory.ContextLengthExceededKey, out var flag) &&
               flag is bool isOverflow && isOverflow;

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
            var policy = (CurrentPolicy ?? DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            CurrentPolicy = null;
            var timeoutSeconds = ResolveRequestTimeoutSeconds(policy);
            using var roundLoopCts = CreateRequestTimeoutCts(policy, cancellationToken);
            var roundLoopCancellationToken = roundLoopCts.Token;

            bool useFunctions = options.IncludeFunctionCalls &&
                               ShouldUseFunctions &&
                               !FunctionsDisabled;

            ChatBlock? originalChat = null;
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
                    bool roundFailed = false;
                    TokenUsage? roundUsage = null;

                    // Context-overflow recovery lives here, per round, rather than around the whole
                    // turn. A round that overflows has emitted nothing yet — the rejection is a
                    // pre-inference validation failure, so the error is that round's first chunk —
                    // which means the round can be compacted and replayed with no duplicate output
                    // and without discarding the tool results earlier rounds already produced.
                    for (int attempt = 0; ; attempt++)
                    {
                        hasFunctionResult = false;
                        roundFailed = false;
                        roundUsage = null;

                        bool roundEmittedAny = false;
                        StreamingContent? withheld = null;

                        await using (var enumerator = StreamRoundAsync(
                            options, useFunctions, policy, roundLoopCancellationToken)
                            .GetAsyncEnumerator(roundLoopCancellationToken))
                        {
                            while (await MoveNextStreamRoundAsync(
                                enumerator,
                                cancellationToken,
                                roundLoopCancellationToken,
                                timeoutSeconds))
                            {
                                var content = enumerator.Current;

                                if (!roundEmittedAny && attempt < ContextRecoveryMaxRetries &&
                                    !_isSummarizing && !StatelessMode && IsContextOverflowChunk(content))
                                {
                                    withheld = content;
                                    break;
                                }

                                roundEmittedAny = true;

                                if (content.Type == StreamingContentType.FunctionResult)
                                    hasFunctionResult = true;
                                else if (content.Type == StreamingContentType.Error &&
                                         !IsContextOverflowChunk(content))
                                    roundFailed = true;

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
                        }

                        if (withheld == null) break;

                        // C# forbids yielding from a catch block, so record the outcome and act below.
                        var compaction = SummaryCompactionResult.Skipped("compaction-threw");
                        try
                        {
                            compaction = await ForceCompactAsync();
                        }
                        catch
                        {
                            // fall through with the skipped result
                        }

                        // Compaction that does not shrink the outgoing request would replay the exact
                        // payload the server just refused. Release the error chunk and leave the
                        // attempt loop: with no function result and no usage recorded, this counts as
                        // the final round, so the stream still ends through the normal terminator —
                        // Completion chunk, accumulated usage, summary policy. Ending the iterator
                        // here instead would make the termination contract depend on whether recovery
                        // happened to be enabled.
                        if (!compaction.IsApplied)
                        {
                            yield return withheld;
                            break;
                        }
                    }

                    // A non-recoverable provider error is a terminal round outcome, not a
                    // successful empty response. Context-overflow errors retain their existing
                    // recovery/give-up completion contract, which callers use for diagnostics.
                    if (roundFailed)
                        yield break;

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

                // Every allowed round requested another tool turn. Ending the iterator silently
                // would look like a successful, empty completion to callers.
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Error,
                    Content = $"Maximum function-calling rounds ({policy.MaxRounds}) exceeded.",
                    Metadata = new Dictionary<string, object>
                    {
                        ["status"] = "max_rounds_exceeded",
                        ["max_rounds"] = policy.MaxRounds,
                        ["model"] = Model
                    }
                };
            }
            finally
            {
                if (originalChat != null)
                    ActivateChat = originalChat;
            }
        }

        private static async Task<bool> MoveNextStreamRoundAsync(
            IAsyncEnumerator<StreamingContent> enumerator,
            CancellationToken callerCancellationToken,
            CancellationToken roundLoopCancellationToken,
            int? timeoutSeconds)
        {
            try
            {
                roundLoopCancellationToken.ThrowIfCancellationRequested();
                return await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (
                callerCancellationToken.IsCancellationRequested)
            {
                // Preserve the caller-owned token instead of exposing the linked policy token.
                throw new OperationCanceledException(
                    exception.Message,
                    exception,
                    callerCancellationToken);
            }
            catch (OperationCanceledException exception) when (
                timeoutSeconds.HasValue &&
                roundLoopCancellationToken.IsCancellationRequested)
            {
                throw new AIServiceException(
                    $"Request timeout after {timeoutSeconds} seconds",
                    exception);
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
