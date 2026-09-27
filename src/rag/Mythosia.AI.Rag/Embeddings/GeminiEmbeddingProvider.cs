using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Embeddings
{
    /// <summary>Text embeddings for Gemini Embedding 2, with explicit document and search-query formatting.</summary>
    /// <remarks>
    /// The caller owns HttpClient. Each input uses a separate embedContent request and produces one vector.
    /// Generic embedding methods send the original text; retrieval methods apply Gemini Embedding 2 prefixes
    /// only to the HTTP input. Gemini Embedding 2 normalizes reduced-dimensional vectors on the server.
    /// Custom model IDs must support the same Gemini Embedding 2 request and retrieval conventions.
    /// Input truncation is disabled. Caller cancellation preserves the caller's token; provider and
    /// HTTP-client timeouts throw TimeoutException. Transport and response diagnostics omit payloads.
    /// </remarks>
    public sealed class GeminiEmbeddingProvider : IRetrievalEmbeddingProvider
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private readonly Uri _endpoint;
        private readonly TimeSpan? _timeout;
        private readonly SemaphoreSlim _requestGate;
        private readonly int _maxConcurrency;

        public string Model { get; }
        public int Dimensions { get; }

        /// <param name="apiKey">The Gemini API key, sent in the x-goog-api-key header.</param>
        /// <param name="httpClient">Caller-owned HTTP client; its settings are not modified.</param>
        /// <param name="model">An unprefixed model ID, such as gemini-embedding-2.</param>
        /// <param name="dimensions">Requested vector dimensions, from 128 through 3072.</param>
        /// <param name="timeout">Optional timeout for the entire operation, including concurrency waits.
        /// Null adds no provider timeout; the caller's HttpClient timeout still applies.</param>
        /// <param name="maxConcurrency">Maximum simultaneous HTTP requests across calls to this provider.</param>
        public GeminiEmbeddingProvider(string apiKey, HttpClient httpClient, string model = "gemini-embedding-2",
            int dimensions = 1536, TimeSpan? timeout = null, int maxConcurrency = 4)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(character => character < '!' || character > '~'))
                throw new ArgumentException("A nonempty API key containing visible ASCII characters is required.", nameof(apiKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (string.IsNullOrWhiteSpace(model) || !IsModelCharacter(model[0], false)
                || model.Any(character => !IsModelCharacter(character, true)))
                throw new ArgumentException("Provide an unprefixed model ID containing only ASCII letters, digits, hyphens, underscores, or periods.", nameof(model));
            if (dimensions < 128 || dimensions > 3072)
                throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be between 128 and 3072.");
            if (timeout.HasValue && timeout.Value != Timeout.InfiniteTimeSpan
                && (timeout.Value <= TimeSpan.Zero || timeout.Value.TotalMilliseconds > int.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive, at most Int32.MaxValue milliseconds, or infinite.");
            if (maxConcurrency < 1)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Concurrency must be positive.");

            _apiKey = apiKey;
            Model = model;
            Dimensions = dimensions;
            _timeout = timeout;
            _maxConcurrency = maxConcurrency;
            _endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/models/" + model + ":embedContent");
            _requestGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        /// <summary>Embeds unmodified text, without inferring a retrieval task.</summary>
        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => (await EmbedAsync(new[] { text }, null, nameof(text), cancellationToken).ConfigureAwait(false))[0];

        /// <summary>Embeds each unmodified text independently and returns vectors in input order.</summary>
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
            => EmbedAsync(texts, null, nameof(texts), cancellationToken);

        /// <summary>Embeds each document chunk independently using its title and Gemini's retrieval document prefix.</summary>
        public Task<IReadOnlyList<float[]>> GetDocumentEmbeddingsAsync(EmbeddingDocument document, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document == null) throw new ArgumentNullException(nameof(document));
            var title = string.IsNullOrWhiteSpace(document.Title) ? "none" : document.Title;
            return EmbedAsync(document.Chunks, text => "title: " + title + " | text: " + text, nameof(document), cancellationToken);
        }

        /// <summary>Embeds a search query using Gemini's search-result task prefix.</summary>
        public async Task<float[]> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default)
            => (await EmbedAsync(new[] { query }, text => "task: search result | query: " + text,
                nameof(query), cancellationToken).ConfigureAwait(false))[0];

        private async Task<IReadOnlyList<float[]>> EmbedAsync(IEnumerable<string> texts, Func<string, string>? format,
            string parameter, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (texts == null) throw new ArgumentNullException(parameter);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_timeout.HasValue) operation.CancelAfter(_timeout.Value);
            try
            {
                var inputs = new List<string>();
                foreach (var text in texts)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(text))
                        throw new ArgumentException("Embedding inputs must contain nonempty text.", parameter);
                    inputs.Add(text);
                }
                operation.Token.ThrowIfCancellationRequested();
                if (inputs.Count == 0) return Array.Empty<float[]>();

                var vectors = new float[inputs.Count][];
                var nextIndex = -1;
                async Task RunWorkerAsync()
                {
                    int index;
                    while ((index = Interlocked.Increment(ref nextIndex)) < inputs.Count)
                        vectors[index] = await EmbedOneAsync(inputs[index], format, operation).ConfigureAwait(false);
                }

                // Keep both active requests and worker tasks bounded for large documents.
                var workers = new Task[Math.Min(inputs.Count, _maxConcurrency)];
                for (var index = 0; index < workers.Length; index++) workers[index] = RunWorkerAsync();
                await Task.WhenAll(workers).ConfigureAwait(false);
                operation.Token.ThrowIfCancellationRequested();
                return vectors;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Gemini embeddings operation was canceled.", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Gemini embeddings operation timed out or was canceled by the HTTP transport.");
            }
        }

        private async Task<float[]> EmbedOneAsync(string text, Func<string, string>? format, CancellationTokenSource operation)
        {
            var cancellationToken = operation.Token;
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await SendAsync(format == null ? text : format(text), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Stop this operation's other requests when any input fails; never return partial results.
                operation.Cancel();
                throw;
            }
            finally
            {
                _requestGate.Release();
            }
        }

        private async Task<float[]> SendAsync(string text, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    content = new { parts = new[] { new { text } } },
                    embedContentConfig = new { outputDimensionality = Dimensions, autoTruncate = false }
                }), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-goog-api-key", _apiKey);
            try
            {
                // ResponseContentRead applies the token to actual body buffering on netstandard2.1.
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Gemini embeddings request failed (HTTP {(int)response.StatusCode}).");
                string json;
                try { json = await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
                catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
                {
                    // Invalid charset names and decoder failures can contain remote header or payload text.
                    throw EmbeddingResponseValidation.InvalidResponse("Gemini", "The response could not be decoded as text.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                using var document = EmbeddingResponseValidation.ParseDocument(json, "Gemini");
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || root.EnumerateObject().Count(property => property.NameEquals("embedding")) != 1
                    || !root.TryGetProperty("embedding", out var embedding) || embedding.ValueKind != JsonValueKind.Object
                    || embedding.EnumerateObject().Count(property => property.NameEquals("values")) != 1
                    || !embedding.TryGetProperty("values", out var values))
                    throw EmbeddingResponseValidation.InvalidResponse("Gemini", "Exactly one embedding object with values is required.");
                RejectTruncated(root);
                RejectTruncated(embedding);
                var vector = EmbeddingResponseValidation.ReadVector(values, Dimensions, "Gemini");
                cancellationToken.ThrowIfCancellationRequested();
                return vector;
            }
            catch (OperationCanceledException)
            {
                throw new OperationCanceledException("Gemini embeddings request was canceled or timed out.", cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw new HttpRequestException("Gemini embeddings HTTP transport failed.");
            }
            catch (IOException)
            {
                throw new IOException("Gemini embeddings response could not be read.");
            }
        }

        private static void RejectTruncated(JsonElement parent)
        {
            if (parent.TryGetProperty("statistics", out var statistics) && statistics.ValueKind == JsonValueKind.Object
                && statistics.TryGetProperty("truncated", out var truncated) && truncated.ValueKind != JsonValueKind.False)
                throw EmbeddingResponseValidation.InvalidResponse("Gemini", "The response indicates truncated input or an invalid truncation flag.");
        }

        private static bool IsModelCharacter(char character, bool allowPunctuation)
            => character >= 'a' && character <= 'z' || character >= 'A' && character <= 'Z'
                || character >= '0' && character <= '9'
                || allowPunctuation && (character == '-' || character == '_' || character == '.');
    }
}
