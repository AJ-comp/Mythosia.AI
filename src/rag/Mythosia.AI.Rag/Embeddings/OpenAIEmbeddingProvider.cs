using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Embeddings
{
    /// <summary>
    /// IEmbeddingProvider implementation that calls the OpenAI Embeddings API.
    /// Uses the same HttpClient/API-key pattern as Mythosia.AI's OpenAIService.
    /// </summary>
    public class OpenAIEmbeddingProvider : IEmbeddingProvider
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private readonly string _model;

        private bool IsAda002 => string.Equals(_model, "text-embedding-ada-002", StringComparison.Ordinal);

        /// <inheritdoc />
        public int Dimensions { get; }

        /// <summary>
        /// Creates an OpenAI embedding provider.
        /// </summary>
        /// <param name="apiKey">OpenAI API key.</param>
        /// <param name="httpClient">HttpClient instance (should not have a BaseAddress pre-set).</param>
        /// <param name="model">Embedding model name. Default is "text-embedding-3-small".</param>
        /// <param name="dimensions">Output vector dimensions. Default is 1536. For text-embedding-ada-002, only 1536 is valid; no dimensions option is sent to the API.</param>
        public OpenAIEmbeddingProvider(string apiKey, HttpClient httpClient, string model = "text-embedding-3-small", int dimensions = 1536)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _model = model;
            Dimensions = dimensions > 0 ? dimensions : throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "Dimensions must be a positive integer.");
            if (IsAda002 && dimensions != 1536)
                throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions,
                    "text-embedding-ada-002 has a fixed output size of 1536 dimensions.");
        }

        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            var results = await GetEmbeddingsAsync(new[] { text }, cancellationToken);
            return results[0];
        }

        public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputList = texts.ToList();
            if (inputList.Count == 0)
                return Array.Empty<float[]>();

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["input"] = inputList
            };
            if (!IsAda002)
                requestBody["dimensions"] = Dimensions;

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"OpenAI Embeddings API request failed (HTTP {(int)response.StatusCode}).");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return IndexedEmbeddingResponseParser.Parse(responseJson, inputList.Count, Dimensions,
                "OpenAI", allowMissingIndices: false);
        }
    }
}
