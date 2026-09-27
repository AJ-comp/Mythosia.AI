using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Mythosia.AI.Rag.Embeddings;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class GeminiEmbeddingProviderTests
{
    private const string Secret = "gemini-private-payload-sentinel";
    private const string ApiKey = "gemini-private-key-sentinel";

    [TestMethod]
    public async Task DefaultConfiguration_UsesGemini2And1536DimensionsWithoutChangingHttpClient()
    {
        using var handler = new RecordingHandler(_ => VectorJson(1, 1536));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(27), BaseAddress = new Uri("https://unrelated.test/") };
        http.DefaultRequestHeaders.Add("X-Caller-Header", "preserved");
        var provider = new GeminiEmbeddingProvider(ApiKey, http);

        var vector = await provider.GetEmbeddingAsync("alpha");

        Assert.AreEqual("gemini-embedding-2", provider.Model);
        Assert.AreEqual(1536, provider.Dimensions);
        Assert.AreEqual(1536, vector.Length);
        Assert.AreEqual(1f, vector[0]);
        Assert.AreEqual(TimeSpan.FromSeconds(27), http.Timeout);
        Assert.AreEqual("https://unrelated.test/", http.BaseAddress!.AbsoluteUri);
        Assert.AreEqual("preserved", http.DefaultRequestHeaders.GetValues("X-Caller-Header").Single());
        Assert.IsFalse(http.DefaultRequestHeaders.Contains("x-goog-api-key"));
        var captured = handler.Requests.Single();
        Assert.AreEqual("https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-2:embedContent", captured.Request.RequestUri!.AbsoluteUri);
        Assert.AreEqual("", captured.Request.RequestUri.Query);
        Assert.AreEqual(ApiKey, captured.Request.Headers.GetValues("x-goog-api-key").Single());
        Assert.AreEqual(HttpMethod.Post, captured.Request.Method);
        Assert.AreEqual("application/json", captured.Request.Content!.Headers.ContentType!.MediaType);
        AssertWireRequest(captured.Body, "alpha", 1536);
        Assert.IsFalse(captured.Body.Contains(ApiKey, StringComparison.Ordinal));
        Assert.IsTrue(handler.Responses.Single().WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => captured.Request.Content.ReadAsStringAsync());
        // The provider does not own or dispose the caller's client.
        Assert.AreEqual(1536, (await provider.GetEmbeddingAsync("beta")).Length);
    }

    [TestMethod]
    [DataRow("generic-single")]
    [DataRow("generic-batch")]
    [DataRow("query")]
    [DataRow("document-single")]
    [DataRow("document-batch")]
    public async Task TaskMode_IsExplicitAndDoesNotDependOnInputCount(string mode)
    {
        using var handler = new RecordingHandler(_ => VectorJson());
        using var http = new HttpClient(handler);
        var provider = Provider(http);
        var chunks = new[] { "서울 \"guide\"\nfirst", "second | document" };
        var document = new EmbeddingDocument("private-document-id", chunks, "Travel \"title\"\n서울");

        IReadOnlyList<float[]> vectors = mode switch
        {
            "generic-single" => new[] { await provider.GetEmbeddingAsync(chunks[0]) },
            "generic-batch" => await provider.GetEmbeddingsAsync(chunks),
            "query" => new[] { await provider.GetQueryEmbeddingAsync(chunks[0]) },
            "document-single" => await provider.GetDocumentEmbeddingsAsync(new EmbeddingDocument("private-document-id", new[] { chunks[0] }, document.Title)),
            _ => await provider.GetDocumentEmbeddingsAsync(document)
        };

        var expected = mode switch
        {
            "generic-single" => new[] { chunks[0] },
            "generic-batch" => chunks,
            "query" => new[] { "task: search result | query: " + chunks[0] },
            "document-single" => new[] { "title: " + document.Title + " | text: " + chunks[0] },
            _ => chunks.Select(chunk => "title: " + document.Title + " | text: " + chunk).ToArray()
        };
        Assert.AreEqual(expected.Length, vectors.Count);
        CollectionAssert.AreEquivalent(expected, handler.Requests.Select(request => ReadText(request.Body)).ToArray());
        foreach (var request in handler.Requests)
        {
            AssertWireRequest(request.Body, ReadText(request.Body), 128);
            Assert.IsFalse(request.Body.Contains("private-document-id", StringComparison.Ordinal));
        }
        CollectionAssert.AreEqual(chunks, document.Chunks.ToArray());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public async Task DocumentWithoutTitle_UsesNone(string? title)
    {
        using var handler = new RecordingHandler(_ => VectorJson());
        using var http = new HttpClient(handler);
        await Provider(http).GetDocumentEmbeddingsAsync(new EmbeddingDocument("doc", new[] { "unchanged" }, title));
        Assert.AreEqual("title: none | text: unchanged", ReadText(handler.Requests.Single().Body));
    }

    [TestMethod]
    public async Task IndependentRequests_PreserveInputOrderWhenCompletionsAreOutOfOrder()
    {
        using var handler = new ControlledHandler("alpha", "beta", "gamma", "delta");
        using var http = new HttpClient(handler);
        var provider = new GeminiEmbeddingProvider(ApiKey, http, dimensions: 128, maxConcurrency: 2);

        var pending = provider.GetEmbeddingsAsync(new[] { "alpha", "beta", "gamma", "delta" });
        await Task.WhenAll(handler.Entered["alpha"].Task, handler.Entered["beta"].Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, handler.RequestCount);
        handler.Release["beta"].SetResult(2);
        await handler.Entered["gamma"].Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.Release["gamma"].SetResult(3);
        await handler.Entered["delta"].Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.Release["delta"].SetResult(4);
        handler.Release["alpha"].SetResult(1);

        var vectors = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f }, vectors.Select(vector => vector[0]).ToArray());
        Assert.AreEqual(2, handler.MaximumActive);
        Assert.AreEqual(4, handler.RequestCount);
        vectors[0][0] = 99;
        Assert.AreEqual(2f, vectors[1][0]);
    }

    [TestMethod]
    public async Task ConcurrencyLimit_IsSharedAcrossSeparateProviderCalls()
    {
        using var handler = new ControlledHandler("alpha", "beta");
        using var http = new HttpClient(handler);
        var provider = new GeminiEmbeddingProvider(ApiKey, http, dimensions: 128, maxConcurrency: 1);
        var first = provider.GetEmbeddingAsync("alpha");
        await handler.Entered["alpha"].Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = provider.GetEmbeddingAsync("beta");
        Assert.IsFalse(handler.Entered["beta"].Task.IsCompleted);
        handler.Release["alpha"].SetResult(1);
        await handler.Entered["beta"].Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.Release["beta"].SetResult(2);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, handler.MaximumActive);
    }

    [TestMethod]
    [DataRow(128)]
    [DataRow(768)]
    [DataRow(1536)]
    [DataRow(3072)]
    public async Task ConfiguredDimensions_AreRequestedAndVectorsAreNotSilentlyChanged(int dimensions)
    {
        using var handler = new RecordingHandler(_ => VectorJson(2, dimensions));
        using var http = new HttpClient(handler);
        var provider = new GeminiEmbeddingProvider(ApiKey, http, "gemini-embedding-2-future.1", dimensions);
        var vector = await provider.GetEmbeddingAsync("text");
        Assert.AreEqual(dimensions, vector.Length);
        Assert.AreEqual(2f, vector[0]);
        AssertWireRequest(handler.Requests.Single().Body, "text", dimensions);
        StringAssert.Contains(handler.Requests.Single().Request.RequestUri!.AbsolutePath, "gemini-embedding-2-future.1:embedContent");
    }

    [TestMethod]
    [DynamicData(nameof(InvalidResponses))]
    public async Task MalformedResponse_IsRejectedWithoutPayloadOrInnerException(string scenario, string response)
    {
        using var handler = new RecordingHandler(_ => response);
        using var http = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(http).GetEmbeddingAsync(Secret));
        StringAssert.Contains(exception.Message, "Gemini embeddings response is invalid");
        Assert.IsFalse(exception.ToString().Contains(Secret, StringComparison.Ordinal), scenario);
        Assert.IsFalse(exception.ToString().Contains(ApiKey, StringComparison.Ordinal), scenario);
        Assert.IsNull(exception.InnerException, scenario);
        Assert.IsTrue(handler.Responses.Single().WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Requests.Single().Request.Content!.ReadAsStringAsync());
    }

    public static IEnumerable<object[]> InvalidResponses()
    {
        yield return new object[] { "invalid-json", "{private: \"" + Secret + "\"}" };
        yield return new object[] { "nonobject", "[]" };
        yield return new object[] { "empty", "{}" };
        yield return new object[] { "null", "null" };
        yield return new object[] { "wrong-plural-envelope", "{\"embeddings\":[{\"values\":[1]},{\"values\":[2]}]}" };
        yield return new object[] { "embedding-null", "{\"embedding\":null}" };
        yield return new object[] { "embedding-array", "{\"embedding\":[]}" };
        yield return new object[] { "missing-values", "{\"embedding\":{}}" };
        yield return new object[] { "duplicate-embedding", "{\"embedding\":{\"values\":[1]},\"embedding\":{\"values\":[2]}}" };
        yield return new object[] { "duplicate-values", "{\"embedding\":{\"values\":[1],\"values\":[2]}}" };
        yield return new object[] { "values-string", "{\"embedding\":{\"values\":\"" + Secret + "\"}}" };
        yield return new object[] { "values-null", "{\"embedding\":{\"values\":null}}" };
        yield return new object[] { "empty-vector", VectorJson(0, 0) };
        yield return new object[] { "short-vector", VectorJson(0, 127) };
        yield return new object[] { "long-vector", VectorJson(0, 129) };
        foreach (var value in new[] { "null", "true", "{}", "[]", "\"" + Secret + "\"", "1e100", "-1e100", "NaN", "Infinity" })
            yield return new object[] { "invalid-coordinate-" + value, "{\"embedding\":{\"values\":[" + value + "," + string.Join(",", Enumerable.Repeat("0", 127)) + "]}}" };
        yield return new object[] { "truncated-root", VectorJson().TrimEnd('}') + "},\"statistics\":{\"truncated\":true}}" };
        yield return new object[] { "truncated-embedding", VectorJson().TrimEnd('}') + ",\"statistics\":{\"truncated\":true}}}" };
    }

    [TestMethod]
    public async Task ResponseMetadata_IsAllowedAndFalseTruncationFlagIsAccepted()
    {
        var response = JsonSerializer.Serialize(new
        {
            embedding = new { values = Enumerable.Repeat(0.5f, 128).ToArray(), shape = new[] { 128 }, statistics = new { truncated = false, tokenCount = 7 } },
            usageMetadata = new { promptTokenCount = 7 }, unknownFutureField = "accepted"
        });
        using var handler = new RecordingHandler(_ => response);
        using var http = new HttpClient(handler);
        Assert.AreEqual(128, (await Provider(http).GetEmbeddingAsync("text")).Length);
    }

    [TestMethod]
    [DataRow(400)]
    [DataRow(401)]
    [DataRow(429)]
    [DataRow(500)]
    public async Task HttpFailures_IncludeOnlyStatusAndAreNotRetried(int status)
    {
        using var handler = new RecordingHandler(_ => Secret) { Status = (HttpStatusCode)status, ReasonPhrase = Secret };
        using var http = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(http).GetEmbeddingAsync(Secret));
        StringAssert.Contains(exception.Message, "HTTP " + status);
        Assert.IsFalse(exception.ToString().Contains(Secret, StringComparison.Ordinal));
        Assert.IsFalse(exception.ToString().Contains(ApiKey, StringComparison.Ordinal));
        Assert.IsNull(exception.InnerException);
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.IsTrue(handler.Responses.Single().WasDisposed);
    }

    [TestMethod]
    [DataRow("http")]
    [DataRow("io")]
    [DataRow("cancellation")]
    public async Task TransportExceptions_DoNotExposeUnderlyingSensitiveDiagnostics(string kind)
    {
        using var handler = new ThrowingHandler(kind);
        using var http = new HttpClient(handler);
        Exception? exception = null;
        try { await Provider(http).GetEmbeddingAsync(Secret); }
        catch (Exception caught) { exception = caught; }
        Assert.IsNotNull(exception);
        Assert.IsFalse(exception.ToString().Contains(Secret, StringComparison.Ordinal));
        Assert.IsFalse(exception.ToString().Contains(ApiKey, StringComparison.Ordinal));
        Assert.IsNull(exception.InnerException);
    }

    [TestMethod]
    public async Task InvalidInputsAndEmptyBatches_DoNotSendRequests()
    {
        using var handler = new RecordingHandler(_ => VectorJson());
        using var http = new HttpClient(handler);
        var provider = Provider(http);
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetEmbeddingsAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetEmbeddingAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetEmbeddingAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetQueryEmbeddingAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetEmbeddingsAsync(new[] { "valid", null! }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetDocumentEmbeddingsAsync(null!));
        Assert.AreEqual(0, (await provider.GetEmbeddingsAsync(Array.Empty<string>())).Count);
        Assert.AreEqual(0, (await provider.GetDocumentEmbeddingsAsync(new EmbeddingDocument("empty", Array.Empty<string>()))).Count);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public void Constructor_RejectsInvalidConfigurationAndUnsafeModelPaths()
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentNullException>(() => new GeminiEmbeddingProvider(ApiKey, null!));
        foreach (var key in new[] { "", " ", "key\r\nsecret", "key\tsecret", "key\0secret" })
            Assert.Throws<ArgumentException>(() => new GeminiEmbeddingProvider(key, http));
        foreach (var model in new[] { "", " ", "models/gemini-embedding-2", "../gemini-embedding-2", "https://attacker.test/", "gemini?key=secret", "gemini#x", "gemini%2fother", "gemini\\other", "..", "gemini\r\nsecret" })
            Assert.Throws<ArgumentException>(() => new GeminiEmbeddingProvider(ApiKey, http, model));
        foreach (var dimensions in new[] { -1, 0, 127, 3073 })
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeminiEmbeddingProvider(ApiKey, http, dimensions: dimensions));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeminiEmbeddingProvider(ApiKey, http, maxConcurrency: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeminiEmbeddingProvider(ApiKey, http, timeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeminiEmbeddingProvider(ApiKey, http, timeout: TimeSpan.FromMilliseconds(-2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeminiEmbeddingProvider(ApiKey, http, timeout: TimeSpan.FromDays(100)));
        _ = new GeminiEmbeddingProvider(ApiKey, http, timeout: Timeout.InfiniteTimeSpan);
    }

    internal static string VectorJson(float first = 0.5f, int dimensions = 128)
    {
        var vector = new float[dimensions];
        if (dimensions > 0) vector[0] = first;
        return JsonSerializer.Serialize(new { embedding = new { values = vector } });
    }

    private static GeminiEmbeddingProvider Provider(HttpClient http) => new(ApiKey, http, dimensions: 128);

    private static string ReadText(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()!;
    }

    private static void AssertWireRequest(string json, string text, int dimensions)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var parts = root.GetProperty("content").GetProperty("parts");
        Assert.AreEqual(1, parts.GetArrayLength());
        Assert.AreEqual(text, parts[0].GetProperty("text").GetString());
        var config = root.GetProperty("embedContentConfig");
        Assert.AreEqual(dimensions, config.GetProperty("outputDimensionality").GetInt32());
        Assert.IsFalse(config.GetProperty("autoTruncate").GetBoolean());
        Assert.IsFalse(config.TryGetProperty("taskType", out _));
        Assert.IsFalse(config.TryGetProperty("title", out _));
        Assert.IsFalse(root.TryGetProperty("taskType", out _));
        Assert.IsFalse(root.TryGetProperty("outputDimensionality", out _));
    }

    private sealed record CapturedRequest(HttpRequestMessage Request, string Body);

    private sealed class RecordingHandler(Func<string, string> response) : HttpMessageHandler
    {
        internal ConcurrentQueue<CapturedRequest> Requests { get; } = new();
        internal ConcurrentQueue<TrackedContent> Responses { get; } = new();
        internal HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        internal string? ReasonPhrase { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Enqueue(new CapturedRequest(request, body));
            var content = new TrackedContent(response(body));
            Responses.Enqueue(content);
            return new HttpResponseMessage(Status) { Content = content, ReasonPhrase = ReasonPhrase };
        }
    }

    private sealed class TrackedContent(string json) : StringContent(json, Encoding.UTF8, "application/json")
    {
        internal bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }

    private sealed class ThrowingHandler(string kind) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw kind switch
            {
                "http" => new HttpRequestException(Secret, new Exception(ApiKey)),
                "io" => new IOException(Secret, new Exception(ApiKey)),
                _ => new OperationCanceledException(Secret, new Exception(ApiKey))
            };
    }

    private sealed class ControlledHandler(params string[] texts) : HttpMessageHandler
    {
        internal Dictionary<string, TaskCompletionSource> Entered { get; } = texts.ToDictionary(text => text, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        internal Dictionary<string, TaskCompletionSource<float>> Release { get; } = texts.ToDictionary(text => text, _ => new TaskCompletionSource<float>(TaskCreationOptions.RunContinuationsAsynchronously));
        private int _active;
        private int _maximum;
        private int _requestCount;
        internal int MaximumActive => _maximum;
        internal int RequestCount => _requestCount;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var text = ReadText(body);
            AssertWireRequest(body, text, 128);
            Interlocked.Increment(ref _requestCount);
            var active = Interlocked.Increment(ref _active);
            int maximum;
            do { maximum = _maximum; } while (active > maximum && Interlocked.CompareExchange(ref _maximum, active, maximum) != maximum);
            Entered[text].TrySetResult();
            try
            {
                var value = await Release[text].Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(VectorJson(value)) };
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
