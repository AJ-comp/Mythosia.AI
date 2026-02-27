using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Enums;
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

        /// <summary>
        /// Core streaming implementation with full control
        /// Virtual method that can be overridden by implementations
        /// </summary>
        public virtual async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Default implementation using callback-based streaming
            var channel = Channel.CreateUnbounded<StreamingContent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

            // Start the streaming task
            var streamingTask = Task.Run(async () =>
            {
                try
                {
                    await StreamCompletionAsync(message, async content =>
                    {
                        var streamingContent = new StreamingContent
                        {
                            Type = StreamingContentType.Text,
                            Content = content
                        };

                        if (options.IncludeMetadata)
                        {
                            streamingContent.Metadata = new Dictionary<string, object>
                            {
                                ["timestamp"] = DateTime.UtcNow
                            };
                        }

                        await channel.Writer.WriteAsync(streamingContent, cancellationToken);
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

            // Read from the channel
            await foreach (var content in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return content;
            }

            // Ensure the streaming task completes
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