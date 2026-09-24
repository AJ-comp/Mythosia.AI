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
    public class VllmEmbeddingProvider : IEmbeddingProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _model;
        private readonly string _baseUrl;
        private readonly int _dimensions;

        public int Dimensions => _dimensions;

        public VllmEmbeddingProvider(
            HttpClient httpClient,
            string model = "Qwen/Qwen3-Embedding-0.6B",
            int dimensions = 1024,
            string baseUrl = "http://localhost:8002")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _model = model;
            _dimensions = dimensions > 0 ? dimensions : throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "Dimensions must be a positive integer.");
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
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
                ["input"] = inputList,
                ["dimensions"] = _dimensions
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/embeddings")
            {
                Content = content
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"vLLM embeddings request failed (HTTP {(int)response.StatusCode}).");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return IndexedEmbeddingResponseParser.Parse(responseJson, inputList.Count, Dimensions,
                "vLLM", allowMissingIndices: true);
        }
    }
}
