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
        private int _dimensions;

        public int Dimensions => _dimensions;

        public VllmEmbeddingProvider(
            HttpClient httpClient,
            string model = "Qwen/Qwen3-Embedding-0.6B",
            int dimensions = 1024,
            string baseUrl = "http://localhost:8002")
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

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/embeddings")
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"vLLM embeddings request failed ({(int)response.StatusCode}): {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return ParseEmbeddingResponse(responseJson, inputList.Count);
        }

        private IReadOnlyList<float[]> ParseEmbeddingResponse(string responseJson, int expectedCount)
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data))
                throw new InvalidOperationException("vLLM embeddings response did not contain a 'data' field.");

            var indexedResults = new List<(int Index, float[] Vector)>(data.GetArrayLength());
            foreach (var item in data.EnumerateArray())
            {
                var index = item.TryGetProperty("index", out var indexElement) ? indexElement.GetInt32() : indexedResults.Count;
                var embedding = item.GetProperty("embedding");
                indexedResults.Add((index, ParseVector(embedding)));
            }

            var ordered = indexedResults
                .OrderBy(item => item.Index)
                .Select(item => item.Vector)
                .ToList();

            if (ordered.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"vLLM embeddings response count mismatch. Expected {expectedCount}, got {ordered.Count}.");
            }

            EnsureDimensions(ordered);
            return ordered;
        }

        private static float[] ParseVector(JsonElement embeddingArray)
        {
            var vector = new float[embeddingArray.GetArrayLength()];
            var i = 0;
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
