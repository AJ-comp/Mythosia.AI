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
    public class VllmReranker : IReranker
    {
        private const string DefaultBaseUrl = "http://localhost:8003";
        private const string DefaultModel = "Qwen/Qwen3-Reranker-0.6B";

        private readonly HttpClient _httpClient;
        private readonly string _model;
        private readonly string _baseUrl;
        private readonly string? _apiKey;

        public VllmReranker(
            HttpClient? httpClient = null,
            string model = DefaultModel,
            string baseUrl = DefaultBaseUrl,
            string? apiKey = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        }

        public async Task<IReadOnlyList<VectorSearchResult>> RerankAsync(
            string query,
            IReadOnlyList<VectorSearchResult> results,
            CancellationToken cancellationToken = default)
        {
            if (results.Count == 0)
                return results;

            var documents = results
                .Select(r => r.Record.Content ?? string.Empty)
                .ToList();

            var requestBody = new
            {
                model = _model,
                query,
                documents,
                top_n = results.Count,
                return_documents = false
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/rerank");
            request.Content = content;
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"vLLM rerank request failed ({(int)response.StatusCode}): {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return ParseRerankResponse(responseJson, results);
        }

        private static IReadOnlyList<VectorSearchResult> ParseRerankResponse(
            string responseJson,
            IReadOnlyList<VectorSearchResult> results)
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("results", out var resultsArray))
                throw new InvalidOperationException("vLLM rerank response did not contain a 'results' field.");

            var rerankedResults = new List<VectorSearchResult>();
            foreach (var item in resultsArray.EnumerateArray())
            {
                var index = item.GetProperty("index").GetInt32();
                var relevanceScore = item.TryGetProperty("relevance_score", out var scoreElement)
                    ? scoreElement.GetDouble()
                    : item.TryGetProperty("score", out var fallbackScore)
                        ? fallbackScore.GetDouble()
                        : 0.0;

                if (index >= 0 && index < results.Count)
                    rerankedResults.Add(new VectorSearchResult(results[index].Record, relevanceScore));
            }

            return rerankedResults;
        }
    }
}
