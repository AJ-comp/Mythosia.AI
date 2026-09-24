using Mythosia.AI.Rag.Retrieval;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class QueryEmbeddingContractTests
{
    [TestMethod]
    [DynamicData(nameof(InvalidCases))]
    public async Task InvalidQueryVectors_FailBeforeRetrievalOrItsProgressCallback(string mode, string invalid)
    {
        using var store = CreateStore(mode);
        await Seed(store);
        var embeddings = new ProbeProvider { Result = InvalidVector(invalid) };
        var pipeline = Pipeline(mode, embeddings, store);
        var stages = new List<RagProgressStage>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.QueryAsync("alpha",
            new RagQueryOptions { ProgressAsync = stage => { stages.Add(stage); return Task.CompletedTask; } }));

        StringAssert.Contains(exception.Message, "query embedding");
        Assert.AreEqual(1, embeddings.Calls);
        Assert.AreEqual(0, store.SearchCalls);
        Assert.IsFalse(stages.Contains(RagProgressStage.Retrieval));
        Assert.AreEqual(2, await store.Inner.CountAsync());
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var mode in Modes)
            foreach (var invalid in new[] { "null", "empty", "short", "long", "nan", "positive-infinity", "negative-infinity" })
                yield return new object[] { mode, invalid };
    }

    [TestMethod]
    [DynamicData(nameof(InvalidDimensions))]
    public async Task InvalidDeclaredDimensions_FailBeforeRequestingEmbeddings(string mode, int dimensions)
    {
        using var store = CreateStore(mode);
        var embeddings = new ProbeProvider { Dimensions = dimensions };
        var pipeline = Pipeline(mode, embeddings, store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.QueryAsync("alpha"));

        Assert.AreEqual(0, embeddings.Calls);
        Assert.AreEqual(0, store.SearchCalls);
    }

    public static IEnumerable<object[]> InvalidDimensions()
    {
        foreach (var mode in Modes)
            foreach (var dimensions in new[] { 0, -1 })
                yield return new object[] { mode, dimensions };
    }

    [TestMethod]
    [DataRow("vector")]
    [DataRow("hybrid-native")]
    [DataRow("hybrid-fallback")]
    [DataRow("legacy")]
    public async Task ReusedBuffer_TwoOverlappingQueriesRetainTheirOwnMeaning(string mode)
    {
        using var store = CreateStore(mode);
        await Seed(store);
        var embeddings = new ReusingProvider();
        var pipeline = Pipeline(mode, embeddings, store);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = pipeline.QueryAsync("alpha", new RagQueryOptions
        {
            FinalFilter = new RagFilter { TopK = 1 },
            ProgressAsync = async stage =>
            {
                if (stage == RagProgressStage.Retrieval)
                {
                    entered.SetResult();
                    await resume.Task;
                }
            }
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Provider calls are sequential; only query processing overlaps. The second
            // call may reuse the first completed call's buffer, never a concurrently read one.
            var second = await pipeline.QueryAsync("beta", 1);
            Assert.AreEqual("beta", second.SearchResults.Single().Record.Id);
        }
        finally { resume.TrySetResult(); }
        Assert.AreEqual("alpha", (await first).SearchResults.Single().Record.Id);
    }

    [TestMethod]
    [DataRow("vector")]
    [DataRow("hybrid-native")]
    [DataRow("hybrid-fallback")]
    [DataRow("legacy")]
    public async Task CancellationAfterEmbedding_DoesNotReachTheStore(string mode)
    {
        using var canceled = new CancellationTokenSource();
        using var store = CreateStore(mode);
        var embeddings = new ProbeProvider { OnCall = canceled.Cancel };
        var pipeline = Pipeline(mode, embeddings, store);

        await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.QueryAsync("alpha", cancellationToken: canceled.Token));

        Assert.AreEqual(1, embeddings.Calls);
        Assert.AreEqual(0, store.SearchCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KeywordOnly_DoesNotInspectOrCallTheEmbeddingProvider(bool zeroWeight)
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("alpha", [1, 0]));
        var embeddings = new UnavailableProvider();
        var rag = await RagStore.BuildAsync(builder =>
        {
            builder.UseStore(store).UseEmbedding(embeddings);
            if (zeroWeight) builder.UseHybridSearch(0);
            else builder.UseKeywordSearch();
        });

        Assert.AreEqual("alpha", (await rag.QueryAsync("alpha")).References.Single().Record.Id);
    }

    private static readonly string[] Modes = ["vector", "hybrid-native", "hybrid-fallback", "legacy"];
    private static float[]? InvalidVector(string invalid) => invalid switch
    {
        "null" => null, "empty" => [], "short" => [1], "long" => [1, 0, 0],
        "nan" => [float.NaN, 1], "positive-infinity" => [float.PositiveInfinity, 1],
        _ => [float.NegativeInfinity, 1]
    };
    private static RagPipeline Pipeline(string mode, IEmbeddingProvider provider, TrackingStore store)
    {
        var pipeline = new RagPipeline(provider, store, new CharacterTextSplitter(), new DefaultContextBuilder());
        if (mode.StartsWith("hybrid", StringComparison.Ordinal))
            pipeline.SetRetriever(new RagRetrievers.Hybrid(provider, store, new HybridSearchOptions { VectorWeight = .9f }));
        else if (mode == "legacy") pipeline.SetRetrievalStrategy(new Strategy(store));
        return pipeline;
    }
    private static TrackingStore CreateStore(string mode) => mode == "hybrid-native" ? new NativeStore() : new TrackingStore();
    private static async Task Seed(TrackingStore store)
    {
        await store.UpsertAsync(Record("alpha", [1, 0]));
        await store.UpsertAsync(Record("beta", [0, 1]));
    }
    private static VectorRecord Record(string id, float[] vector) => new() { Id = id, Content = id + " policy", Vector = vector };

    private sealed class ProbeProvider : IEmbeddingProvider
    {
        public int Dimensions { get; set; } = 2;
        internal int Calls { get; private set; }
        internal float[]? Result { get; init; } = [1, 0];
        internal Action? OnCall { get; init; }
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            OnCall?.Invoke();
            return Task.FromResult(Result!);
        }
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Querying must not request a batch.");
    }
    private sealed class ReusingProvider : IEmbeddingProvider
    {
        private readonly float[] _buffer = new float[2];
        public int Dimensions => 2;
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            _buffer[0] = text == "alpha" ? 1 : 0;
            _buffer[1] = text == "beta" ? 1 : 0;
            return Task.FromResult(_buffer);
        }
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Querying must not request a batch.");
    }
    private sealed class UnavailableProvider : IEmbeddingProvider
    {
        public int Dimensions => throw new AssertFailedException("Keyword search must not inspect embedding dimensions.");
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) => throw new AssertFailedException("No embedding is needed.");
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) => throw new AssertFailedException("No embedding is needed.");
    }
    private sealed class Strategy(IVectorStore store) : IRetrievalStrategy
    {
        public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(float[] denseVector, string? query, int topK, VectorFilter? filter = null, CancellationToken cancellationToken = default)
            => store.SearchAsync(denseVector, topK, filter, cancellationToken);
    }
    private class TrackingStore : IVectorStore, ITextSearchStore, IDisposable
    {
        internal InMemoryVectorStore Inner { get; } = new();
        internal int SearchCalls { get; set; }
        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        { SearchCalls++; return Inner.SearchAsync(queryVector, topK, filter, cancellationToken); }
        public Task<IReadOnlyList<VectorSearchResult>> TextSearchAsync(string query, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        { SearchCalls++; return Inner.TextSearchAsync(query, topK, filter, cancellationToken); }
        public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default) => Inner.UpsertAsync(record, cancellationToken);
        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default) => Inner.UpsertBatchAsync(records, cancellationToken);
        public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) => Inner.GetAsync(id, filter, cancellationToken);
        public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) => Inner.DeleteAsync(id, filter, cancellationToken);
        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default) => Inner.DeleteByFilterAsync(filter, cancellationToken);
        public void Dispose() => Inner.Dispose();
    }
    private sealed class NativeStore : TrackingStore, IConfigurableHybridSearchStore
    {
        public Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(float[] denseVector, string query, HybridSearchOptions options, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        { SearchCalls++; return Inner.HybridSearchAsync(denseVector, query, options, topK, filter, cancellationToken); }
    }
}
