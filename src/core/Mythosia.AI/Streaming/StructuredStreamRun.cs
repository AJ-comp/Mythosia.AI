using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Utilities;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mythosia.AI.Models.Streaming
{
    /// <summary>
    /// Represents a running streaming request whose final response will be deserialized to <typeparamref name="T"/>.
    /// <para>
    /// The stream starts eagerly in the background on construction.
    /// Call <see cref="Stream"/> to observe text chunks in real-time (optional).
    /// Await <see cref="Result"/> to get the deserialized object after the stream completes
    /// (with auto-repair retries if the JSON is invalid).
    /// </para>
    /// <para>
    /// <b>Usage:</b>
    /// <code>
    /// var run = service.BeginStream(prompt)
    ///     .WithStructuredOutput(new StructuredOutputPolicy { MaxRepairAttempts = 2 })
    ///     .As&lt;MyDto&gt;();
    ///
    /// await foreach (var chunk in run.Stream(ct))
    ///     Console.Write(chunk);
    ///
    /// MyDto dto = await run.Result;
    /// </code>
    /// </para>
    /// </summary>
    /// <typeparam name="T">The POCO type to deserialize the LLM response into.</typeparam>
    public sealed class StructuredStreamRun<T> where T : class
    {
        private readonly Mythosia.AI.Builders.AIRequestBuilder _request;
        private readonly int _maxAttempts;
        private readonly string _schemaJson;
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly Channel<string> _channel;
        private readonly TaskCompletionSource<string> _completionTcs;
        private int _streamConsumed; // 0=available, 1=consumed
        private readonly Lazy<Task<T>> _resultLazy;

        internal StructuredStreamRun(AIService service, string prompt, StructuredOutputPolicy? policy)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));
            var maxRepairAttempts = policy?.MaxRepairAttempts ?? service.StructuredOutputMaxRetries;
            _schemaJson = JsonSchemaGenerator.Generate(typeof(T));
            _request = service.CreateRequest(prompt).WithStructuredOutputSchema(_schemaJson);
            // Consume the request's one-call settings before rejecting its budget, while
            // still validating before the eager producer starts any provider work.
            _maxAttempts = AIService.ResolveStructuredOutputAttemptLimit(maxRepairAttempts);

            _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
            _completionTcs = new TaskCompletionSource<string>(
#if !NETSTANDARD2_0
                TaskCreationOptions.RunContinuationsAsynchronously
#endif
            );
            _resultLazy = new Lazy<Task<T>>(() => ResolveResultAsync());

            // Start streaming immediately in background
            _ = ProduceStreamAsync();
        }

        /// <summary>
        /// Enumerate streaming text chunks as they arrive from the LLM.
        /// <para>
        /// Can only be called <b>once</b>; a second call throws <see cref="InvalidOperationException"/>.
        /// Calling this method is <b>optional</b> — <see cref="Result"/> works without it.
        /// </para>
        /// </summary>
        public async IAsyncEnumerable<string> Stream(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _streamConsumed, 1, 0) != 0)
                throw new InvalidOperationException("Stream() can only be called once.");

            await foreach (var chunk in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return chunk;
            }
        }

        /// <summary>
        /// Awaitable result that completes after the stream finishes and the response
        /// has been deserialized (with auto-repair retries if needed).
        /// <para>
        /// Safe to access before, during, or after <see cref="Stream"/> enumeration.
        /// If <see cref="Stream"/> was never called, the buffer is still accumulated
        /// internally and the result is produced normally.
        /// </para>
        /// </summary>
        public Task<T> Result => _resultLazy.Value;

        #region Internal Implementation

        private async Task ProduceStreamAsync()
        {
            try
            {
                await foreach (var chunk in _request.StreamAsync())
                {
                    _buffer.Append(chunk);
                    await _channel.Writer.WriteAsync(chunk);
                }

                _channel.Writer.Complete();
                _completionTcs.TrySetResult(_buffer.ToString());
            }
            catch (Exception ex)
            {
                _channel.Writer.Complete(ex);
                _completionTcs.TrySetException(ex);
            }
        }

        private async Task<T> ResolveResultAsync()
        {
            var rawResponse = await _completionTcs.Task;
            return await ParseAndRepairAsync(rawResponse);
        }

        private async Task<T> ParseAndRepairAsync(string rawResponse)
        {
            string firstRawResponse = rawResponse;
            string lastRawResponse = rawResponse;
            string? lastParseError = null;

            for (int attempt = 0; attempt < _maxAttempts; attempt++)
            {
                string toProcess;

                if (attempt == 0)
                {
                    toProcess = rawResponse;
                }
                else
                {
                    // Repair via non-streaming call
                    var correctionPrompt = BuildCorrectionPrompt(lastRawResponse, lastParseError!);
                    toProcess = await _request.WithInput(correctionPrompt).GetCompletionAsync();
                    lastRawResponse = toProcess;
                }

                var extracted = AIService.ExtractJsonFromResponse(toProcess);

                try
                {
                    var result = JsonSerializer.Deserialize<T>(extracted, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (result != null) return result;
                    lastParseError = $"Deserialization returned null for type {typeof(T).Name}";
                }
                catch (JsonException ex)
                {
                    lastParseError = ex.Message;
                }
            }

            throw new StructuredOutputException(
                typeof(T).Name,
                firstRawResponse,
                lastRawResponse,
                lastParseError ?? "Unknown error",
                _maxAttempts,
                _schemaJson);
        }

        private static string BuildCorrectionPrompt(string rawResponse, string parseError)
        {
            return "[STRUCTURED OUTPUT CORRECTION] Your previous response was not valid JSON " +
                   "conforming to the required schema.\n\n" +
                   $"Your output was:\n{rawResponse}\n\n" +
                   $"Parse error: {parseError}\n\n" +
                   "Output ONLY valid JSON that strictly conforms to the schema. " +
                   "No markdown code blocks, no explanation, no text before or after the JSON.";
        }

        #endregion
    }
}
