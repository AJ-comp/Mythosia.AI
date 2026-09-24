using System.Net;
using System.Text;
using System.Text.Json;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class OllamaEmbeddingProviderTests
{
    private const string Secret = "private-ollama-response-sentinel";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DefaultProvider_Requests1024InsteadOfReceivingNative2560(bool batch)
    {
        // A compliant server uses the model's native dimension when the field is absent.
        // This reproduces the original mismatch without downloading the model.
        using var handler = new DimensionAwareHandler();
        using var http = new HttpClient(handler);
        var provider = new OllamaEmbeddingProvider(http);
        var inputs = batch ? new[] { "alpha", "서울", "C++" } : new[] { "alpha" };

        var vectors = batch
            ? await provider.GetEmbeddingsAsync(inputs)
            : new[] { await provider.GetEmbeddingAsync(inputs[0]) };

        Assert.AreEqual(1024, provider.Dimensions);
        Assert.AreEqual(inputs.Length, vectors.Count);
        Assert.IsTrue(vectors.All(vector => vector.Length == 1024));
        Assert.AreEqual("qwen3-embedding:4b", handler.Models.Single());
        Assert.AreEqual(1024, handler.Dimensions.Single());
        CollectionAssert.AreEqual(inputs, handler.Inputs.Single());
        Assert.AreEqual("http://localhost:11434/api/embed", handler.Requests.Single().RequestUri!.AbsoluteUri);
        Assert.IsTrue(handler.Responses.Single().WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Requests.Single().Content!.ReadAsStringAsync());
    }

    [TestMethod]
    [DataRow("qwen3-embedding:4b", 32, 2560, false)]
    [DataRow("qwen3-embedding:4b", 2560, 2560, true)]
    [DataRow("embeddinggemma", 768, 768, false)]
    [DataRow("custom-embedding-model", 128, 512, true)]
    public async Task ConfiguredDimensions_AreRequestedForSingleAndBatchInputs(string model, int dimensions, int nativeDimensions, bool batch)
    {
        using var handler = new DimensionAwareHandler { NativeDimensions = nativeDimensions };
        using var http = new HttpClient(handler);
        var provider = new OllamaEmbeddingProvider(http, model, dimensions, "http://unused.test/ollama/");
        var inputs = batch ? new[] { "alpha", "beta", "gamma" } : new[] { "alpha" };

        var vectors = batch
            ? await provider.GetEmbeddingsAsync(inputs)
            : new[] { await provider.GetEmbeddingAsync(inputs[0]) };

        Assert.AreEqual(dimensions, provider.Dimensions);
        Assert.AreEqual(inputs.Length, vectors.Count);
        for (var index = 0; index < vectors.Count; index++)
        {
            Assert.AreEqual(dimensions, vectors[index].Length);
            Assert.AreEqual(1f, vectors[index][index]);
        }
        Assert.AreEqual(dimensions, handler.Dimensions.Single());
        Assert.AreEqual(model, handler.Models.Single());
        CollectionAssert.AreEqual(inputs, handler.Inputs.Single());
        Assert.AreEqual("http://unused.test/ollama/api/embed", handler.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ServerIgnoringDimensions_RejectsNativeVectorInsteadOfSilentlyResizing(bool batch)
    {
        using var handler = new DimensionAwareHandler { IgnoreDimensions = true };
        using var http = new HttpClient(handler);
        var provider = new OllamaEmbeddingProvider(http);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (batch) await provider.GetEmbeddingsAsync(new[] { "alpha", "beta" });
            else await provider.GetEmbeddingAsync("alpha");
        });

        StringAssert.Contains(exception.Message, "expected 1024, received 2560");
        Assert.AreEqual(1024, provider.Dimensions);
        Assert.AreEqual(1024, handler.Dimensions.Single());
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.IsTrue(handler.Responses.Single().WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Requests.Single().Content!.ReadAsStringAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnsupportedDimensions_FailWithoutChangingTheExistingIndexOrRetrying(bool rejectRequest)
    {
        using var handler = new DimensionAwareHandler
        {
            IgnoreDimensions = !rejectRequest,
            RejectDimensions = rejectRequest
        };
        using var http = new HttpClient(handler);
        using var store = new InMemoryVectorStore();
        var oldVector = new float[1024];
        oldVector[0] = 1;
        await store.UpsertAsync(new VectorRecord
        {
            Id = "policy_chunk_0", Content = "old policy", Vector = oldVector,
            Metadata = new Dictionary<string, string> { ["document_id"] = "policy" }
        });
        var provider = new OllamaEmbeddingProvider(http);
        var pipeline = new RagPipeline(provider, store, new CharacterTextSplitter(), new DefaultContextBuilder());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.IndexDocumentAsync(new RagDocument("policy", "new policy", "inline")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.QueryAsync("alpha"));

        StringAssert.Contains(exception.Message, rejectRequest ? "HTTP 400" : "expected 1024, received 2560");
        Assert.IsFalse(exception.ToString().Contains(Secret, StringComparison.Ordinal));
        var record = (await store.ListAllRecordsAsync()).Single();
        Assert.AreEqual("old policy", record.Content);
        CollectionAssert.AreEqual(oldVector, record.Vector);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Dimensions.All(dimensions => dimensions == 1024));
        Assert.IsTrue(handler.Responses.All(content => content.WasDisposed));
        foreach (var request in handler.Requests)
            await Assert.ThrowsAsync<ObjectDisposedException>(() => request.Content!.ReadAsStringAsync());
    }

    [TestMethod]
    [DynamicData(nameof(InvalidResponses))]
    public async Task InvalidResponses_RejectEveryMalformedVectorWithoutExposingPayload(string scenario, string response, int inputCount)
    {
        using var handler = new ResponseHandler(response);
        using var http = new HttpClient(handler);
        var provider = Provider(http);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetEmbeddingsAsync(Enumerable.Range(0, inputCount).Select(i => "text" + i)));

        StringAssert.Contains(exception.Message, "Ollama embeddings response is invalid");
        Assert.IsFalse(exception.ToString().Contains(Secret, StringComparison.Ordinal), scenario);
        Assert.IsTrue(handler.Responses.All(content => content.WasDisposed));
        await AssertRequestDisposed(handler);
    }

    public static IEnumerable<object[]> InvalidResponses()
    {
        foreach (var item in new (string Scenario, string Json, int Count)[]
        {
            ("malformed_json", "{\"" + Secret + "\":", 1),
            ("null_root", "null", 1), ("array_root", "[]", 1), ("string_root", "\"" + Secret + "\"", 1),
            ("missing_fields", "{}", 1),
            ("null_embeddings", "{\"embeddings\":null}", 1),
            ("object_embeddings", "{\"embeddings\":{}}", 1),
            ("empty_embeddings", "{\"embeddings\":[]}", 1),
            ("too_few", "{\"embeddings\":[[1,0]]}", 2),
            ("too_many", "{\"embeddings\":[[1,0],[0,1]]}", 1),
            ("null_vector", "{\"embeddings\":[null]}", 1),
            ("object_vector", "{\"embeddings\":[{}]}", 1),
            ("empty_vector", "{\"embeddings\":[[]]}", 1),
            ("short_vector", "{\"embeddings\":[[1]]}", 1),
            ("long_vector", "{\"embeddings\":[[1,0,0]]}", 1),
            ("second_short", "{\"embeddings\":[[1,0],[0]]}", 2),
            ("second_null", "{\"embeddings\":[[1,0],null]}", 2),
            ("null_coordinate", "{\"embeddings\":[[null,0]]}", 1),
            ("string_coordinate", "{\"embeddings\":[[\"" + Secret + "\",0]]}", 1),
            ("boolean_coordinate", "{\"embeddings\":[[true,0]]}", 1),
            ("object_coordinate", "{\"embeddings\":[[{},0]]}", 1),
            ("array_coordinate", "{\"embeddings\":[[[],0]]}", 1),
            ("nan_string", "{\"embeddings\":[[\"NaN\",0]]}", 1),
            ("bare_nan", "{\"embeddings\":[[NaN,0]]}", 1),
            ("positive_overflow", "{\"embeddings\":[[1e1000,1]]}", 1),
            ("negative_overflow", "{\"embeddings\":[[-1e1000,1]]}", 1),
            ("second_overflow", "{\"embeddings\":[[1,0],[1e1000,1]]}", 2),
            ("legacy_null", "{\"embedding\":null}", 1),
            ("legacy_object", "{\"embedding\":{}}", 1),
            ("legacy_short", "{\"embedding\":[1]}", 1),
            ("legacy_overflow", "{\"embedding\":[1e1000,1]}", 1),
            ("legacy_batch", "{\"embedding\":[1,0]}", 2),
            ("invalid_plural_cannot_fall_back", "{\"embeddings\":null,\"embedding\":[1,0]}", 1)
        }) yield return new object[] { item.Scenario, item.Json, item.Count };
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ValidSingleResponse_PreservesModernAndLegacyShapes(bool legacy)
    {
        using var handler = new ResponseHandler(legacy ? "{\"embedding\":[1,0]}" : "{\"embeddings\":[[1,0]]}");
        using var http = new HttpClient(handler);
        var provider = Provider(http);

        CollectionAssert.AreEqual(new[] { 1f, 0f }, await provider.GetEmbeddingAsync("alpha"));
        CollectionAssert.AreEqual(new[] { "alpha" }, handler.Inputs.Single());
        Assert.AreEqual("http://unused.test/api/embed", handler.Requests.Single().RequestUri!.AbsoluteUri);
        Assert.IsTrue(handler.Responses.Single().WasDisposed);
        await AssertRequestDisposed(handler);

        // Disposing messages must not dispose the caller's reusable HttpClient.
        CollectionAssert.AreEqual(new[] { 1f, 0f }, await provider.GetEmbeddingAsync("second"));
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ValidBatch_RetainsInputOrderAndOwnsIndependentFiniteBuffers()
    {
        using var handler = new ResponseHandler("{\"embeddings\":[[3.4028235e38,0],[0,-3.4028235e38]]}");
        using var http = new HttpClient(handler);
        var provider = Provider(http);
        var first = await provider.GetEmbeddingsAsync(new[] { "alpha", "beta" });
        var second = await provider.GetEmbeddingsAsync(new[] { "alpha", "beta" });
        Assert.AreEqual(float.MaxValue, first[0][0]);
        Assert.AreEqual(-float.MaxValue, first[1][1]);
        Assert.AreNotSame(first[0], first[1]);
        first[0][0] = 0;
        Assert.AreEqual(float.MaxValue, second[0][0]);
        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, handler.Inputs.First());
    }

    [TestMethod]
    public async Task MalformedOllamaVectors_RejectQueryAndPreserveThePreviousIndex()
    {
        using var handler = new ResponseHandler("{\"embeddings\":[[1e1000,1]]}");
        using var http = new HttpClient(handler);
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(new VectorRecord
        {
            Id = "policy_chunk_0", Content = "old policy", Vector = [1, 0],
            Metadata = new Dictionary<string, string> { ["document_id"] = "policy" }
        });
        var pipeline = new RagPipeline(Provider(http), store, new CharacterTextSplitter(), new DefaultContextBuilder());

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.QueryAsync("alpha"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.IndexDocumentAsync(new RagDocument("policy", "new policy", "inline")));

        Assert.AreEqual("old policy", (await store.ListAllRecordsAsync()).Single().Content);
    }

    [TestMethod]
    public async Task HttpFailure_ReportsStatusAndDisposesMessagesWithoutExposingServerPayload()
    {
        using var handler = new ResponseHandler(Secret) { Status = HttpStatusCode.BadGateway };
        using var http = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(http).GetEmbeddingAsync("alpha"));
        StringAssert.Contains(exception.Message, "502");
        Assert.IsFalse(exception.ToString().Contains(Secret, StringComparison.Ordinal));
        Assert.IsTrue(handler.Responses.Single().WasDisposed);
        await AssertRequestDisposed(handler);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AlreadyCanceledRequests_DoNotSendEvenForEmptyInputs(bool empty)
    {
        using var handler = new ResponseHandler("{}");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Provider(http).GetEmbeddingsAsync(empty ? Array.Empty<string>() : new[] { "alpha" }, new CancellationToken(true)));
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task EmptyBatch_DoesNotSendHttpRequest()
    {
        using var handler = new ResponseHandler("{}");
        using var http = new HttpClient(handler);
        Assert.AreEqual(0, (await Provider(http).GetEmbeddingsAsync(Array.Empty<string>())).Count);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task CancellationDuringHttpRequest_IsPropagatedAndRequestIsDisposed()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ResponseHandler("{}")
        {
            BeforeResponse = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
        };
        using var http = new HttpClient(handler);
        var pending = Provider(http).GetEmbeddingAsync("alpha", cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        }
        finally { cancellation.Cancel(); }
        await AssertRequestDisposed(handler);
        Assert.AreEqual(0, handler.Responses.Count);
    }

    [TestMethod]
    public async Task CancellationWhileBufferingResponse_DisposesRequestAndResponseContent()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new StreamingHandler();
        using var http = new HttpClient(handler);
        var pending = Provider(http).GetEmbeddingAsync("alpha", cancellation.Token);
        try
        {
            await handler.Content.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        }
        finally { cancellation.Cancel(); }

        Assert.IsTrue(handler.Content.WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request!.Content!.ReadAsStringAsync());
    }

    private static OllamaEmbeddingProvider Provider(HttpClient http) => new(http, dimensions: 2, baseUrl: "http://unused.test/");
    private static async Task AssertRequestDisposed(ResponseHandler handler)
    {
        foreach (var request in handler.Requests)
            await Assert.ThrowsAsync<ObjectDisposedException>(() => request.Content!.ReadAsStringAsync());
    }
    private sealed class DimensionAwareHandler : HttpMessageHandler
    {
        internal int NativeDimensions { get; init; } = 2560;
        internal bool IgnoreDimensions { get; init; }
        internal bool RejectDimensions { get; init; }
        internal List<HttpRequestMessage> Requests { get; } = [];
        internal List<string> Models { get; } = [];
        internal List<int?> Dimensions { get; } = [];
        internal List<string[]> Inputs { get; } = [];
        internal List<TrackedContent> Responses { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            Models.Add(root.GetProperty("model").GetString()!);
            int? requestedDimensions = root.TryGetProperty("dimensions", out var value) ? value.GetInt32() : null;
            Dimensions.Add(requestedDimensions);
            var inputs = root.GetProperty("input").EnumerateArray().Select(item => item.GetString()!).ToArray();
            Inputs.Add(inputs);
            var dimensions = IgnoreDimensions ? NativeDimensions : requestedDimensions ?? NativeDimensions;
            var vectors = inputs.Select((_, index) =>
            {
                var vector = new float[dimensions];
                vector[index % dimensions] = 1;
                return vector;
            }).ToArray();
            var content = new TrackedContent(RejectDimensions
                ? JsonSerializer.Serialize(new { error = Secret })
                : JsonSerializer.Serialize(new { embeddings = vectors }));
            Responses.Add(content);
            return new HttpResponseMessage(RejectDimensions ? HttpStatusCode.BadRequest : HttpStatusCode.OK) { Content = content };
        }
    }
    private sealed class ResponseHandler(string response) : HttpMessageHandler
    {
        internal HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        internal Func<CancellationToken, Task>? BeforeResponse { get; init; }
        internal List<HttpRequestMessage> Requests { get; } = [];
        internal List<string[]> Inputs { get; } = [];
        internal List<TrackedContent> Responses { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Inputs.Add(json.RootElement.GetProperty("input").EnumerateArray().Select(item => item.GetString()!).ToArray());
            if (BeforeResponse != null) await BeforeResponse(cancellationToken);
            var content = new TrackedContent(response);
            Responses.Add(content);
            return new HttpResponseMessage(Status) { Content = content };
        }
    }
    private sealed class TrackedContent(string content) : StringContent(content, Encoding.UTF8, "application/json")
    {
        internal bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
    private sealed class StreamingHandler : HttpMessageHandler
    {
        internal SlowContent Content { get; } = new();
        internal HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = Content });
        }
    }
    private sealed class SlowContent : HttpContent
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool WasDisposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new AssertFailedException("Response buffering must receive the cancellation token.");
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
