using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// IEmbeddingProvider implementation that calls the OpenAI Embeddings API.
    /// Uses the same HttpClient/API-key pattern as Mythosia.AI's ChatGptService.
    /// </summary>
    public class OpenAIEmbeddingProvider : IEmbeddingProvider
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private readonly string _model;

        /// <inheritdoc />
        public int Dimensions { get; }

        /// <summary>
        /// Creates an OpenAI embedding provider.
        /// </summary>
        /// <param name="apiKey">OpenAI API key.</param>
        /// <param name="httpClient">HttpClient instance (should not have a BaseAddress pre-set).</param>
        /// <param name="model">Embedding model name. Default is "text-embedding-3-small".</param>
        /// <param name="dimensions">Output vector dimensions. Default is 1536.</param>
        public OpenAIEmbeddingProvider(string apiKey, HttpClient httpClient, string model = "text-embedding-3-small", int dimensions = 1536)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _model = model;
            Dimensions = dimensions;
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
                ["input"] = inputList,
                ["dimensions"] = Dimensions
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"OpenAI Embeddings API request failed ({(int)response.StatusCode}): {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return ParseEmbeddingResponse(responseJson);
        }

        private static IReadOnlyList<float[]> ParseEmbeddingResponse(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            var data = doc.RootElement.GetProperty("data");
            var results = new List<float[]>(data.GetArrayLength());

            foreach (var item in data.EnumerateArray())
            {
                var embeddingArray = item.GetProperty("embedding");
                var vector = new float[embeddingArray.GetArrayLength()];
                int i = 0;
                foreach (var val in embeddingArray.EnumerateArray())
                {
                    vector[i++] = val.GetSingle();
                }
                results.Add(vector);
            }

            // Sort by index to ensure correct order
            return results;
        }
    }
}
