using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Services.Perplexity;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests;

// Only synthetic inputs and public search queries are used. Diagnostics exclude keys,
// headers, search arguments, prompts, snippets, URLs and encoded vector contents.
internal sealed class PerplexityRetrievalLiveProbe : IDisposable
{
    private readonly string _key;
    private readonly CaptureHandler _handler = new();
    private readonly HttpClient _http;
    public IReadOnlyList<RequestRecord> Requests => _handler.Requests;
    public PerplexitySearchClient Search { get; }

    private PerplexityRetrievalLiveProbe(string key)
    {
        _key = key;
        _http = new HttpClient(_handler) { Timeout = TimeSpan.FromMinutes(3) };
        Search = new PerplexitySearchClient(key, _http);
    }

    public static async Task<PerplexityRetrievalLiveProbe> CreateAsync()
        => new(await LiveTestSecrets.GetAsync("sonar-secret2"));

    public PerplexityEmbeddingProvider Standard(string model) => new(_key, _http, model, 128);
    public PerplexityContextualizedEmbeddingProvider Context(string model) => new(_key, _http, model, 128);

    public void AssertTransport(int expectedCount, string path, string? model = null, string? format = null)
    {
        Assert.HasCount(expectedCount, Requests, "No hidden retries or substituted endpoints are permitted.");
        foreach (var request in Requests)
        {
            Assert.IsTrue(request.IsOfficialHttps);
            Assert.IsTrue(request.HasBearerAuthentication);
            Assert.IsFalse(request.HasQuery);
            Assert.AreEqual(path, request.Path);
            Assert.AreEqual(200, request.StatusCode, "Every actual API request must succeed.");
            Assert.IsNotNull(request.Response);
            if (model == null) continue;
            Assert.AreEqual(model, request.Body["model"]!.GetValue<string>());
            Assert.AreEqual(model, request.Response["model"]!.GetValue<string>());
            Assert.AreEqual(128, request.Body["dimensions"]!.GetValue<int>());
            Assert.AreEqual(format, request.Body["encoding_format"]!.GetValue<string>());
            Assert.IsGreaterThan(0, request.VectorCount);
            foreach (var item in request.VectorItems())
            {
                var encoded = item["embedding"]!.GetValue<string>();
                var bytes = Convert.FromBase64String(encoded);
                Assert.HasCount(format == "base64_binary" ? 16 : 128, bytes);
            }
        }
    }

    public static void AssertNormalized(float[] vector)
    {
        Assert.HasCount(128, vector);
        Assert.IsTrue(vector.All(float.IsFinite));
        Assert.AreEqual(1d, vector.Sum(value => (double)value * value), 0.00001);
    }

    public void Dispose()
    {
        _http.CancelPendingRequests();
        foreach (var request in Requests)
        {
            Console.WriteLine("LIVE_PERPLEXITY_RETRIEVAL_REQUEST " + JsonSerializer.Serialize(new
            {
                api = request.Path == "/search" ? "search" : request.Path == "/v1/embeddings" ? "standard" : "context",
                request.StatusCode,
                model = request.Body["model"]?.GetValue<string>(),
                format = request.Body["encoding_format"]?.GetValue<string>(),
                dimensions = request.Body["dimensions"]?.GetValue<int>(),
                searchType = request.Body["search_type"]?.GetValue<string>(),
                providerError = request.SafeError,
                count = request.Path == "/search" ? request.Response?["results"]?.AsArray().Count ?? 0 : request.VectorCount
            }));
        }
        _http.Dispose();
    }

    internal sealed class RequestRecord
    {
        public required JsonObject Body { get; init; }
        public required string Path { get; init; }
        public bool IsOfficialHttps { get; init; }
        public bool HasBearerAuthentication { get; init; }
        public bool HasQuery { get; init; }
        public int StatusCode { get; set; }
        public JsonObject? Response { get; set; }
        public string? SafeError { get; set; }
        public int VectorCount => VectorItems().Count();
        public IEnumerable<JsonNode> VectorItems()
        {
            if (Response?["data"] is not JsonArray data) yield break;
            foreach (var item in data)
            {
                if (item?["data"] is JsonArray chunks)
                {
                    foreach (var chunk in chunks) if (chunk != null) yield return chunk;
                }
                else if (item != null) yield return item;
            }
        }
    }

    private sealed class CaptureHandler : DelegatingHandler
    {
        public List<RequestRecord> Requests { get; } = [];
        public CaptureHandler() : base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var record = new RequestRecord
            {
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject(),
                Path = request.RequestUri!.AbsolutePath,
                IsOfficialHttps = request.RequestUri.Scheme == "https" && request.RequestUri.Host == "api.perplexity.ai",
                HasQuery = !string.IsNullOrEmpty(request.RequestUri.Query),
                HasBearerAuthentication = request.Headers.Authorization is { Scheme: "Bearer", Parameter.Length: > 0 }
            };
            Requests.Add(record);
            var response = await base.SendAsync(request, cancellationToken);
            record.StatusCode = (int)response.StatusCode;
            try
            {
                record.Response = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) as JsonObject;
                if (!response.IsSuccessStatusCode)
                    record.SafeError = SafeProviderError(record.Response, record.Body, request.Headers.Authorization?.Parameter);
                return response;
            }
            catch { response.Dispose(); throw; }
        }

        private static string SafeProviderError(JsonObject? response, JsonObject body, string? key)
        {
            if (response == null) return "No structured provider error.";
            var error = response["error"] as JsonObject ?? response;
            var parts = new List<string>();
            foreach (var name in new[] { "code", "type", "message", "error", "detail" })
                if (error[name] is JsonValue value && value.TryGetValue<string>(out var text)) parts.Add(name + "=" + text);
            if (response["detail"] is JsonArray details)
                foreach (var item in details)
                    if (item?["msg"] is JsonValue value && value.TryGetValue<string>(out var text)) parts.Add("message=" + text);
            var summary = parts.Count == 0 ? "No recognized provider error fields." : string.Join("; ", parts);
            if (!string.IsNullOrEmpty(key)) summary = summary.Replace(key, "[redacted]", StringComparison.Ordinal);
            foreach (var input in InputStrings(body).Where(input => input.Length >= 3).OrderByDescending(input => input.Length))
                summary = summary.Replace(input, "[input]", StringComparison.Ordinal);
            summary = System.Text.RegularExpressions.Regex.Replace(summary, "(?i)(?:bearer\\s+|pplx-)[A-Za-z0-9_\\-]+", "[redacted]");
            summary = System.Text.RegularExpressions.Regex.Replace(summary, "\\s+", " ");
            return summary.Length <= 600 ? summary : summary[..600];
        }

        private static IEnumerable<string> InputStrings(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var text)) yield return text;
            else if (node is JsonObject obj)
            {
                foreach (var item in obj) foreach (var child in InputStrings(item.Value)) yield return child;
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array) foreach (var child in InputStrings(item)) yield return child;
            }
        }
    }
}
