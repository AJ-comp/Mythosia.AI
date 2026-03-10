using Mythosia.VectorDb;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Reranking
{
    /// <summary>
    /// Re-ranker that uses the Cohere Rerank API to reorder search results by relevance.
    /// Requires a Cohere API key. See: https://docs.cohere.com/reference/rerank
    /// </summary>
    public class CohereReranker : IReranker
    {
        private const string DefaultEndpoint = "https://api.cohere.com/v2/rerank";
        private const string DefaultModel = "rerank-v3.5";

        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private readonly string _model;
        private readonly string _endpoint;

        /// <summary>
        /// Creates a Cohere-based re-ranker.
        /// </summary>
        /// <param name="apiKey">Cohere API key.</param>
        /// <param name="httpClient">Optional HTTP client. If null, a new one is created.</param>
        /// <param name="model">Model to use. Default: rerank-v3.5.</param>
        /// <param name="endpoint">API endpoint override.</param>
        public CohereReranker(
            string apiKey,
            HttpClient? httpClient = null,
            string model = DefaultModel,
            string endpoint = DefaultEndpoint)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _httpClient = httpClient ?? new HttpClient();
            _model = model;
            _endpoint = endpoint;
        }

        public async Task<IReadOnlyList<VectorSearchResult>> RerankAsync(
            string query,
            IReadOnlyList<VectorSearchResult> results,
            int topK,
            CancellationToken cancellationToken = default)
        {
            if (results.Count == 0)
                return results;

            // Build request body
            var documents = results.Select(r => r.Record.Content ?? string.Empty).ToList();

            var requestBody = new
            {
                model = _model,
                query = query,
                documents = documents,
                top_n = Math.Min(topK, results.Count),
                return_documents = false
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            request.Content = content;

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            var rerankedResults = new List<VectorSearchResult>();
            var resultsArray = doc.RootElement.GetProperty("results");

            foreach (var item in resultsArray.EnumerateArray())
            {
                var index = item.GetProperty("index").GetInt32();
                var relevanceScore = item.GetProperty("relevance_score").GetDouble();

                if (index >= 0 && index < results.Count)
                {
                    rerankedResults.Add(new VectorSearchResult(results[index].Record, relevanceScore));
                }
            }

            return rerankedResults;
        }
    }
}
