using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests;

internal static class RetrievalEmbeddingLiveSettings
{
    public const string OptInVariable = "MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE";
    public const string VoyageIntervalVariable = "MYTHOSIA_VOYAGE_REQUEST_INTERVAL_SECONDS";

    internal static TimeSpan ParseVoyageRequestInterval(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TimeSpan.Zero;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds < 0 || seconds > 120)
            throw new InvalidOperationException($"{VoyageIntervalVariable} must be an integer from 0 to 120.");
        return TimeSpan.FromSeconds(seconds);
    }

    internal static async Task<string> ResolveKeyAsync(string provider,
        Func<string, string?> readEnvironment, Func<string, Task<string>> readSecret)
    {
        if (readEnvironment(OptInVariable) != "1")
            Assert.Inconclusive($"Set {OptInVariable}=1 to enable billable retrieval embedding live tests. No credentials were read.");

        var variables = provider switch
        {
            "Voyage" => new[] { "VOYAGE_API_KEY", "MYTHOSIA_VOYAGE_API_KEY" },
            "Gemini" => new[] { "GEMINI_API_KEY", "GOOGLE_API_KEY", "MYTHOSIA_GEMINI_API_KEY" },
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        foreach (var variable in variables)
        {
            var value = readEnvironment(variable);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        var secretName = provider == "Gemini" ? "gemini-secret" : readEnvironment("MYTHOSIA_VOYAGE_SECRET_NAME");
        if (string.IsNullOrWhiteSpace(secretName))
            Assert.Inconclusive("Voyage credential missing. Set VOYAGE_API_KEY or MYTHOSIA_VOYAGE_API_KEY, or set MYTHOSIA_VOYAGE_SECRET_NAME to an existing Key Vault secret name.");

        string? secret = null;
        try { secret = await readSecret(secretName!).ConfigureAwait(false); }
        catch (Exception)
        {
            // Authentication errors can include account details; never include the exception or key.
            Assert.Inconclusive($"{provider} Key Vault credential is unavailable. Configure an API key environment variable or authenticate to the existing vault.");
        }
        if (string.IsNullOrWhiteSpace(secret))
            Assert.Inconclusive($"{provider} credential is empty. Live API validation did not run.");
        return secret!;
    }
}

// One shared instance is used by every Voyage capture handler. The monotonic clock
// spaces request starts across test cases; cancellation never reserves a future slot.
internal sealed class RetrievalEmbeddingLiveRequestPacer
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<TimeSpan> _elapsed;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private TimeSpan? _lastStart;

    public RetrievalEmbeddingLiveRequestPacer() : this(CreateClock(), Task.Delay) { }

    internal RetrievalEmbeddingLiveRequestPacer(Func<TimeSpan> elapsed, Func<TimeSpan, CancellationToken, Task> delay)
    { _elapsed = elapsed; _delay = delay; }

    private static Func<TimeSpan> CreateClock()
    {
        var started = Stopwatch.GetTimestamp();
        return () => Stopwatch.GetElapsedTime(started);
    }

    public async Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (_lastStart.HasValue)
            {
                var remaining = interval - (_elapsed() - _lastStart.Value);
                if (remaining <= TimeSpan.Zero) break;
                await _delay(remaining, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            _lastStart = _elapsed();
        }
        finally { _gate.Release(); }
    }
}

// Only synthetic documents are sent. Bodies remain in memory for assertions; logs contain
// fixed provider/model names, request counts and status, never keys, URLs, text or vectors.
internal sealed class RetrievalEmbeddingLiveProbe : IDisposable
{
    private readonly CaptureHandler _handler;
    private readonly HttpClient _http;
    public string ProviderName { get; }
    public string Model { get; }
    public IRetrievalEmbeddingProvider Provider { get; }
    public IReadOnlyList<RequestRecord> Requests => _handler.Requests.ToArray();

    private RetrievalEmbeddingLiveProbe(string provider, string key, TimeSpan voyageInterval)
    {
        ProviderName = provider;
        Model = provider == "Voyage" ? "voyage-context-4" : "gemini-embedding-2";
        _handler = new CaptureHandler(provider, voyageInterval);
        _http = new HttpClient(_handler) { Timeout = TimeSpan.FromMinutes(3) };
        Provider = provider == "Voyage"
            ? new VoyageContextualizedEmbeddingProvider(key, _http, dimensions: 1024)
            : new GeminiEmbeddingProvider(key, _http, dimensions: 1536);
    }

    public static async Task<RetrievalEmbeddingLiveProbe> CreateAsync(string provider)
    {
        var interval = provider == "Voyage" ? RetrievalEmbeddingLiveSettings.ParseVoyageRequestInterval(
            Environment.GetEnvironmentVariable(RetrievalEmbeddingLiveSettings.VoyageIntervalVariable)) : TimeSpan.Zero;
        return new(provider, await RetrievalEmbeddingLiveSettings.ResolveKeyAsync(
            provider, Environment.GetEnvironmentVariable, LiveTestSecrets.GetAsync), interval);
    }

