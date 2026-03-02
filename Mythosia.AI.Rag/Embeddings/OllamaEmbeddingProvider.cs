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
    /// IEmbeddingProvider implementation that calls the local Ollama embeddings API.
    /// </summary>
    public class OllamaEmbeddingProvider : IEmbeddingProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _model;
        private readonly string _baseUrl;
        private int _dimensions;

        /// <inheritdoc />
        public int Dimensions => _dimensions;

        /// <summary>
        /// Creates an Ollama embedding provider.
        /// </summary>
        /// <param name="httpClient">HttpClient instance.</param>
        /// <param name="model">Embedding model name. Default is "qwen3-embedding:4b".</param>
        /// <param name="dimensions">Expected output vector dimensions. Default is 1024.</param>
        /// <param name="baseUrl">Ollama API base URL. Default is "http://localhost:11434".</param>
        public OllamaEmbeddingProvider(
            HttpClient httpClient,
            string model = "qwen3-embedding:4b",
            int dimensions = 1024,
            string baseUrl = "http://localhost:11434")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _model = model;
            _dimensions = dimensions;
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            var results = await GetEmbeddingsAsync(new[] { text }, cancellationToken);
            return results[0];
        }

        public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var inputList = texts.ToList();
            if (inputList.Count == 0)
                return Array.Empty<float[]>();

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["input"] = inputList
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/embed")
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"Ollama embeddings request failed ({(int)response.StatusCode}): {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return ParseEmbeddingResponse(responseJson, inputList.Count);
        }

        private IReadOnlyList<float[]> ParseEmbeddingResponse(string responseJson, int expectedCount)
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("embeddings", out var embeddingsArray))
            {
                var results = new List<float[]>(embeddingsArray.GetArrayLength());
                foreach (var embedding in embeddingsArray.EnumerateArray())
                {
                    results.Add(ParseVector(embedding));
                }
                if (results.Count != expectedCount)
                {
                    throw new InvalidOperationException(
                        $"Ollama embeddings response count mismatch. Expected {expectedCount}, got {results.Count}.");
                }
                EnsureDimensions(results);
                return results;
            }

            if (root.TryGetProperty("embedding", out var singleEmbedding))
            {
                if (expectedCount != 1)
                {
                    throw new InvalidOperationException(
                        $"Ollama embeddings response returned a single vector for {expectedCount} inputs.");
                }
                var vector = ParseVector(singleEmbedding);
                var results = new List<float[]>(expectedCount) { vector };
                EnsureDimensions(results);
                return results;
            }

            throw new InvalidOperationException("Ollama embeddings response did not contain an 'embeddings' or 'embedding' field.");
        }

        private float[] ParseVector(JsonElement embeddingArray)
        {
            var vector = new float[embeddingArray.GetArrayLength()];
            int i = 0;
            foreach (var val in embeddingArray.EnumerateArray())
            {
                vector[i++] = val.GetSingle();
            }
            return vector;
        }

        private void EnsureDimensions(IReadOnlyList<float[]> vectors)
        {
            if (vectors.Count == 0)
                return;

            var actualDimensions = vectors[0].Length;
            if (_dimensions <= 0 || _dimensions != actualDimensions)
                _dimensions = actualDimensions;
        }
    }
}
