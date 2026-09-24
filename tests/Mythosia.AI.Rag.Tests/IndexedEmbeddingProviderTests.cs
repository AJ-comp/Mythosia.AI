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
public sealed class IndexedEmbeddingProviderTests
{
    private const string Secret = "private-response-body-and-key-sentinel";
    private const string Ordered = """{"data":[{"index":0,"embedding":[1,0]},{"index":1,"embedding":[0,1]}]}""";
    private const string Reversed = """{"data":[{"index":1,"embedding":[0,1]},{"index":0,"embedding":[1,0]}]}""";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ResponseIndices_MapVectorsToInputsAndSearchReturnsTheCorrectDocument(bool vllm)
    {
        using var handler = new ResponseHandler(_ => Reversed);
        using var http = new HttpClient(handler);
        using var store = new InMemoryVectorStore();
        var pipeline = Pipeline(CreateProvider(vllm, http), store);

        await pipeline.IndexDocumentAsync(new RagDocument("policy", "Alpha Beta", "inline"));

        var records = (await store.ListAllRecordsAsync()).OrderBy(record => record.Id).ToArray();
        CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, records.Select(record => record.Content).ToArray());
        CollectionAssert.AreEqual(new[] { 1f, 0f }, records[0].Vector);
        CollectionAssert.AreEqual(new[] { 0f, 1f }, records[1].Vector);
        var first = await store.SearchAsync(new[] { 1f, 0f }, 1);
        var second = await store.SearchAsync(new[] { 0f, 1f }, 1);
        Assert.AreEqual("Alpha", first.Single().Record.Content);
        Assert.AreEqual("Beta", second.Single().Record.Content);
        CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, handler.Inputs.Single());
        Assert.IsTrue(handler.Contents.All(content => content.WasDisposed));
    }

    [TestMethod]
    [DynamicData(nameof(InvalidResponses))]
    public async Task InvalidResponse_IsRejectedBeforeReplacingThePreviousIndex(bool vllm, string scenario, string response)
    {
        using var handler = new ResponseHandler(_ => response);
        using var http = new HttpClient(handler);
        using var store = new InMemoryVectorStore();
        await SeedExistingRecords(store);
        var before = await Snapshot(store);
        var pipeline = Pipeline(CreateProvider(vllm, http), store);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.IndexDocumentAsync(new RagDocument("policy", "Alpha Beta", "inline")));

        Assert.IsTrue(exception.Message.Contains("embeddings response", StringComparison.OrdinalIgnoreCase), scenario);
        Assert.IsFalse(exception.ToString().Contains(Secret, StringComparison.Ordinal), scenario);
        Assert.AreEqual(before, await Snapshot(store), scenario);
        Assert.AreEqual(1, handler.Calls, scenario);
        Assert.IsTrue(handler.Contents.All(content => content.WasDisposed), scenario);
    }

    public static IEnumerable<object[]> InvalidResponses()
    {
        const string first = """{"index":0,"embedding":[1,0]}""";
        var cases = new (string Name, string Json)[]
        {
            ("malformed_json", "{\"" + Secret + "\":"),
            ("null_root", "null"),
            ("array_root", "[]"),
            ("missing_data", "{}"),
            ("nonarray_data", "{\"data\":null}"),
            ("fewer_items", "{\"data\":[" + first + "]}"),
            ("empty_items", "{\"data\":[]}"),
            ("extra_item", """{"data":[{"index":0,"embedding":[1,0]},{"index":1,"embedding":[0,1]},{"index":2,"embedding":[1,1]}]}"""),
            ("null_item", Data(first, "null")),
            ("nonobject_item", Data(first, "[]")),
            ("duplicate_index", Data(first, """{"index":0,"embedding":[0,1]}""")),
            ("negative_index", Data(first, """{"index":-1,"embedding":[0,1]}""")),
            ("out_of_range_index", Data(first, """{"index":2,"embedding":[0,1]}""")),
            ("integer_overflow_index", Data(first, """{"index":2147483648,"embedding":[0,1]}""")),
            ("fractional_index", Data(first, """{"index":1.5,"embedding":[0,1]}""")),
            ("string_index", Data(first, """{"index":"1","embedding":[0,1]}""")),
            ("null_index", Data(first, """{"index":null,"embedding":[0,1]}""")),
            ("boolean_index", Data(first, """{"index":true,"embedding":[0,1]}""")),
            ("mixed_missing_index_after", Data(first, """{"embedding":[0,1]}""")),
            ("mixed_missing_index_before", Data("""{"embedding":[1,0]}""", """{"index":1,"embedding":[0,1]}""")),
            ("missing_embedding", Data(first, """{"index":1}""")),
            ("null_embedding", Data(first, """{"index":1,"embedding":null}""")),
            ("nonarray_embedding", Data(first, """{"index":1,"embedding":{}}""")),
            ("wrong_dimension_after_valid_vector", Data(first, """{"index":1,"embedding":[1]}""")),
            ("extra_dimension", Data(first, """{"index":1,"embedding":[1,0,0]}""")),
            ("empty_embedding", Data(first, """{"index":1,"embedding":[]}""")),
            ("null_coordinate", Data(first, """{"index":1,"embedding":[null,1]}""")),
            ("boolean_coordinate", Data(first, """{"index":1,"embedding":[true,1]}""")),
            ("object_coordinate", Data(first, """{"index":1,"embedding":[{},1]}""")),
            ("string_coordinate", Data(first, "{\"index\":1,\"embedding\":[\"" + Secret + "\",1]}")),
            ("nan_string", Data(first, """{"index":1,"embedding":["NaN",1]}""")),
            ("positive_overflow", Data(first, """{"index":1,"embedding":[1e1000,1]}""")),
            ("negative_overflow", Data(first, """{"index":1,"embedding":[-1e1000,1]}""")),
            ("bare_nan_invalid_json", Data(first, """{"index":1,"embedding":[NaN,1]}"""))
        };
        foreach (var vllm in new[] { false, true })
            foreach (var item in cases)
                yield return new object[] { vllm, item.Name, item.Json };
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EntirelyMissingIndices_PreservesOnlyTheVllmCompatibilityMode(bool vllm)
    {
        using var handler = new ResponseHandler(_ => """{"data":[{"embedding":[1,0]},{"embedding":[0,1]}]}""");
        using var http = new HttpClient(handler);
        var provider = CreateProvider(vllm, http);
        if (!vllm)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetEmbeddingsAsync(new[] { "Alpha", "Beta" }));
            return;
        }
        var vectors = await provider.GetEmbeddingsAsync(new[] { "Alpha", "Beta" });
        CollectionAssert.AreEqual(new[] { 1f, 0f }, vectors[0]);
        CollectionAssert.AreEqual(new[] { 0f, 1f }, vectors[1]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LaterBatchFailure_PreservesAllPreviouslyStoredChunks(bool vllm)
    {
        using var handler = new ResponseHandler(call => call == 1 ? Ordered :
            """{"data":[{"index":0,"embedding":[1,1]},{"index":0,"embedding":[-1,1]}]}""");
        using var http = new HttpClient(handler);
        using var store = new InMemoryVectorStore();
        await SeedExistingRecords(store);
        var before = await Snapshot(store);
        var pipeline = Pipeline(CreateProvider(vllm, http), store);
        pipeline.Options.EmbeddingBatchSize = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.IndexDocumentAsync(new RagDocument("policy", "Alpha Beta Gamma Delta", "inline")));

        Assert.AreEqual(2, handler.Calls);
        Assert.AreEqual(before, await Snapshot(store));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExtraResponseItem_IsRejectedBeforeASecondBatchCanShiftTheMappings(bool vllm)
    {
        using var handler = new ResponseHandler(_ => """{"data":[{"index":0,"embedding":[1,0]},{"index":1,"embedding":[0,1]},{"index":2,"embedding":[99,99]}]}""");
        using var http = new HttpClient(handler);
        using var store = new InMemoryVectorStore();
        await SeedExistingRecords(store);
        var before = await Snapshot(store);
        var pipeline = Pipeline(CreateProvider(vllm, http), store);
        pipeline.Options.EmbeddingBatchSize = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.IndexDocumentAsync(new RagDocument("policy", "Alpha Beta Gamma Delta", "inline")));

        Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual(before, await Snapshot(store));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HttpErrors_ExposeStatusWithoutServerPayloadOrRequestKey(bool vllm)
    {
        using var handler = new ResponseHandler(_ => Secret) { Status = HttpStatusCode.BadGateway };
        using var http = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateProvider(vllm, http).GetEmbeddingAsync("Alpha"));
        Assert.IsTrue(exception.Message.Contains("502", StringComparison.Ordinal));
        Assert.IsFalse(exception.ToString().Contains(Secret, StringComparison.Ordinal));
        Assert.IsTrue(handler.Contents.Single().WasDisposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyInputs_DoNotSendRequestsAndCanceledInputsStayCanceled(bool vllm)
    {
        using var handler = new ResponseHandler(_ => throw new AssertFailedException("No HTTP request is expected."));
        using var http = new HttpClient(handler);
        var provider = CreateProvider(vllm, http);
        Assert.AreEqual(0, (await provider.GetEmbeddingsAsync(Array.Empty<string>())).Count);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetEmbeddingsAsync(new[] { "Alpha" }, canceled.Token));
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FiniteFloatExtremes_AreAcceptedWithoutReusingVectorBuffers(bool vllm)
    {
        using var handler = new ResponseHandler(_ => """{"data":[{"index":1,"embedding":[0,-3.4028235e38]},{"index":0,"embedding":[3.4028235e38,0]}]}""");
        using var http = new HttpClient(handler);
        var provider = CreateProvider(vllm, http);
        var first = await provider.GetEmbeddingsAsync(new[] { "Alpha", "Beta" });
        Assert.AreEqual(float.MaxValue, first[0][0]);
        Assert.AreEqual(-float.MaxValue, first[1][1]);
        Assert.AreNotSame(first[0], first[1]);
        var second = await provider.GetEmbeddingsAsync(new[] { "Alpha", "Beta" });
        first[0][0] = 0;
        Assert.AreEqual(float.MaxValue, second[0][0]);
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, -1)]
    [DataRow(true, 0)]
    [DataRow(true, -1)]
    public void InvalidDimensions_AreRejectedBeforeAnyRequest(bool vllm, int dimensions)
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateProvider(vllm, http, dimensions));
    }

    private static string Data(string first, string second) => "{\"data\":[" + first + "," + second + "]}";
    private static IEmbeddingProvider CreateProvider(bool vllm, HttpClient http, int dimensions = 2)
        => vllm ? new VllmEmbeddingProvider(http, dimensions: dimensions, baseUrl: "http://unused.test")
            : new OpenAIEmbeddingProvider(Secret, http, dimensions: dimensions);
    private static RagPipeline Pipeline(IEmbeddingProvider provider, IVectorStore store)
        => new(provider, store, new TokenTextSplitter(1, 0), new DefaultContextBuilder());
    private static async Task SeedExistingRecords(InMemoryVectorStore store)
    {
        foreach (var item in new[] { ("policy_chunk_0", "policy", "OLD"), ("policy_chunk_1", "policy", "TAIL"), ("other_chunk_0", "other", "KEEP") })
            await store.UpsertAsync(new VectorRecord
            {
                Id = item.Item1, Content = item.Item3, Vector = new[] { 1f, 0f },
                Metadata = new Dictionary<string, string> { ["document_id"] = item.Item2 }
            });
    }
    private static async Task<string> Snapshot(InMemoryVectorStore store)
        => JsonSerializer.Serialize((await store.ListAllRecordsAsync()).OrderBy(record => record.Id));

    private sealed class ResponseHandler(Func<int, string> response) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        internal List<string[]> Inputs { get; } = [];
        internal List<TrackedContent> Contents { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Inputs.Add(body.RootElement.GetProperty("input").EnumerateArray().Select(item => item.GetString()!).ToArray());
            var content = new TrackedContent(response(Calls));
            Contents.Add(content);
            return new HttpResponseMessage(Status) { Content = content };
        }
    }
    private sealed class TrackedContent(string content) : StringContent(content, Encoding.UTF8, "application/json")
    {
        internal bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
