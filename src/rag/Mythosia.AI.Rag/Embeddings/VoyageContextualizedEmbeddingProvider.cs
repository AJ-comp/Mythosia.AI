using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Embeddings
{
    /// <summary>Generates Voyage embeddings while retaining the context of each document's ordered chunks.</summary>
    /// <remarks>
    /// Retrieval methods explicitly distinguish documents from queries. The legacy generic methods omit
    /// input_type and embed each text as an independent, single-chunk group. Documents are never split into
    /// separate HTTP requests: one document may contain at most 16,000 chunks. Generic batches may contain
    /// at most 1,000 independent inputs. Voyage also enforces model-specific token limits on the server;
    /// this provider neither estimates tokens from character counts nor truncates or rechunks inputs.
    /// The caller owns HttpClient. Its configuration and default headers are not changed.
    /// </remarks>
    public sealed class VoyageContextualizedEmbeddingProvider : IRetrievalEmbeddingProvider
    {
        private const string Provider = "Voyage";
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private readonly TimeSpan? _timeout;

        /// <summary>The model requested for both document and query embeddings.</summary>
        public string Model { get; }

        /// <inheritdoc />
        public int Dimensions { get; }

        /// <summary>Creates a provider using the caller's reusable HTTP client.</summary>
        /// <param name="apiKey">Voyage API key.</param>
        /// <param name="httpClient">Caller-owned HTTP client.</param>
        /// <param name="model">Voyage contextualized embedding model name.</param>
        /// <param name="dimensions">Output dimensions: 256, 512, 1024, or 2048.</param>
        /// <param name="timeout">Optional per-request timeout. Null uses the client's timeout; the client's timeout always also applies.</param>
        public VoyageContextualizedEmbeddingProvider(string apiKey, HttpClient httpClient,
            string model = "voyage-context-4", int dimensions = 1024, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(character => character <= ' ' || character > '~'))
                throw new ArgumentException("A nonempty API key without whitespace or control characters is required.", nameof(apiKey));
            _apiKey = apiKey;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (string.IsNullOrEmpty(model) || !IsAsciiLetterOrDigit(model[0]) ||
                model.Any(character => !IsAsciiLetterOrDigit(character) && character != '-' && character != '_' && character != '.'))
                throw new ArgumentException("Provide a model name containing only letters, digits, dots, underscores, or hyphens, beginning with a letter or digit.", nameof(model));
            if (dimensions != 256 && dimensions != 512 && dimensions != 1024 && dimensions != 2048)
                throw new ArgumentOutOfRangeException(nameof(dimensions), "Voyage contextualized embeddings support 256, 512, 1024, or 2048 dimensions.");
            if (timeout.HasValue && timeout.Value != Timeout.InfiniteTimeSpan &&
                (timeout.Value <= TimeSpan.Zero || timeout.Value.TotalMilliseconds > int.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must be positive and at most Int32.MaxValue milliseconds, or infinite.");
            Model = model;
            Dimensions = dimensions;
            _timeout = timeout;
        }

        /// <summary>Embeds one text without a retrieval-specific input type.</summary>
        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => (await GetEmbeddingsAsync(new[] { text }, cancellationToken).ConfigureAwait(false))[0];

        /// <summary>Embeds independent texts without a retrieval-specific input type or shared document context.</summary>
        public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = ValidateTexts(texts, nameof(texts), 1000, cancellationToken);
            if (inputs.Length == 0) return Array.Empty<float[]>();
            var groups = inputs.Select(text => new[] { text }).ToArray();
            var vectors = await SendAsync(groups, null, cancellationToken).ConfigureAwait(false);
            return vectors.Select(group => group[0]).ToArray();
        }

        /// <summary>Embeds all ordered chunks together in one request with input_type set to document.</summary>
        /// <remarks>DocumentId and Title are application metadata and are not sent or prepended to the chunks.</remarks>
        public async Task<IReadOnlyList<float[]>> GetDocumentEmbeddingsAsync(EmbeddingDocument document, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document == null) throw new ArgumentNullException(nameof(document));
            var chunks = ValidateTexts(document.Chunks, nameof(document), 16000, cancellationToken);
            if (chunks.Length == 0) return Array.Empty<float[]>();
            return (await SendAsync(new[] { chunks }, "document", cancellationToken).ConfigureAwait(false))[0];
        }

        /// <summary>Embeds a query with input_type set to query using the same model and dimensions as documents.</summary>
        public async Task<float[]> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = ValidateTexts(new[] { query }, nameof(query), 1, cancellationToken);
            return (await SendAsync(new[] { input }, "query", cancellationToken).ConfigureAwait(false))[0][0];
        }

        private async Task<float[][][]> SendAsync(string[][] inputs, string? inputType, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["inputs"] = inputs,
                ["output_dimension"] = Dimensions,
                ["output_dtype"] = "float",
                ["enable_auto_chunking"] = false
            };
            if (inputType != null) body["input_type"] = inputType;
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.voyageai.com/v1/contextualizedembeddings")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_timeout.HasValue) timeout.CancelAfter(_timeout.Value);
            try
            {
                // ResponseContentRead buffers under the request token, including on netstandard2.1,
                // where ReadAsStringAsync has no CancellationToken overload.
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Voyage embeddings request failed (HTTP {(int)response.StatusCode}). Check the model, credentials, rate limits, and the service's token limits for pre-chunked inputs; documents are not truncated or split automatically.");
                string json;
                try { json = await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
                catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
                {
                    // Invalid charset names and decoder failures can contain remote header or payload text.
                    throw InvalidResponse("The response could not be decoded as text.");
                }
                timeout.Token.ThrowIfCancellationRequested();
                using var result = EmbeddingResponseValidation.ParseDocument(json, Provider);
                var root = result.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("model", out var model) &&
                    (model.ValueKind != JsonValueKind.String || model.GetString() != Model))
                    throw InvalidResponse("The response model does not match the requested model.");
                var vectors = ReadIndexed(root, inputs.Length, (group, index) =>
                    ReadIndexed(group, inputs[index].Length, (item, _) => ReadVector(item)));
                timeout.Token.ThrowIfCancellationRequested();
                return vectors;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException("Voyage embeddings request was canceled.", cancellationToken);
                // Neither HttpClient diagnostics nor remote error content belongs in exception chains.
                throw new TimeoutException("Voyage embeddings request timed out or was canceled by the HTTP client.");
            }
            catch (HttpRequestException)
            {
                throw new HttpRequestException("Voyage embeddings request failed during HTTP transport.");
            }
            catch (IOException)
            {
                throw new HttpRequestException("Voyage embeddings response could not be read.");
            }
        }

        private static T[] ReadIndexed<T>(JsonElement parent, int expectedCount, Func<JsonElement, int, T> read)
        {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw InvalidResponse("Each result level must contain a data array.");
            if (data.GetArrayLength() != expectedCount)
                throw InvalidResponse($"The response count does not match the input count: expected {expectedCount}, received {data.GetArrayLength()}.");
            var ordered = new T[expectedCount];
            var seen = new bool[expectedCount];
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("index", out var value) ||
                    value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var index) ||
                    index < 0 || index >= expectedCount || seen[index])
                    throw InvalidResponse("Each result must have a unique integer index within the input range.");
                seen[index] = true;
                ordered[index] = read(item, index);
            }
            return ordered;
        }

        private float[] ReadVector(JsonElement item)
        {
            if (!item.TryGetProperty("embedding", out var embedding))
                throw InvalidResponse("Every result must contain an embedding array.");
            return EmbeddingResponseValidation.ReadVector(embedding, Dimensions, Provider);
        }

        private static string[] ValidateTexts(IEnumerable<string> texts, string parameter, int maximum,
            CancellationToken cancellationToken)
        {
            if (texts == null) throw new ArgumentNullException(parameter);
            var inputs = new List<string>();
            using (var iterator = texts.GetEnumerator())
            {
                while (true)
                {
                    // Bound lazy input consumption before materializing it. Check both sides
                    // of MoveNext because user iterators may cancel even when returning false.
                    cancellationToken.ThrowIfCancellationRequested();
                    var hasNext = iterator.MoveNext();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!hasNext) break;
                    if (inputs.Count == maximum)
                        throw new ArgumentException($"Provide at most {maximum} nonempty texts, retaining each document's chunk order.", parameter);
                    var text = iterator.Current;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(text))
                        throw new ArgumentException($"Provide at most {maximum} nonempty texts, retaining each document's chunk order.", parameter);
                    inputs.Add(text);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return inputs.ToArray();
        }

        private static bool IsAsciiLetterOrDigit(char value)
            => (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') || (value >= '0' && value <= '9');

        private static InvalidOperationException InvalidResponse(string detail)
            => EmbeddingResponseValidation.InvalidResponse(Provider, detail);
    }
}
