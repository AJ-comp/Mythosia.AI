using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Mythosia.AI.Rag.Embeddings;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class VoyageContextualizedEmbeddingProviderTests
{
    private const string Secret = "private-voyage-sentinel";

    [TestMethod]
    public async Task Document_PreservesAllOrderedChunksBeyondGenericBatchSize_AndRestoresIndices()
    {
        using var handler = new Handler { Reply = body => Reply(body, reverse: true).ToJsonString() };
        using var http = new HttpClient(handler);
        var provider = new VoyageContextualizedEmbeddingProvider("key", http, dimensions: 256);
        var chunks = Enumerable.Range(1, 137).Select(index => index.ToString()).ToArray();

        var vectors = await provider.GetDocumentEmbeddingsAsync(new EmbeddingDocument("local-id", chunks, "local-title"));

        Assert.HasCount(chunks.Length, vectors);
        for (var index = 0; index < chunks.Length; index++) Assert.AreEqual(index + 1f, vectors[index][0]);
        Assert.HasCount(1, handler.Bodies);
        var body = handler.Bodies.Single();
        Assert.AreEqual("document", body["input_type"]!.GetValue<string>());
        Assert.HasCount(1, body["inputs"]!.AsArray());
        CollectionAssert.AreEqual(chunks, body["inputs"]![0]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.AreEqual(false, body["enable_auto_chunking"]!.GetValue<bool>());
        Assert.AreEqual("float", body["output_dtype"]!.GetValue<string>());
        Assert.AreEqual(256, body["output_dimension"]!.GetValue<int>());
        Assert.AreEqual("voyage-context-4", body["model"]!.GetValue<string>());
        Assert.IsFalse(body.ContainsKey("truncation"));
        Assert.IsFalse(body.ContainsKey("title"));
        Assert.IsFalse(body.ToJsonString().Contains("local-id", StringComparison.Ordinal));
        Assert.AreEqual("https://api.voyageai.com/v1/contextualizedembeddings", handler.Requests.Single().RequestUri!.AbsoluteUri);
        Assert.AreEqual(HttpMethod.Post, handler.Requests.Single().Method);
        await AssertDisposed(handler);
    }

    [TestMethod]
    public async Task RetrievalQuery_UsesSingletonQueryPurpose_GenericCallsKeepInputsIndependent()
    {
        using var handler = new Handler { Reply = body => Reply(body, reverse: true).ToJsonString() };
        using var http = new HttpClient(handler);
        var provider = new VoyageContextualizedEmbeddingProvider("key", http, dimensions: 512);

        Assert.AreEqual(1f, (await provider.GetQueryEmbeddingAsync("1"))[0]);
        Assert.AreEqual(2f, (await provider.GetEmbeddingAsync("2"))[0]);
        var vectors = await provider.GetEmbeddingsAsync(new[] { "3", "4", "5" });

        CollectionAssert.AreEqual(new[] { 3f, 4f, 5f }, vectors.Select(vector => vector[0]).ToArray());
        Assert.AreEqual("query", handler.Bodies[0]["input_type"]!.GetValue<string>());
        Assert.HasCount(1, handler.Bodies[0]["inputs"]!.AsArray());
        Assert.HasCount(1, handler.Bodies[0]["inputs"]![0]!.AsArray());
        Assert.IsFalse(handler.Bodies[1].ContainsKey("input_type"));
        Assert.IsFalse(handler.Bodies[2].ContainsKey("input_type"));
        Assert.HasCount(3, handler.Bodies[2]["inputs"]!.AsArray());
        Assert.IsTrue(handler.Bodies[2]["inputs"]!.AsArray().All(group => group!.AsArray().Count == 1));
        Assert.IsTrue(handler.Bodies.All(body => body["output_dimension"]!.GetValue<int>() == 512));
        Assert.IsTrue(vectors.All(vector => vector.Length == 512));
        await AssertDisposed(handler);
    }

    [TestMethod]
    public async Task ConstructorAndRequests_PreserveCallerHttpClientConfiguration()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://caller.example/"), Timeout = TimeSpan.FromSeconds(41) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        http.DefaultRequestHeaders.Add("X-Caller", "preserved");
        var provider = new VoyageContextualizedEmbeddingProvider("provider-token", http, timeout: TimeSpan.FromSeconds(5));

        var first = await provider.GetQueryEmbeddingAsync("1");
        var second = await provider.GetQueryEmbeddingAsync("2");

        Assert.AreEqual(1024, provider.Dimensions);
        Assert.AreEqual(1024, first.Length);
        Assert.AreEqual(1024, second.Length);
        Assert.AreEqual("https://caller.example/", http.BaseAddress.AbsoluteUri);
        Assert.AreEqual(TimeSpan.FromSeconds(41), http.Timeout);
        Assert.AreEqual("Bearer caller-token", http.DefaultRequestHeaders.Authorization.ToString());
        Assert.AreEqual("preserved", http.DefaultRequestHeaders.GetValues("X-Caller").Single());
        Assert.IsTrue(handler.Requests.All(request => request.Headers.Authorization!.ToString() == "Bearer provider-token"));
    }

    [TestMethod]
    public async Task ValidationAndEmptyInputs_DoNotSendHttpRequests()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var provider = new VoyageContextualizedEmbeddingProvider("key", http);
        Assert.IsEmpty(await provider.GetEmbeddingsAsync(Array.Empty<string>()));
        Assert.IsEmpty(await provider.GetDocumentEmbeddingsAsync(new EmbeddingDocument("empty", Array.Empty<string>())));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetEmbeddingsAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetDocumentEmbeddingsAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetQueryEmbeddingAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetEmbeddingAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetEmbeddingsAsync(Enumerable.Repeat("1", 1001)));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetDocumentEmbeddingsAsync(new EmbeddingDocument("large", Enumerable.Repeat("1", 16001))));
        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetEmbeddingsAsync(Array.Empty<string>(), new CancellationToken(true)));
        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetDocumentEmbeddingsAsync(new EmbeddingDocument("empty", Array.Empty<string>()), new CancellationToken(true)));
        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetQueryEmbeddingAsync("1", new CancellationToken(true)));
        Assert.IsEmpty(handler.Bodies);
    }

    [TestMethod]
    public async Task BatchEnumeration_RejectsAtFirstExcessInputWithoutReadingIt()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var provider = new VoyageContextualizedEmbeddingProvider("key", http);
        var inputs = new ObservedInputs(call => call <= 20000);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => provider.GetEmbeddingsAsync(inputs));

        Assert.AreEqual("texts", error.ParamName);
        Assert.AreEqual(1001, inputs.MoveNextCalls);
        Assert.AreEqual(1000, inputs.CurrentReads);
        Assert.IsTrue(inputs.WasDisposed);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BatchEnumeration_CancellationInMoveNextStopsBeforeReadingCurrent(bool hasValue)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var provider = new VoyageContextualizedEmbeddingProvider("key", http);
        var inputs = new ObservedInputs(call => { cancellation.Cancel(); return hasValue && call == 1; });

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetEmbeddingsAsync(inputs, cancellation.Token));

        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.AreEqual(1, inputs.MoveNextCalls);
        Assert.AreEqual(0, inputs.CurrentReads);
        Assert.IsTrue(inputs.WasDisposed);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task BatchEnumeration_CancellationInCurrentStopsBeforeNextMove()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var provider = new VoyageContextualizedEmbeddingProvider("key", http);
        var inputs = new ObservedInputs(call => call <= 3,
            _ => { cancellation.Cancel(); return "text"; });

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetEmbeddingsAsync(inputs, cancellation.Token));

        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.AreEqual(1, inputs.MoveNextCalls);
        Assert.AreEqual(1, inputs.CurrentReads);
        Assert.IsTrue(inputs.WasDisposed);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task BatchEnumeration_CancellationOnDisposeDoesNotReturnEmptySuccess()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var provider = new VoyageContextualizedEmbeddingProvider("key", http);
        var inputs = new ObservedInputs(_ => false, onDispose: cancellation.Cancel);

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetEmbeddingsAsync(inputs, cancellation.Token));

        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.IsTrue(inputs.WasDisposed);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(" \t")]
    public async Task BatchEnumeration_InvalidTextDoesNotConsumeRemainingInput(string? invalid)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var inputs = new ObservedInputs(call => call <= 10, _ => invalid!);

        await Assert.ThrowsAsync<ArgumentException>(() => new VoyageContextualizedEmbeddingProvider("key", http).GetEmbeddingsAsync(inputs));

        Assert.AreEqual(1, inputs.MoveNextCalls);
        Assert.AreEqual(1, inputs.CurrentReads);
        Assert.IsTrue(inputs.WasDisposed);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task BatchEnumeration_ExactLimitPreservesOrderAndUsesOneRequest()
    {
        using var handler = new Handler { Reply = body => Reply(body, reverse: true).ToJsonString() };
        using var http = new HttpClient(handler);
        var inputs = new ObservedInputs(call => call <= 1000);

        var vectors = await new VoyageContextualizedEmbeddingProvider("key", http, dimensions: 256).GetEmbeddingsAsync(inputs);

        Assert.AreEqual(1001, inputs.MoveNextCalls);
        Assert.AreEqual(1000, inputs.CurrentReads);
        Assert.IsTrue(inputs.WasDisposed);
        Assert.HasCount(1000, vectors);
        for (var index = 0; index < vectors.Count; index++) Assert.AreEqual(index + 1f, vectors[index][0]);
        Assert.HasCount(1, handler.Bodies);
        Assert.HasCount(1000, handler.Bodies[0]["inputs"]!.AsArray());
    }

    [TestMethod]
    public async Task TokenLimits_AreLeftToService_WithoutCharacterEstimatesOrTruncation()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var provider = new VoyageContextualizedEmbeddingProvider("key", http, dimensions: 256);
        var original = new string('a', 150000) + " preserved ending";

        await provider.GetDocumentEmbeddingsAsync(new EmbeddingDocument("id", new[] { original }));

        Assert.AreEqual(original, handler.Bodies.Single()["inputs"]![0]![0]!.GetValue<string>());
        Assert.HasCount(1, handler.Bodies);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(128)]
    [DataRow(257)]
    [DataRow(2049)]
    public void UnsupportedDimensions_AreRejected(int dimensions)
    {
        using var http = new HttpClient(new Handler());
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoyageContextualizedEmbeddingProvider("key", http, dimensions: dimensions));
    }

    [TestMethod]
    public void InvalidConfiguration_IsRejectedWithoutEchoingSecrets()
    {
        using var http = new HttpClient(new Handler());
        foreach (var key in new[] { "", " ", Secret + "\r\nHeader: injected", Secret + "\t" })
        {
            var error = Assert.Throws<ArgumentException>(() => new VoyageContextualizedEmbeddingProvider(key, http));
            AssertSafe(error);
        }
        foreach (var model in new[] { "", " ", "https://example.com/" + Secret, "../" + Secret, Secret + "?key=", Secret + "\n" })
        {
            var error = Assert.Throws<ArgumentException>(() => new VoyageContextualizedEmbeddingProvider("key", http, model));
            AssertSafe(error);
        }
        Assert.Throws<ArgumentNullException>(() => new VoyageContextualizedEmbeddingProvider("key", null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoyageContextualizedEmbeddingProvider("key", http, timeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoyageContextualizedEmbeddingProvider("key", http, timeout: TimeSpan.FromMilliseconds(-2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoyageContextualizedEmbeddingProvider("key", http, timeout: TimeSpan.FromDays(30)));
    }

    [TestMethod]
    [DataRow("outer-count")]
    [DataRow("outer-missing-index")]
    [DataRow("outer-duplicate-index")]
    [DataRow("outer-negative-index")]
    [DataRow("outer-large-index")]
    [DataRow("outer-string-index")]
    [DataRow("outer-fractional-index")]
    [DataRow("inner-count")]
    [DataRow("inner-missing-index")]
    [DataRow("inner-duplicate-index")]
    [DataRow("inner-negative-index")]
    [DataRow("inner-large-index")]
    [DataRow("inner-string-index")]
    [DataRow("inner-fractional-index")]
    [DataRow("missing-vector")]
    [DataRow("null-vector")]
    [DataRow("short-vector")]
    [DataRow("long-vector")]
    [DataRow("string-coordinate")]
    [DataRow("null-coordinate")]
    [DataRow("overflow-coordinate")]
    [DataRow("wrong-model")]
    [DataRow("malformed-json")]
    [DataRow("invalid-root")]
    public async Task InvalidResponse_RejectsIndicesShapeAndCoordinatesWithoutExposingRemoteData(string fault)
    {
        using var handler = new Handler { Reply = body => Corrupt(Reply(body), fault) };
        using var http = new HttpClient(handler);
        var provider = new VoyageContextualizedEmbeddingProvider(Secret, http, dimensions: 256);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fault.StartsWith("outer", StringComparison.Ordinal)
            ? provider.GetEmbeddingsAsync(new[] { "1", "2" })
            : provider.GetDocumentEmbeddingsAsync(new EmbeddingDocument("id", new[] { "1", "2" })));
        StringAssert.Contains(error.Message, "Voyage");
        AssertSafe(error);
        await AssertDisposed(handler);
    }

    [TestMethod]
    public async Task ExtraResponseFields_AndMissingOptionalModel_AreAccepted()
    {
        using var handler = new Handler
        {
            Reply = body =>
            {
                var response = Reply(body);
                response.Remove("model");
                response["future-field"] = Secret;
                response["data"]![0]!["metadata"] = new JsonObject { ["future"] = true };
                response["data"]![0]!["data"]![0]!["text"] = Secret;
                return response.ToJsonString();
            }
        };
        using var http = new HttpClient(handler);
        Assert.AreEqual(1f, (await new VoyageContextualizedEmbeddingProvider("key", http).GetQueryEmbeddingAsync("1"))[0]);
    }

    [TestMethod]
    public async Task HttpFailure_ReportsStatusAndLimitHintWithoutBodyOrReasonPhrase()
    {
        using var handler = new Handler { Status = HttpStatusCode.BadRequest, Reply = _ => Secret, Reason = Secret };
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new VoyageContextualizedEmbeddingProvider(Secret, http).GetQueryEmbeddingAsync(Secret));
        StringAssert.Contains(error.Message, "HTTP 400");
        StringAssert.Contains(error.Message, "token limits");
        AssertSafe(error);
        await AssertDisposed(handler);
    }

    [TestMethod]
    public async Task TransportFailure_DoesNotExposeDiagnosticMessagesOrInnerExceptions()
    {
        using var handler = new Handler { BeforeResponse = _ => throw new HttpRequestException(Secret, new IOException(Secret)) };
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new VoyageContextualizedEmbeddingProvider(Secret, http).GetQueryEmbeddingAsync(Secret));
        AssertSafe(error);
        await AssertDisposed(handler);
    }

    [TestMethod]
    public async Task CancellationDuringRequest_PreservesCallerTokenAndSanitizesException()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler
        {
            BeforeResponse = async token =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { throw new OperationCanceledException(Secret, new IOException(Secret), token); }
            }
        };
        using var http = new HttpClient(handler);
        var pending = new VoyageContextualizedEmbeddingProvider(Secret, http).GetQueryEmbeddingAsync(Secret, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        AssertSafe(error);
        await AssertDisposed(handler);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationAndTimeout_ApplyWhileBufferingResponse(bool useTimeout)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new StreamingHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(50) };
        var provider = new VoyageContextualizedEmbeddingProvider("key", http, timeout: useTimeout ? TimeSpan.FromMilliseconds(200) : null);
        var pending = provider.GetQueryEmbeddingAsync("1", cancellation.Token);
        await handler.Content.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (useTimeout)
        {
            var error = await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
            AssertSafe(error);
        }
        else
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        Assert.AreEqual(TimeSpan.FromSeconds(50), http.Timeout);
        Assert.IsTrue(handler.Content.WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request!.Content!.ReadAsStringAsync());
    }

    private static void AssertSafe(Exception error)
    {
        Assert.IsFalse(error.ToString().Contains(Secret, StringComparison.Ordinal));
        Assert.IsNull(error.InnerException);
    }

    private static async Task AssertDisposed(Handler handler)
    {
        Assert.IsTrue(handler.Responses.All(content => content.WasDisposed));
        foreach (var request in handler.Requests)
            await Assert.ThrowsAsync<ObjectDisposedException>(() => request.Content!.ReadAsStringAsync());
    }

    private static JsonObject Reply(JsonObject request, bool reverse = false)
    {
        var dimensions = request["output_dimension"]!.GetValue<int>();
        var groups = request["inputs"]!.AsArray().Select((group, index) =>
        {
            var chunks = group!.AsArray().Select((text, chunkIndex) =>
            {
                var vector = new float[dimensions];
                vector[0] = float.TryParse(text!.GetValue<string>(), out var value) ? value : 1;
                return (JsonNode)new JsonObject { ["index"] = chunkIndex, ["embedding"] = new JsonArray(vector.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()) };
            }).ToArray();
            if (reverse) Array.Reverse(chunks);
            return (JsonNode)new JsonObject { ["index"] = index, ["data"] = new JsonArray(chunks) };
        }).ToArray();
        if (reverse) Array.Reverse(groups);
        return new JsonObject { ["model"] = request["model"]!.GetValue<string>(), ["data"] = new JsonArray(groups) };
    }

    private static string Corrupt(JsonObject response, string fault)
    {
        var outer = response["data"]!.AsArray();
        var inner = outer[0]!["data"]!.AsArray();
        var items = fault.StartsWith("outer", StringComparison.Ordinal) ? outer : inner;
        if (fault.EndsWith("-count", StringComparison.Ordinal)) items.RemoveAt(0);
        else if (fault.EndsWith("-missing-index", StringComparison.Ordinal)) items[0]!.AsObject().Remove("index");
        else if (fault.EndsWith("-duplicate-index", StringComparison.Ordinal)) items[1]!["index"] = 0;
        else if (fault.EndsWith("-negative-index", StringComparison.Ordinal)) items[0]!["index"] = -1;
        else if (fault.EndsWith("-large-index", StringComparison.Ordinal)) items[0]!["index"] = items.Count;
        else if (fault.EndsWith("-string-index", StringComparison.Ordinal)) items[0]!["index"] = Secret;
        else if (fault.EndsWith("-fractional-index", StringComparison.Ordinal)) items[0]!["index"] = 0.5;
        else if (fault == "missing-vector") inner[0]!.AsObject().Remove("embedding");
        else if (fault == "null-vector") inner[0]!["embedding"] = null;
        else if (fault == "short-vector") inner[0]!["embedding"]!.AsArray().RemoveAt(0);
        else if (fault == "long-vector") inner[0]!["embedding"]!.AsArray().Add(0);
        else if (fault == "string-coordinate") inner[0]!["embedding"]![0] = Secret;
        else if (fault == "null-coordinate") inner[0]!["embedding"]![0] = null;
        else if (fault == "overflow-coordinate") inner[0]!["embedding"]![0] = "OVERFLOW";
        else if (fault == "wrong-model") response["model"] = Secret;
        else if (fault == "malformed-json") return "{\"" + Secret + "\":";
        else if (fault == "invalid-root") return "null";
        return response.ToJsonString().Replace("\"OVERFLOW\"", "1e1000", StringComparison.Ordinal);
    }

    private sealed class ObservedInputs(Func<int, bool> moveNext, Func<int, string>? current = null,
        Action? onDispose = null) : IEnumerable<string>, IEnumerator<string>
    {
        internal int MoveNextCalls { get; private set; }
        internal int CurrentReads { get; private set; }
        internal bool WasDisposed { get; private set; }
        public string Current
        {
            get { CurrentReads++; return current == null ? MoveNextCalls.ToString() : current(MoveNextCalls); }
        }
        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => moveNext(++MoveNextCalls);
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { WasDisposed = true; onDispose?.Invoke(); }
        public IEnumerator<string> GetEnumerator() => this;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal Func<JsonObject, string> Reply { get; init; } = body => VoyageContextualizedEmbeddingProviderTests.Reply(body).ToJsonString();
        internal Func<CancellationToken, Task>? BeforeResponse { get; init; }
        internal HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        internal string? Reason { get; init; }
        internal List<JsonObject> Bodies { get; } = [];
        internal List<HttpRequestMessage> Requests { get; } = [];
        internal List<TrackedContent> Responses { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Bodies.Add(body);
            if (BeforeResponse != null) await BeforeResponse(cancellationToken);
            var content = new TrackedContent(Reply(body));
            Responses.Add(content);
            return new HttpResponseMessage(Status) { Content = content, ReasonPhrase = Reason };
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