    public void AssertTransport(int documentCalls, int queryCalls)
    {
        Assert.HasCount(documentCalls + queryCalls, Requests, "Every expected call must reach the real endpoint without hidden retries.");
        Assert.AreEqual(documentCalls, Requests.Count(record => record.Operation == "document"));
        Assert.AreEqual(queryCalls, Requests.Count(record => record.Operation == "query"));
        foreach (var request in Requests)
        {
            Assert.IsTrue(request.IsOfficialHttps && request.HasAuthentication && !request.HasQuery);
            Assert.AreEqual(200, request.StatusCode);
            Assert.AreEqual(Provider.Dimensions, request.Dimensions);
            Assert.AreEqual(Model, request.Model);
            if (ProviderName == "Voyage")
            {
                Assert.AreEqual("/v1/contextualizedembeddings", request.Path);
                Assert.AreEqual("float", request.Body["output_dtype"]!.GetValue<string>());
                Assert.HasCount(1, request.Body["inputs"]!.AsArray());
            }
            else
            {
                Assert.AreEqual($"/v1beta/models/{Model}:embedContent", request.Path);
                Assert.IsFalse(request.Body["embedContentConfig"]!["autoTruncate"]!.GetValue<bool>());
                Assert.IsFalse(request.Body.ContainsKey("taskType"));
            }
        }
    }

    public static void AssertVector(float[] vector, int dimensions)
    {
        Assert.HasCount(dimensions, vector);
        Assert.IsTrue(vector.All(float.IsFinite));
        Assert.IsGreaterThan(0d, vector.Sum(value => (double)value * value));
    }

    public void Dispose()
    {
        _http.CancelPendingRequests();
        foreach (var request in Requests)
            Console.WriteLine("LIVE_RETRIEVAL_EMBEDDING_REQUEST " + JsonSerializer.Serialize(new
            {
                provider = ProviderName, model = Model, request.StatusCode,
                dimensions = request.Dimensions, operation = request.Operation, chunks = request.ChunkCount
            }));
        _http.Dispose();
    }

    internal sealed class RequestRecord
    {
        public required JsonObject Body { get; init; }
        public required string Path { get; init; }
        public required string Model { get; init; }
        public required string Operation { get; init; }
        public int Dimensions { get; init; }
        public int ChunkCount { get; init; }
        public bool IsOfficialHttps { get; init; }
        public bool HasAuthentication { get; init; }
        public bool HasQuery { get; init; }
        public int StatusCode { get; set; }
    }

    private sealed class CaptureHandler(string provider, TimeSpan voyageInterval) : DelegatingHandler(new HttpClientHandler())
    {
        private static readonly RetrievalEmbeddingLiveRequestPacer VoyagePacer = new();
        public ConcurrentQueue<RequestRecord> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var uri = request.RequestUri!;
            var voyage = provider == "Voyage";
            var text = voyage ? null : body["content"]!["parts"]![0]!["text"]!.GetValue<string>();
            var record = new RequestRecord
            {
                Body = body, Path = uri.AbsolutePath,
                Model = voyage ? body["model"]!.GetValue<string>() : uri.AbsolutePath.Split('/').Last().Split(':')[0],
                Operation = voyage ? body["input_type"]?.GetValue<string>() ?? "generic"
                    : text!.StartsWith("task: search result | query: ", StringComparison.Ordinal) ? "query"
                    : text.StartsWith("title: ", StringComparison.Ordinal) ? "document" : "generic",
                Dimensions = voyage ? body["output_dimension"]!.GetValue<int>()
                    : body["embedContentConfig"]!["outputDimensionality"]!.GetValue<int>(),
                ChunkCount = voyage ? body["inputs"]!.AsArray().Sum(group => group!.AsArray().Count) : 1,
                IsOfficialHttps = uri.Scheme == "https" && uri.Host == (voyage ? "api.voyageai.com" : "generativelanguage.googleapis.com"),
                HasAuthentication = voyage ? request.Headers.Authorization is { Scheme: "Bearer", Parameter.Length: > 0 }
                    : request.Headers.TryGetValues("x-goog-api-key", out var values) && values.Any(value => !string.IsNullOrWhiteSpace(value)),
                HasQuery = !string.IsNullOrEmpty(uri.Query)
            };
            if (voyage) await VoyagePacer.WaitAsync(voyageInterval, cancellationToken).ConfigureAwait(false);
            Requests.Enqueue(record);
            var response = await base.SendAsync(request, cancellationToken);
            record.StatusCode = (int)response.StatusCode;
            return response;
        }
    }
}
