using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[DoNotParallelize]
public class ChatUiRetrievalEmbeddingTests
{
    [TestMethod]
    [DataRow("voyage", "voyage-context-4", 256)]
    [DataRow("gemini", "gemini-embedding-2", 128)]
    [DataRow("perplexity", PerplexityEmbeddingModels.Context0_6B, 128)]
    [DataRow("perplexity", PerplexityEmbeddingModels.Context4B, 128)]
    public async Task Factory_IndexAndQuery_PreserveWholeDocumentsAndOriginalStoredText(string provider, string model, int dimensions)
    {
        using var handler = new EmbeddingHandler(dimensions);
        using var http = new HttpClient(handler);
        var embedding = Build(provider, model, dimensions, http);
        Assert.IsInstanceOfType<IRetrievalEmbeddingProvider>(embedding);
        using var vectors = new InMemoryVectorStore();
        var settings = Settings(provider, model, dimensions);
        var store = await RagStore.BuildAsync(builder => builder.AddText("document-a", "a").AddText("document-b", "b")
            .WithTextSplitter(new OrderedSplitter()).UseEmbedding(embedding).UseStore(vectors));
        Assert.AreEqual(134L, await vectors.CountAsync());
        Assert.AreEqual("a-piece-128", (await vectors.GetAsync("a-128"))!.Content);
        var requests = handler.Requests.ToArray();
        if (provider == "gemini")
        {
            Assert.AreEqual(134, requests.Length);
            Assert.IsTrue(requests.All(r => r.GetProperty("content").GetProperty("parts").GetArrayLength() == 1));
            Assert.IsTrue(requests.All(r => r.GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()!.StartsWith("title: ", StringComparison.Ordinal)));
        }
        else
        {
            Assert.AreEqual(2, requests.Length, "A document exceeding the legacy batch size must stay in one contextual request.");
            var groupProperty = provider == "voyage" ? "inputs" : "input";
            foreach (var request in requests) Assert.AreEqual(1, request.GetProperty(groupProperty).GetArrayLength());
            var large = requests.Single(r => r.GetProperty(groupProperty)[0].GetArrayLength() == 129).GetProperty(groupProperty)[0];
            CollectionAssert.AreEqual(Enumerable.Range(0, 129).Select(i => $"a-piece-{i:D3}").ToArray(),
                large.EnumerateArray().Select(e => e.GetString()).ToArray());
            if (provider == "voyage") Assert.IsTrue(requests.All(r => r.GetProperty("input_type").GetString() == "document"));
        }
        // Reconnect the search pipeline to the same dataset without embedding again.
        var queryStore = await ChatUiRagCoreEndpoints.BuildQueryStoreAsync(vectors, embedding, settings, default);
        Assert.AreEqual(requests.Length, handler.Requests.Count);
        var result = await queryStore.QueryAsync("parking");
        Assert.IsTrue(result.References.Count > 0);
        var query = handler.Requests.Last();
        if (provider == "voyage") Assert.AreEqual("query", query.GetProperty("input_type").GetString());
        if (provider == "gemini") Assert.AreEqual("task: search result | query: parking", query.GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [TestMethod]
    [DataRow("openai", "text-embedding-3-small", 128)]
    [DataRow("perplexity", PerplexityEmbeddingModels.Standard0_6B, 128)]
    [DataRow("perplexity", PerplexityEmbeddingModels.Standard4B, 128)]
    public async Task LegacyFactory_RetainsFlatEmbeddingContract(string provider, string model, int dimensions)
    {
        using var handler = new EmbeddingHandler(dimensions);
        using var http = new HttpClient(handler);
        var embedding = Build(provider, model, dimensions, http);
        Assert.IsFalse(embedding is IRetrievalEmbeddingProvider);
        var values = await embedding.GetEmbeddingsAsync(["first", "second"]);
        Assert.AreEqual(2, values.Count);
        var input = handler.Requests.Single().GetProperty("input");
        CollectionAssert.AreEqual(new[] { "first", "second" }, input.EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [TestMethod]
    [DataRow("voyage", "voyage-context-4", 1536, 120, 4)]
    [DataRow("gemini", "gemini-embedding-2", 127, 120, 4)]
    [DataRow("gemini", "gemini-embedding-2", 3073, 120, 4)]
    [DataRow("gemini", "gemini-embedding-2", 128, 0, 4)]
    [DataRow("gemini", "gemini-embedding-2", 128, 601, 4)]
    [DataRow("gemini", "gemini-embedding-2", 128, 120, 0)]
    [DataRow("gemini", "gemini-embedding-2", 128, 120, 17)]
    [DataRow("perplexity", PerplexityEmbeddingModels.Context0_6B, 1025, 120, 4)]
    [DataRow("perplexity", "unrecognized-model", 128, 120, 4)]
    [DataRow("openai", "text-embedding-ada-002", 128, 120, 4)]
    public void Factory_RejectsInvalidConfigurationWithoutSendingRequests(string provider, string model, int dimensions, int timeout, int concurrency)
    {
        using var handler = new EmbeddingHandler(dimensions);
        using var http = new HttpClient(handler);
        Assert.Throws<ArgumentException>(() => Build(provider, model, dimensions, http, timeout, concurrency));
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    [DataRow("voyage", "voyage-context-4", 256)]
    [DataRow("gemini", "gemini-embedding-2", 128)]
    [DataRow("perplexity", PerplexityEmbeddingModels.Context0_6B, 128)]
    public void Factory_RequiresSelectedProviderKey(string provider, string model, int dimensions)
    {
        using var http = new HttpClient();
        Assert.Throws<InvalidOperationException>(() => ChatUiUtilityHelpers.BuildRagEmbeddingProvider(provider,
            "unrelated-openai-key", null, http, model, dimensions, ""));
    }

    [TestMethod]
    [DataRow("voyage", "voyage-context-4", 256)]
    [DataRow("gemini", "gemini-embedding-2", 128)]
    [DataRow("perplexity", PerplexityEmbeddingModels.Context0_6B, 128)]
    public async Task Cancellation_ReachesHttpAndKeepsExistingDocumentVectors(string provider, string model, int dimensions)
    {
        using var handler = new EmbeddingHandler(dimensions) { Block = true };
        using var http = new HttpClient(handler);
        var embedding = Build(provider, model, dimensions, http);
        using var vectors = new InMemoryVectorStore();
        await vectors.UpsertAsync(new VectorRecord { Id = "old", Vector = Enumerable.Repeat(1f, dimensions).ToArray(), Content = "original", Metadata = new() { ["document_id"] = "a" } });
        using var cancellation = new CancellationTokenSource();
        var pending = RagStore.BuildAsync(builder => builder.AddText("replacement", "a").WithTextSplitter(new OrderedSplitter()).UseEmbedding(embedding).UseStore(vectors), cancellationToken: cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
        await handler.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("original", (await vectors.GetAsync("old"))!.Content);
        Assert.AreEqual(1L, await vectors.CountAsync());
    }

    [TestMethod]
    [DataRow("voyage", "voyage-context-4", 256)]
    [DataRow("gemini", "gemini-embedding-2", 128)]
    [DataRow("perplexity", PerplexityEmbeddingModels.Context0_6B, 128)]
    public async Task ConfiguredTimeout_CancelsHttpWithoutChangingSharedClient(string provider, string model, int dimensions)
    {
        using var handler = new EmbeddingHandler(dimensions) { Block = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(600) };
        var embedding = Build(provider, model, dimensions, http, timeout: 1);
        await Assert.ThrowsAsync<TimeoutException>(() => embedding.GetEmbeddingAsync("hello"));
        Assert.AreEqual(TimeSpan.FromSeconds(600), http.Timeout);
        Assert.IsTrue(handler.Canceled.Task.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task GeminiConcurrency_IsAppliedToActualHttpRequests()
    {
        using var handler = new EmbeddingHandler(128) { DelayMilliseconds = 20 };
        using var http = new HttpClient(handler);
        var embedding = Build("gemini", "gemini-embedding-2", 128, http, concurrency: 2);
        await embedding.GetEmbeddingsAsync(Enumerable.Range(0, 12).Select(i => "input-" + i));
        Assert.AreEqual(2, handler.MaximumActive);
    }

    [TestMethod]
    public async Task QueryReconfiguration_IsAtomicAndDoesNotReembedOrDiscardIndexOnMissingKey()
    {
        using var handler = new EmbeddingHandler(128);
        using var http = new HttpClient(handler);
        using var vectors = new InMemoryVectorStore();
        var settings = Settings("gemini", "gemini-embedding-2", 128);
        var embedding = Build("gemini", "gemini-embedding-2", 128, http);
        var store = await ChatUiRagCoreEndpoints.BuildQueryStoreAsync(vectors, embedding, settings, default);
        var state = new RagReferenceState();
        state.UpdateSettings(settings);
        state.SetExternalStore(store, settings, vectors, "inmemory");
        var request = VectorRequest() with { GeminiApiKey = "offline", EmbeddingProvider = "gemini", EmbeddingModel = "gemini-embedding-2", EmbeddingDimensions = 128 };
        var changed = settings with { EmbeddingTimeoutSeconds = 42, EmbeddingMaxConcurrency = 2 };
        Assert.IsNull(await ChatUiRagCoreEndpoints.EnsureExternalStoreMatchesSettingsAsync(state, http, changed, request));
        Assert.AreNotSame(store, state.Store);
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(42, state.ActiveSettings!.EmbeddingTimeoutSeconds);
        var current = state.Store;
        Assert.IsNull(await ChatUiRagCoreEndpoints.EnsureExternalStoreMatchesSettingsAsync(state, http, changed, request));
        Assert.AreSame(current, state.Store, "Unchanged requests reuse the provider and its Gemini concurrency gate.");
        var warning = await ChatUiRagCoreEndpoints.EnsureExternalStoreMatchesSettingsAsync(state, http, changed, request with { GeminiApiKey = null });
        Assert.IsNotNull(warning);
        Assert.AreSame(current, state.Store, "A missing replacement key must not discard an in-memory dataset.");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => ChatUiRagCoreEndpoints.EnsureExternalStoreMatchesSettingsAsync(state, http, settings, request, canceled.Token));
        Assert.AreSame(current, state.Store);
    }

    [TestMethod]
    public async Task ModelChange_RequiresReindexButTimeoutChangeDoesNot()
    {
        using var vectors = new InMemoryVectorStore();
        using var http = new HttpClient(new EmbeddingHandler(128));
        var original = Settings("perplexity", PerplexityEmbeddingModels.Standard0_6B, 128);
        var store = await ChatUiRagCoreEndpoints.BuildQueryStoreAsync(vectors, Build("perplexity", original.EmbeddingModel, 128, http), original, default);
        var state = new RagReferenceState();
        state.SetExternalStore(store, original, vectors, "inmemory");
        state.UpdateSettings(original);
        Assert.IsFalse(state.RequiresReindex);
        Assert.IsFalse(state.RequiresReindexFor(original with { EmbeddingTimeoutSeconds = 60, EmbeddingMaxConcurrency = 2 }));
        var changed = original with { EmbeddingModel = PerplexityEmbeddingModels.Context0_6B };
        Assert.IsTrue(state.RequiresReindexFor(changed), "Same dimensions do not make standard and contextual vector spaces interchangeable.");
        await Assert.ThrowsAsync<RagReindexRequiredException>(() => ChatUiRagCoreEndpoints.EnsureExternalStoreMatchesSettingsAsync(state, http, changed, VectorRequest()));
        Assert.AreSame(store, state.Store);
    }

    [TestMethod]
    public async Task QueryReconfiguration_RebuildsForRerankerChangesWithoutEmbeddingAgain()
    {
        using var handler = new EmbeddingHandler(128);
        using var http = new HttpClient(handler);
        using var vectors = new InMemoryVectorStore();
        var initial = Settings("gemini", "gemini-embedding-2", 128);
        var store = await ChatUiRagCoreEndpoints.BuildQueryStoreAsync(vectors, Build("gemini", initial.EmbeddingModel, 128, http), initial, default);
        var state = new RagReferenceState();
        state.UpdateSettings(initial);
        state.SetExternalStore(store, initial, vectors, "inmemory");
        var request = VectorRequest() with { GeminiApiKey = "offline" };
        var configurations = new[]
        {
            initial with { RerankEnabled = true, RerankProvider = "cohere", RerankModel = "rerank-v3.5", RerankApiKey = "first" },
            initial with { RerankEnabled = true, RerankProvider = "cohere", RerankModel = "rerank-v3.5", RerankApiKey = "rotated" },
            initial with { RerankEnabled = true, RerankProvider = "cohere", RerankModel = "rerank-english-v3.0", RerankApiKey = "rotated" },
            initial
        };
        foreach (var settings in configurations)
        {
            var previous = state.Store;
            Assert.IsNull(await ChatUiRagCoreEndpoints.EnsureExternalStoreMatchesSettingsAsync(state, http, settings, request));
            Assert.AreNotSame(previous, state.Store);
            Assert.AreEqual(settings.RerankEnabled, state.ActiveSettings!.RerankEnabled);
            var same = state.Store;
            Assert.IsNull(await ChatUiRagCoreEndpoints.EnsureExternalStoreMatchesSettingsAsync(state, http, settings, request));
            Assert.AreSame(same, state.Store);
        }
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public void DatasetIdentity_IgnoresCredentialsOrderingAndOtherProviders()
    {
        var first = VectorRequest() with { Provider = "postgres", ConnectionString = "Host=LOCALHOST;Database=rag;Username=one;Password=old", TableName = "vectors", SchemaName = "public" };
        var rotated = first with { ConnectionString = "Password=new;Username=two;Database=rag;Host=localhost;Port=5432", QdrantHost = "ignored" };
        Assert.AreEqual(ChatUiRagCoreEndpoints.GetVectorStoreIdentity(first), ChatUiRagCoreEndpoints.GetVectorStoreIdentity(rotated));
        Assert.AreNotEqual(ChatUiRagCoreEndpoints.GetVectorStoreIdentity(first), ChatUiRagCoreEndpoints.GetVectorStoreIdentity(first with { TableName = "new_vectors" }));
        var qdrant = first with { Provider = "qdrant", QdrantHost = "localhost", QdrantPort = 6334, QdrantCollectionName = "documents" };
        Assert.AreEqual(ChatUiRagCoreEndpoints.GetVectorStoreIdentity(qdrant), ChatUiRagCoreEndpoints.GetVectorStoreIdentity(qdrant with { ConnectionString = "unrelated", QdrantApiKey = "rotated" }));
    }

    [TestMethod]
    public async Task TrackingStore_DelegatesAtomicReplacementAndOnlyRecordsSuccessfulWrites()
    {
        var backend = new AtomicProbeStore();
        var trace = new List<VectorRecord>();
        IVectorStore tracking = new TrackingVectorStore(backend, trace);
        var records = new[] { new VectorRecord { Id = "new", Vector = [1f], Content = "replacement" } };
        var filter = new VectorFilter().Where("document_id", "document");
        using var token = new CancellationTokenSource();
        await tracking.ReplaceByFilterAsync(filter, records, token.Token);
        Assert.AreEqual(1, backend.AtomicCalls);
        Assert.AreSame(filter, backend.Filter);
        Assert.AreEqual(token.Token, backend.Token);
        Assert.AreSame(records[0], trace.Single());
        backend.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => tracking.ReplaceByFilterAsync(filter, records));
        Assert.AreEqual(1, trace.Count);
    }

    [TestMethod]
    [DataRow("voyage", "voyage-context-4", 1024, "VoyageContextualizedEmbeddingProvider")]
    [DataRow("gemini", "gemini-embedding-2", 1536, "GeminiEmbeddingProvider")]
    [DataRow("perplexity", PerplexityEmbeddingModels.Context4B, 128, "PerplexityContextualizedEmbeddingProvider")]
    public void CodeExport_PreservesProviderAndExecutionConfiguration(string provider, string model, int dimensions, string type)
    {
        var config = new RagReferenceConfig(["manual.txt"], 300, 30, "recursive", provider, model, dimensions, "",
            new RagFilter { TopK = 5 }, new RagRetrievalDerivation(), null, 37, 2);
        var code = ChatUiUtilityHelpers.GenerateRagReferenceCodeSnippet(config);
        StringAssert.Contains(code, type);
        StringAssert.Contains(code, model);
        StringAssert.Contains(code, "TimeSpan.FromSeconds(37)");
        if (provider == "gemini") StringAssert.Contains(code, "maxConcurrency: 2");
    }

    private static IEmbeddingProvider Build(string provider, string model, int dimensions, HttpClient http, int timeout = 120, int concurrency = 4)
        => ChatUiUtilityHelpers.BuildRagEmbeddingProvider(provider, "offline", "offline", http, model, dimensions, "", "offline", "offline", timeout, concurrency);

    private static RagPipelineSettings Settings(string provider, string model, int dimensions)
        => new RagPipelineSettings() with { EmbeddingProvider = provider, EmbeddingModel = model, EmbeddingDimensions = dimensions,
            HybridSearchEnabled = false, QueryRewriterEnabled = false, FinalFilter = new RagFilter { TopK = 5, MinScore = 0 } };

    private static VectorStoreConfigRequest VectorRequest() => new("inmemory", null, null, null, null, null);

    private sealed class OrderedSplitter : ITextSplitter
    {
        public IReadOnlyList<RagChunk> Split(RagDocument document)
            => Enumerable.Range(0, document.Id == "a" ? 129 : 5).Select(i => new RagChunk($"{document.Id}-{i}", document.Id, $"{document.Id}-piece-{i:D3}", i)).ToArray();
    }

    private sealed class EmbeddingHandler(int dimensions) : HttpMessageHandler
    {
        internal ConcurrentQueue<JsonElement> Requests { get; } = new();
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Block { get; init; }
        internal int DelayMilliseconds { get; init; }
        internal int MaximumActive;
        private int _active;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = json.RootElement.Clone();
            Requests.Enqueue(body);
            var active = Interlocked.Increment(ref _active);
            int observed;
            while ((observed = MaximumActive) < active && Interlocked.CompareExchange(ref MaximumActive, active, observed) != observed) { }
            Started.TrySetResult();
            try
            {
                if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
                if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds, cancellationToken);
                var vector = Enumerable.Repeat(1f / MathF.Sqrt(dimensions), dimensions).ToArray();
                object response;
                if (body.TryGetProperty("content", out _)) response = new { embedding = new { values = vector } };
                else if (body.TryGetProperty("inputs", out var groups)) response = new { data = groups.EnumerateArray().Select((group, i) => new { index = i, data = group.EnumerateArray().Select((_, j) => new { index = j, embedding = vector }).ToArray() }).ToArray() };
                else if (request.RequestUri!.Host == "api.perplexity.ai")
                {
                    var encoded = Convert.ToBase64String(Enumerable.Repeat((byte)1, dimensions).ToArray());
                    var input = body.GetProperty("input");
                    response = request.RequestUri.AbsolutePath.EndsWith("contextualizedembeddings", StringComparison.Ordinal)
                        ? new { model = body.GetProperty("model").GetString(), data = (object)input.EnumerateArray().Select((group, i) => new { index = i, data = group.EnumerateArray().Select((_, j) => new { index = j, embedding = encoded }).ToArray() }).ToArray() }
                        : new { model = body.GetProperty("model").GetString(), data = (object)input.EnumerateArray().Select((_, i) => new { index = i, embedding = encoded }).ToArray() };
                }
                else response = new { data = body.GetProperty("input").EnumerateArray().Select((_, i) => new { index = i, embedding = vector }).ToArray() };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
            }
            catch (OperationCanceledException) { Canceled.TrySetResult(); throw; }
            finally { Interlocked.Decrement(ref _active); json.Dispose(); }
        }
    }

    private sealed class AtomicProbeStore : IVectorStore
    {
        public int AtomicCalls;
        public bool Fail;
        public VectorFilter? Filter;
        public CancellationToken Token;
        public Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
        { AtomicCalls++; Filter = filter; Token = cancellationToken; return Fail ? Task.FromException(new InvalidOperationException("controlled failure")) : Task.CompletedTask; }
        public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default) => throw new AssertFailedException("Atomic replacement must not call upsert.");
        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default) => throw new AssertFailedException("Atomic replacement must not call batch upsert.");
        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) => throw new AssertFailedException("Atomic replacement must not delete separately.");
        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default) => throw new AssertFailedException("Atomic replacement must not delete separately.");
    }
}
