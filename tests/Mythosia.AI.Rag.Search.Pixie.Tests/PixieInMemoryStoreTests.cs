using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services;
using Mythosia.VectorDb;
using System.Reflection;

namespace Mythosia.AI.Rag.Search.Pixie.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class PixieInMemoryStoreTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task SparseSearch_UsesNeuralWeightsAndFiltersBeforeTopK()
    {
        var store = CreateStore(new Dictionary<string, PixieSparseVector>
        {
            ["question"] = Sparse((1, 2), (2, 3)),
            ["lexically unrelated document"] = Sparse((1, 4), (2, 5)),
            ["other tenant"] = Sparse((1, 100)),
            ["no overlap"] = Sparse((9, 10))
        });
        await store.UpsertBatchAsync(new[]
        {
            Record("target", "lexically unrelated document", "A"),
            Record("foreign", "other tenant", "B"),
            Record("irrelevant", "no overlap", "A")
        });

        var result = await store.TextSearchAsync("question", 1, Tenant("A"));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("target", result[0].Record.Id);
        Assert.AreEqual(23d, result[0].Score, 1e-12);
        Assert.AreEqual(0, (await store.TextSearchAsync("question", 10, Tenant("A").WithMinScore(24))).Count);
    }

    [TestMethod]
    public async Task UpsertAndDelete_RemoveStaleSparsePostings()
    {
        var store = TokenStore();
        await store.UpsertAsync(Record("x", "old"));
        await store.UpsertAsync(Record("x", "new"));
        Assert.AreEqual(0, (await store.TextSearchAsync("old")).Count);
        Assert.AreEqual("new", (await store.TextSearchAsync("new"))[0].Record.Content);
        await store.DeleteAsync("x", Tenant("other"));
        Assert.AreEqual(1, (await store.TextSearchAsync("new")).Count);
        await store.DeleteAsync("x", Tenant("A"));
        Assert.AreEqual(0, (await store.TextSearchAsync("new")).Count);
        Assert.AreEqual(0L, await store.CountAsync());
    }

    [TestMethod]
    public async Task DuplicateIds_LastWinsWithoutDuplicatePostingOrCount()
    {
        var encoded = new List<string>();
        var store = new PixieInMemoryStore((_, _) => Task.FromResult(Sparse((1, 1))), (text, _) =>
        {
            encoded.Add(text);
            return Task.FromResult(Sparse((1, text == "last" ? 3 : 1)));
        });
        await store.UpsertBatchAsync(new[] { Record("x", "first"), Record("x", "last") });
        CollectionAssert.AreEqual(new[] { "last" }, encoded);
        Assert.AreEqual(1L, await store.CountAsync());
        var result = await store.TextSearchAsync("anything");
        Assert.AreEqual(3d, result[0].Score);
        Assert.AreEqual("last", result[0].Record.Content);
    }

    [TestMethod]
    public async Task InputAndResultMutation_DoesNotChangeStoredTenantVectorsOrContents()
    {
        var store = TokenStore();
        var input = Record("x", "old");
        await store.UpsertAsync(input);
        input.Metadata["tenant"] = "B";
        input.Vector[0] = -1;
        input.Content = "changed";
        var fetched = (await store.GetAsync("x"))!;
        fetched.Metadata["tenant"] = "C";
        fetched.Vector[0] = -2;
        fetched.Content = "changed again";
        var batch = await store.GetBatchAsync(new[] { "x" });
        batch[0].Metadata.Clear();
        var search = await store.SearchAsync(new[] { 1f, 0f });
        search[0].Record.Metadata.Clear();
        search[0].Record.Vector[0] = -3;
        var sparse = await store.TextSearchAsync("old");
        sparse[0].Record.Metadata.Clear();

        var remaining = (await store.GetAsync("x", Tenant("A")))!;
        Assert.AreEqual("old", remaining.Content);
        Assert.AreEqual(1f, remaining.Vector[0]);
        Assert.IsNull(await store.GetAsync("x", Tenant("B")));
        Assert.AreEqual(1d, (await store.SearchAsync(new[] { 1f, 0f }, filter: Tenant("A")))[0].Score);
    }

    [TestMethod]
    public async Task EntireInputBatch_IsCopiedBeforeFirstEncoderAwait()
    {
        var started = Signal();
        var release = Signal();
        var seen = new List<string>();
        var store = new PixieInMemoryStore((_, _) => Task.FromResult(Sparse((1, 1))), async (text, _) =>
        {
            seen.Add(text);
            if (seen.Count == 1) { started.SetResult(); await release.Task; }
            return Sparse((1, 1));
        });
        var first = Record("first", "first");
        var second = Record("second", "second");
        var pending = store.UpsertBatchAsync(new[] { first, second });
        await started.Task.WaitAsync(Timeout);
        second.Content = "mutated";
        second.Metadata["tenant"] = "B";
        second.Vector[0] = -1;
        Assert.AreEqual(0L, await store.CountAsync());
        release.SetResult();
        await pending.WaitAsync(Timeout);
        CollectionAssert.AreEqual(new[] { "first", "second" }, seen);
        var saved = (await store.GetAsync("second", Tenant("A")))!;
        Assert.AreEqual("second", saved.Content);
        Assert.AreEqual(1f, saved.Vector[0]);
    }

    [TestMethod]
    public async Task FailedEncoding_PreservesEntireUpsertBatchAndReplacement()
    {
        var store = new PixieInMemoryStore((_, _) => Task.FromResult(Sparse((1, 1))), (text, _) =>
            text == "fail" ? Task.FromException<PixieSparseVector>(new InvalidOperationException("encoding failed"))
                : Task.FromResult(Sparse((1, 1))));
        await store.UpsertAsync(Record("old", "original"));
        var replacement = new[] { Record("old", "modified"), Record("new", "fail") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpsertBatchAsync(replacement));
        Assert.AreEqual("original", (await store.GetAsync("old"))!.Content);
        Assert.IsNull(await store.GetAsync("new"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceByFilterAsync(Tenant("A"), replacement));
        Assert.AreEqual("original", (await store.GetAsync("old"))!.Content);
        Assert.AreEqual("original", (await store.TextSearchAsync("query"))[0].Record.Content);
    }

    [TestMethod]
    public async Task CancellationAfterEncoderReturns_PreservesExistingData()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new PixieInMemoryStore((_, _) => Task.FromResult(Sparse((1, 1))), (text, _) =>
        {
            if (text == "cancel") cancellation.Cancel();
            return Task.FromResult(Sparse((1, 1)));
        });
        await store.UpsertAsync(Record("old", "original"));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.ReplaceByFilterAsync(Tenant("A"),
            new[] { Record("old", "cancel") }, cancellation.Token));
        Assert.AreEqual("original", (await store.GetAsync("old"))!.Content);
    }

    [TestMethod]
    public async Task EmptyReplacement_RemovesOnlyMatchingDocumentsAndPostings()
    {
        var store = TokenStore();
        await store.UpsertBatchAsync(new[] { Record("a", "old", "A"), Record("b", "old", "B") });
        await store.ReplaceByFilterAsync(Tenant("A"), Array.Empty<VectorRecord>());
        Assert.IsNull(await store.GetAsync("a"));
        Assert.AreEqual("b", (await store.TextSearchAsync("old"))[0].Record.Id);
        Assert.AreEqual(1L, await store.CountAsync());
    }

    [TestMethod]
    public async Task ScopedReplacement_RejectsCrossTenantIdsAndNonmatchingRecordsAtomically()
    {
        var store = TokenStore();
        await store.UpsertBatchAsync(new[] { Record("a", "old", "A"), Record("b", "old", "B") });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceByFilterAsync(Tenant("A"),
            new[] { Record("b", "new", "A") }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReplaceByFilterAsync(Tenant("A"),
            new[] { Record("a", "new", "B") }));
        Assert.AreEqual("old", (await store.GetAsync("a", Tenant("A")))!.Content);
        Assert.AreEqual("old", (await store.GetAsync("b", Tenant("B")))!.Content);
    }

    [TestMethod]
    public async Task ConcurrentCommits_PreserveBothCompletedBatches()
    {
        var started = Signal();
        var release = Signal();
        var store = new PixieInMemoryStore((_, _) => Task.FromResult(Sparse((1, 1))), async (text, _) =>
        {
            if (text == "slow") { started.SetResult(); await release.Task; }
            return Sparse((1, 1));
        });
        var slow = store.UpsertAsync(Record("slow", "slow"));
        await started.Task.WaitAsync(Timeout);
        await store.UpsertAsync(Record("fast", "fast"));
        release.SetResult();
        await slow.WaitAsync(Timeout);
        CollectionAssert.AreEquivalent(new[] { "slow", "fast" },
            (await store.TextSearchAsync("query")).Select(result => result.Record.Id).ToArray());
    }

    [TestMethod]
    public async Task PendingHybridSearch_UsesOneDataAndFilterSnapshot()
    {
        var started = Signal();
        var release = Signal();
        var store = new PixieInMemoryStore(async (_, _) =>
        {
            started.SetResult();
            await release.Task;
            return Sparse((1, 1));
        }, (_, _) => Task.FromResult(Sparse((1, 1))));
        await store.UpsertBatchAsync(new[] { Record("a", "old", "A"), Record("b", "old", "B") });
        var tenants = new[] { "A" };
        var filter = new VectorFilter().WhereIn("tenant", tenants);
        var settings = new HybridSearchOptions { VectorWeight = 0.5f };
        var query = new[] { 1f, 0f };
        var pending = store.HybridSearchAsync(query, "query", settings, 5, filter);
        await started.Task.WaitAsync(Timeout);
        tenants[0] = "B";
        filter.Where("tenant", "B").WithMinScore(2);
        settings.VectorWeight = 0;
        query[0] = -1;
        await store.ReplaceByFilterAsync(Tenant("A"), new[] { Record("new", "replacement", "A") });
        release.SetResult();
        var results = await pending.WaitAsync(Timeout);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("a", results[0].Record.Id);
        Assert.AreEqual("old", results[0].Record.Content);
        Assert.AreEqual(1d, results[0].Score, 1e-12);
    }

    [TestMethod]
    public async Task WeightedRrf_UsesRanksAndAppliesMinScoreOnlyAfterFusion()
    {
        var store = CreateStore(new Dictionary<string, PixieSparseVector>
        {
            ["q"] = Sparse((1, 1)), ["a"] = Sparse((1, 1)), ["b"] = Sparse((1, 2))
        });
        await store.UpsertBatchAsync(new[] { Record("a", "a", vector: new[] { 1f, 0f }), Record("b", "b", vector: new[] { 0f, 1f }) });
        var options = new HybridSearchOptions { VectorWeight = 0.75f, CandidateMultiplier = 2, RrfK = 1 };
        var results = await store.HybridSearchAsync(new[] { 1f, 0f }, "q", options, 2);
        Assert.AreEqual("a", results[0].Record.Id);
        Assert.AreEqual(2 * (0.75 / 2 + 0.25 / 3), results[0].Score, 1e-12);
        Assert.AreEqual(2 * (0.75 / 3 + 0.25 / 2), results[1].Score, 1e-12);
        var filtered = await store.HybridSearchAsync(new[] { 1f, 0f }, "q", options, 2, new VectorFilter().WithMinScore(0.9));
        Assert.AreEqual(1, filtered.Count);
        Assert.AreEqual("a", filtered[0].Record.Id);
    }

    [TestMethod]
    public async Task DisabledLeg_DoesNotValidateInputOrRunQueryEncoder()
    {
        var queryCalls = 0;
        var store = new PixieInMemoryStore((_, _) =>
        {
            queryCalls++;
            return Task.FromResult(Sparse((1, 1)));
        }, (_, _) => Task.FromResult(Sparse((1, 1))));
        await store.UpsertAsync(Record("a", "a"));
        var dense = await store.HybridSearchAsync(new[] { 1f, 0f }, null!, new HybridSearchOptions { VectorWeight = 1 });
        Assert.AreEqual(1, dense.Count);
        Assert.AreEqual(0, queryCalls);
        var sparse = await store.HybridSearchAsync(null!, "q", new HybridSearchOptions { VectorWeight = 0 });
        Assert.AreEqual(1, sparse.Count);
        Assert.AreEqual(1, queryCalls);
        Assert.AreEqual(1d, sparse[0].Score);
    }

    [TestMethod]
    public async Task InvalidDimensionsAndNonfiniteValues_DoNotPartiallyUpdateStore()
    {
        var store = TokenStore();
        await store.UpsertAsync(Record("a", "old"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertBatchAsync(new[]
        {
            Record("a", "new"), Record("b", "new", vector: new[] { 1f, 0f, 0f })
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync(Record("a", "new", vector: new[] { float.NaN, 0f })));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync(new[] { 1f }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync(new[] { float.PositiveInfinity, 0f }));
        Assert.AreEqual("old", (await store.GetAsync("a"))!.Content);
        Assert.IsNull(await store.GetAsync("b"));
        Assert.AreEqual("old", (await store.TextSearchAsync("old"))[0].Record.Content);
    }

    [TestMethod]
    public async Task EmptyDenseVector_IsSearchableOnlyBySparseLeg()
    {
        var store = TokenStore();
        await store.UpsertAsync(Record("sparse", "old", vector: Array.Empty<float>()));
        Assert.AreEqual(0, (await store.SearchAsync(new[] { 1f, 0f })).Count);
        Assert.AreEqual("sparse", (await store.TextSearchAsync("old"))[0].Record.Id);
    }

    [TestMethod]
    public async Task EmptyContentUpdate_RemovesOldSparseTermsAndKeepsDenseRecord()
    {
        var store = TokenStore();
        await store.UpsertAsync(Record("a", "old"));
        await store.UpsertAsync(Record("a", " "));
        Assert.AreEqual(0, (await store.TextSearchAsync("old")).Count);
        Assert.AreEqual(1, (await store.SearchAsync(new[] { 1f, 0f })).Count);
    }

    [TestMethod]
    public async Task NestedFiltersAndMissingKeys_AreConsistentAcrossSearchGetCountAndDelete()
    {
        var store = TokenStore();
        var a = Record("a", "old"); a.Metadata["path"] = "docs/a.cs"; a.Metadata["version"] = "20";
        var b = Record("b", "old"); b.Metadata["path"] = "docs/b.md"; b.Metadata["version"] = "10";
        var c = Record("c", "old", "B");
        await store.UpsertBatchAsync(new[] { a, b, c });
        var filter = Tenant("A").Or(group => group
            .And(nested => nested.WhereLike("path", "%/_.cs").WhereGreaterThanOrEqual("version", "20"))
            .Where("path", "missing"));
        Assert.AreEqual("a", (await store.TextSearchAsync("old", filter: filter))[0].Record.Id);
        Assert.AreEqual("a", (await store.SearchAsync(new[] { 1f, 0f }, filter: filter))[0].Record.Id);
        Assert.AreEqual(1L, await store.CountAsync(filter));
        Assert.IsNull(await store.GetAsync("b", filter));
        Assert.AreEqual(0L, await store.CountAsync(new VectorFilter().WhereNot("absent", "x")));
        Assert.AreEqual(0L, await store.CountAsync(new VectorFilter().WhereNotIn("absent", Array.Empty<string>())));
        Assert.AreEqual(2L, await store.CountAsync(new VectorFilter().WhereNotIn("path", Array.Empty<string>())));
        Assert.AreEqual(0L, await store.CountAsync(new VectorFilter().WhereIn("path", Array.Empty<string>())));
        Assert.AreEqual(1L, await store.CountAsync(new VectorFilter().WhereNotExists("path")));
        await store.DeleteByFilterAsync(filter);
        CollectionAssert.AreEquivalent(new[] { "b", "c" }, (await store.TextSearchAsync("old")).Select(result => result.Record.Id).ToArray());
    }

    [TestMethod]
    public async Task CancelledOperations_DoNotCallEncoderOrMutateState()
    {
        var queryCalls = 0;
        var documentCalls = 0;
        var store = new PixieInMemoryStore((_, _) => { queryCalls++; return Task.FromResult(Sparse((1, 1))); },
            (_, _) => { documentCalls++; return Task.FromResult(Sparse((1, 1))); });
        await store.UpsertAsync(Record("a", "old"));
        using var source = new CancellationTokenSource();
        source.Cancel();
        var ct = source.Token;
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.UpsertAsync(Record("b", "new"), ct));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.TextSearchAsync("q", cancellationToken: ct));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SearchAsync(new[] { 1f, 0f }, cancellationToken: ct));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.DeleteByFilterAsync(Tenant("A"), ct));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.ReplaceByFilterAsync(Tenant("A"), Array.Empty<VectorRecord>(), ct));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.GetAsync("a", cancellationToken: ct));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.CountAsync(cancellationToken: ct));
        Assert.AreEqual(0, queryCalls);
        Assert.AreEqual(1, documentCalls);
        Assert.AreEqual(1L, await store.CountAsync());
    }

    [TestMethod]
    public async Task EmptyQueryAndEmptyStore_DoNotRunEncoder()
    {
        var queryCalls = 0;
        var store = new PixieInMemoryStore((_, _) => { queryCalls++; throw new InvalidOperationException(); },
            (_, _) => Task.FromResult(Sparse((1, 1))));
        Assert.AreEqual(0, (await store.TextSearchAsync("query")).Count);
        await store.UpsertAsync(Record("a", "old"));
        Assert.AreEqual(0, (await store.TextSearchAsync(" ")).Count);
        Assert.AreEqual(0, queryCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RagBuilder_RoutesOrdinaryAndAgenticSearchThroughPixie(bool hybrid)
    {
        var queries = new List<string>();
        var store = new PixieInMemoryStore((text, _) =>
        {
            queries.Add(text);
            return Task.FromResult(Sparse((1, 1)));
        }, (text, _) => Task.FromResult(text == "refund guide" ? Sparse((1, 2)) : Sparse((2, 1))));
        var embedding = new CountingEmbedding();
        var rag = await RagStore.BuildAsync(builder =>
        {
            builder.UseStore(store).UseEmbedding(embedding).WithTextSplitter(new WholeDocumentSplitter())
                .AddText("refund guide", "refund").AddText("shipping guide", "shipping").WithTopK(1);
            if (hybrid) builder.UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.5f });
            else builder.UseKeywordSearch();
        });
        var ordinary = await rag.QueryAsync("refund question");
        Assert.AreEqual("refund guide", ordinary.References.Single().Record.Content);
        Assert.AreEqual(2, embedding.DocumentCalls);
        Assert.AreEqual(hybrid ? 1 : 0, embedding.QueryCalls);

        var service = DispatchProxy.Create<IRegisteredSearchService, RegistrationProxy>();
        service.WithAgenticRag(rag);
        var function = ((RegistrationProxy)(object)service).Functions.Single();
        var answer = await function.HandlerWithCancellation!(new Dictionary<string, object>
        {
            ["query"] = "refund agent question"
        }, CancellationToken.None);
        StringAssert.Contains(answer, "refund guide");
        Assert.IsFalse(answer.Contains("shipping guide", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[] { "refund question", "refund agent question" }, queries);
        Assert.AreEqual(hybrid ? 2 : 0, embedding.QueryCalls);
    }

    [TestMethod]
    public async Task RagPipeline_ReindexEmptyUpdateAndDeleteMaintainPixieIndex()
    {
        var store = TokenStore();
        var embedding = new CountingEmbedding();
        var pipeline = new RagPipeline(embedding, store, new WholeDocumentSplitter(), new PlainContextBuilder());
        await pipeline.IndexDocumentAsync(new RagDocument("document", "old", "file"));
        Assert.AreEqual(1, (await store.TextSearchAsync("old")).Count);
        await pipeline.IndexDocumentAsync(new RagDocument("document", "new", "file"));
        Assert.AreEqual(0, (await store.TextSearchAsync("old")).Count);
        Assert.AreEqual(1, (await store.TextSearchAsync("new")).Count);
        await pipeline.IndexDocumentAsync(new RagDocument("document", "", "file"));
        Assert.AreEqual(0, (await store.TextSearchAsync("new")).Count);
        Assert.AreEqual(0L, await store.CountAsync());
        Assert.AreEqual(2, embedding.DocumentCalls, "Empty updates must not ask the embedding provider to process text.");
        await pipeline.IndexDocumentAsync(new RagDocument("document", "new", "file"));
        await pipeline.DeleteDocumentAsync("document");
        Assert.AreEqual(0L, await store.CountAsync());
        Assert.AreEqual(0, (await store.TextSearchAsync("new")).Count);
    }

    public interface IRegisteredSearchService : IAIService, IFunctionRegisterable { }

    public class RegistrationProxy : DispatchProxy
    {
        public List<FunctionDefinition> Functions { get; } = new();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IFunctionRegisterable.AddFunction))
                throw new InvalidOperationException("The integration test must only register and invoke the RAG tool.");
            Functions.Add((FunctionDefinition)args![0]!);
            return null;
        }
    }

    private sealed class CountingEmbedding : IEmbeddingProvider
    {
        public int Dimensions => 2;
        public int QueryCalls { get; private set; }
        public int DocumentCalls { get; private set; }
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QueryCalls++;
            return Task.FromResult(new[] { 1f, 0f });
        }
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentCalls++;
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f, 0f }).ToArray());
        }
    }

    private sealed class WholeDocumentSplitter : ITextSplitter
    {
        public IReadOnlyList<RagChunk> Split(RagDocument document)
            => string.IsNullOrWhiteSpace(document.Content) ? Array.Empty<RagChunk>()
            : new[] { new RagChunk(document.Id + "_0", document.Id, document.Content, 0)
                { Metadata = new Dictionary<string, string>(document.Metadata) } };
    }

    private sealed class PlainContextBuilder : IContextBuilder
    {
        public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
            => string.Join("\n", searchResults.Select(result => result.Record.Content));
    }

    private static PixieInMemoryStore CreateStore(IReadOnlyDictionary<string, PixieSparseVector> vectors)
        => new((text, _) => Task.FromResult(vectors[text]), (text, _) => Task.FromResult(vectors[text]));

    private static PixieInMemoryStore TokenStore() => CreateStore(new Dictionary<string, PixieSparseVector>
    {
        ["old"] = Sparse((1, 1)), ["new"] = Sparse((2, 1))
    });

    private static PixieSparseVector Sparse(params (int index, float value)[] terms)
        => new(terms.Select(term => term.index), terms.Select(term => term.value));

    private static VectorRecord Record(string id, string content, string tenant = "A", float[]? vector = null)
        => new(id, vector ?? new[] { 1f, 0f }, content) { Metadata = new() { ["tenant"] = tenant } };

    private static VectorFilter Tenant(string tenant) => new VectorFilter().Where("tenant", tenant);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
