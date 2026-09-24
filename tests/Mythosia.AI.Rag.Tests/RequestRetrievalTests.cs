using Mythosia.AI.Rag.Retrieval;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public class RequestRetrievalTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KeywordAndZeroWeight_SearchWithoutQueryEmbedding(bool zeroWeight)
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("alpha", "refund policy", "alpha"));
        await store.UpsertAsync(Record("beta", "refund policy refund", "beta"));
        var embeddings = new ProbeEmbeddings { FailOnQuery = true };
        var rag = await RagStore.BuildAsync(b =>
        {
            b.UseStore(store).UseEmbedding(embeddings);
            if (zeroWeight) b.UseHybridSearch(0);
            else b.UseKeywordSearch();
        });
        var stages = new List<RagProgressStage>();
        var result = await rag.QueryAsync("refund", new RagQueryOptions
        {
            StoreFilter = new VectorFilter().Where("tenant", "alpha"),
            ProgressAsync = stage => { stages.Add(stage); return Task.CompletedTask; }
        });
        Assert.AreEqual(0, embeddings.QueryCalls);
        Assert.AreEqual(1, result.References.Count);
        Assert.AreEqual("alpha", result.References[0].Record.Id);
        Assert.IsFalse(stages.Contains(RagProgressStage.Embedding));
        Assert.IsTrue(stages.Contains(RagProgressStage.Retrieval));
    }

    [TestMethod]
    public async Task KeywordIngestionAndUpdates_KeepSearchIndexCurrent()
    {
        using var store = new InMemoryVectorStore();
        var embeddings = new ProbeEmbeddings();
        var rag = await RagStore.BuildAsync(b => b.UseStore(store).UseEmbedding(embeddings)
            .UseKeywordSearch().AddText("refund policy", "policy"));
        Assert.IsTrue(embeddings.BatchCalls > 0, "Shared vector index still embeds during ingestion.");
        embeddings.FailOnQuery = true;
        Assert.IsTrue((await rag.QueryAsync("refund")).HasReferences);
        var pipeline = (RagPipeline)rag.Pipeline;
        await pipeline.IndexDocumentsAsync(new[] { new RagDocument { Id = "policy", Content = "replacement warranty" } });
        Assert.IsFalse((await rag.QueryAsync("refund")).HasReferences);
        Assert.IsTrue((await rag.QueryAsync("warranty")).HasReferences);
        await pipeline.DeleteDocumentAsync("policy");
        Assert.IsFalse((await rag.QueryAsync("warranty")).HasReferences);
        Assert.AreEqual(0, embeddings.QueryCalls);
    }

    [TestMethod]
    public async Task HybridPlainQuery_UsesTextAndForwardsSnapshotOptions()
    {
        var store = new ProbeStore();
        var options = new HybridSearchOptions { VectorWeight = .7f, CandidateMultiplier = 4, RrfK = 17 };
        var builder = new RagBuilder().UseStore(store).UseEmbedding(new ProbeEmbeddings()).UseHybridSearch(options);
        options.VectorWeight = .1f;
        var rag = await builder.BuildAsync();
        await rag.QueryAsync("full original question");
        Assert.AreEqual("full original question", store.Text);
        Assert.AreEqual(.7f, store.Options!.VectorWeight);
        Assert.AreEqual(4, store.Options.CandidateMultiplier);
        Assert.AreEqual(17, store.Options.RrfK);
        Assert.AreEqual(1, store.HybridCalls);
        Assert.AreEqual(0, store.LegacyCalls);
    }

    [TestMethod]
    public async Task HybridOverride_EmbedsSemanticQueryAndSearchesSeparateText()
    {
        var store = new ProbeStore();
        var embeddings = new ProbeEmbeddings();
        var rag = await RagStore.BuildAsync(b => b.UseStore(store).UseEmbedding(embeddings).UseHybridSearch());
        await ((RagPipeline)rag.Pipeline).ProcessAsync("complete semantic question", "refund policy", null);
        Assert.AreEqual("complete semantic question", embeddings.LastQuery);
        Assert.AreEqual("refund policy", store.Text);
    }

    [TestMethod]
    public async Task EmptyTextOverride_ExplicitlyDisablesKeywordLeg()
    {
        var store = new SplitSearchStore();
        var rag = await RagStore.BuildAsync(b => b.UseStore(store).UseEmbedding(new ProbeEmbeddings()).UseHybridSearch());
        await ((RagPipeline)rag.Pipeline).ProcessAsync("question", "", null);
        Assert.AreEqual(1, store.VectorCalls);
        Assert.AreEqual(0, store.TextCalls);
        Assert.AreEqual(0, store.LegacyCalls);
    }

    [TestMethod]
    public async Task VectorWeightOne_SkipsTextSearch()
    {
        var store = new SplitSearchStore();
        var embeddings = new ProbeEmbeddings();
        var rag = await RagStore.BuildAsync(b => b.UseStore(store).UseEmbedding(embeddings).UseHybridSearch(1));
        await rag.QueryAsync("question");
        Assert.AreEqual(1, embeddings.QueryCalls);
        Assert.AreEqual(1, store.VectorCalls);
        Assert.AreEqual(0, store.LegacyCalls);
        Assert.AreEqual(0, store.TextCalls);
    }

    [TestMethod]
    public async Task CustomRetriever_ReceivesTextAndConstraintsWithoutEmbedding()
    {
        var embeddings = new ProbeEmbeddings { FailOnQuery = true };
        var custom = new ProbeRetriever();
        var rag = await RagStore.BuildAsync(b => b.UseEmbedding(embeddings).UseRetriever(custom));
        var options = new RagQueryOptions { StoreFilter = new VectorFilter().Where("tenant", "alpha") };
        options.FinalFilter.TopK = 7;
        await ((RagPipeline)rag.Pipeline).ProcessAsync("semantic text", "keyword text", options);
        Assert.AreEqual("semantic text", custom.Request!.Query);
        Assert.AreEqual("keyword text", custom.Request.TextQuery);
        Assert.AreEqual(7, custom.Request.TopK);
        Assert.AreEqual("alpha", ((MetadataCondition)custom.Request.Filter!.Conditions.Single()).Value);
        Assert.AreEqual(0, embeddings.QueryCalls);
    }

    [TestMethod]
    public void Request_SnapshotsNestedFiltersAndSetValues()
    {
        var values = new[] { "alpha", "beta" };
        var filter = new VectorFilter().WhereIn("tenant", values).Or(g => g.Where("kind", "manual"));
        var request = new RagRetrievalRequest("question", filter: filter);
        values[0] = "changed";
        filter.Where("new", "condition");
        filter.MinScore = .9;
        Assert.AreEqual(2, request.Filter!.Conditions.Count);
        Assert.AreEqual("alpha", ((MetadataCondition)request.Filter.Conditions[0]).Values![0]);
        Assert.IsNull(request.Filter.MinScore);
    }

    [TestMethod]
    public async Task LegacyStrategy_RemainsCompatibleAndGetsDenseVector()
    {
        using var store = new InMemoryVectorStore();
        var embeddings = new ProbeEmbeddings();
        var strategy = new ProbeLegacyStrategy();
        var pipeline = new RagPipeline(embeddings, store, new CharacterTextSplitter(),
            new DefaultContextBuilder(), strategy, null);
        await pipeline.ProcessAsync("original");
        Assert.AreEqual(1, embeddings.QueryCalls);
        Assert.IsNotNull(strategy.Vector);
        Assert.IsNull(strategy.Text, "Preserve the existing legacy contract's null override.");
    }

    [TestMethod]
    public async Task UnsupportedKeywordMode_FailsBeforeIngestion()
    {
        var embeddings = new ProbeEmbeddings();
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => RagStore.BuildAsync(b => b
            .UseStore(new LegacyStore()).UseEmbedding(embeddings).UseKeywordSearch().AddText("document", "id")));
        Assert.AreEqual(0, embeddings.BatchCalls);
        Assert.AreEqual(0, embeddings.QueryCalls);
    }

    [TestMethod]
    public async Task UnsupportedHybridOptions_AreNotSilentlyIgnored()
    {
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => RagStore.BuildAsync(b => b
            .UseStore(new LegacyStore()).UseHybridSearch(.8f)));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => RagStore.BuildAsync(b => b
            .UseStore(new LegacyStore()).UseHybridSearch(new HybridSearchOptions())));
    }

    [TestMethod]
    public async Task LegacyDefaultHybrid_StillDelegatesButDoesNotHideFailure()
    {
        var store = new LegacyStore();
        var rag = await RagStore.BuildAsync(b => b.UseStore(store).UseEmbedding(new ProbeEmbeddings()).UseHybridSearch());
        await rag.QueryAsync("refund");
        Assert.AreEqual(1, store.LegacyCalls);
        store.FailHybrid = true;
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => rag.QueryAsync("refund"));
        Assert.AreEqual(0, store.VectorCalls);
    }

    [TestMethod]
    public async Task AgenticKeywordAndHybrid_UseConfiguredRetriever()
    {
        foreach (var keyword in new[] { true, false })
        {
            var backend = new ProbeStore();
            var embeddings = new ProbeEmbeddings { FailOnQuery = keyword };
            var rag = await RagStore.BuildAsync(b =>
            {
                b.UseStore(backend).UseEmbedding(embeddings);
                if (keyword) b.UseKeywordSearch(); else b.UseHybridSearch(.7f);
            });
            var service = new MockAIService();
            service.WithAgenticRag(rag, _ => new RagQueryOptions { StoreFilter = new VectorFilter().Where("tenant", "alpha") });
            await service.Functions.Single().HandlerWithCancellation!(
                new Dictionary<string, object> { ["query"] = "refund policy" }, CancellationToken.None);
            Assert.AreEqual("refund policy", backend.Text);
            Assert.AreEqual(keyword ? 1 : 0, backend.TextCalls);
            Assert.AreEqual(keyword ? 0 : 1, backend.HybridCalls);
            Assert.AreEqual("alpha", ((MetadataCondition)backend.Filter!.Conditions.Single()).Value);
        }
    }

    [TestMethod]
    public async Task RuntimeModeChanges_UseCurrentIndexAndForwardOptions()
    {
        var backend = new ProbeStore();
        var embeddings = new ProbeEmbeddings();
        var rag = await RagStore.BuildAsync(b => b.UseStore(backend).UseEmbedding(embeddings));
        Assert.IsTrue(rag.UseKeywordSearch());
        await rag.QueryAsync("text");
        Assert.AreEqual(0, embeddings.QueryCalls);
        Assert.IsTrue(rag.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = .2f }));
        await rag.QueryAsync("hybrid");
        Assert.AreEqual(.2f, backend.Options!.VectorWeight);
        Assert.IsTrue(rag.UpdateRetrievalStrategy(false));
        await rag.QueryAsync("vector");
        Assert.AreEqual(1, backend.VectorCalls);
    }

    [TestMethod]
    public async Task CancelledQuery_DoesNotInvokeCustomRetriever()
    {
        var custom = new ProbeRetriever();
        var rag = await RagStore.BuildAsync(b => b.UseRetriever(custom));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => rag.QueryAsync("query", cancellation.Token));
        Assert.IsNull(custom.Request);
    }

    [TestMethod]
    public async Task CancellationAfterEmbedding_DoesNotCallBackend()
    {
        using var cancellation = new CancellationTokenSource();
        var embeddings = new ProbeEmbeddings { OnQuery = () => cancellation.Cancel() };
        var backend = new ProbeStore();
        var rag = await RagStore.BuildAsync(b => b.UseStore(backend).UseEmbedding(embeddings).UseHybridSearch());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => rag.QueryAsync("query", cancellation.Token));
        Assert.AreEqual(0, backend.HybridCalls);
    }

    [TestMethod]
    [DataRow(0f, "query", 1d)]
    [DataRow(1f, "query", 1d)]
    [DataRow(.5f, "", .5d)]
    public async Task HybridSingleLeg_PreservesRankScoresAndFinalThreshold(float weight, string text, double expectedScore)
    {
        var backend = new SplitSearchStore();
        var embeddings = new ProbeEmbeddings { FailOnQuery = weight == 0 };
        var retriever = new RagRetrievers.Hybrid(embeddings, backend,
            new HybridSearchOptions { VectorWeight = weight, CandidateMultiplier = 3, RrfK = 9 });
        var results = await retriever.RetrieveAsync(new RagRetrievalRequest("semantic", text, 2,
            new VectorFilter { MinScore = expectedScore - .01 }.Where("tenant", "alpha")));
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(expectedScore, results[0].Score, 1e-8);
        Assert.AreEqual(6, backend.LastTopK);
        Assert.IsNull(backend.LastFilter!.MinScore, "Raw candidate scores must not be compared to a fused threshold.");
        Assert.AreEqual("alpha", ((MetadataCondition)backend.LastFilter.Conditions.Single()).Value);
        Assert.AreEqual(weight == 0 ? 0 : 1, embeddings.QueryCalls);
        Assert.AreEqual(weight == 0 ? 1 : 0, backend.TextCalls);
    }

    [TestMethod]
    public async Task ApplicationHybridFusion_AppliesOptionsAndFiltersAfterRanking()
    {
        var backend = new SplitSearchStore();
        var retriever = new RagRetrievers.Hybrid(new ProbeEmbeddings(), backend,
            new HybridSearchOptions { VectorWeight = .75f, CandidateMultiplier = 4, RrfK = 10 });
        var results = await retriever.RetrieveAsync(new RagRetrievalRequest("question", topK: 2,
            filter: new VectorFilter { MinScore = .5 }.Where("tenant", "alpha")));
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("vector", results[0].Record.Id);
        Assert.AreEqual(.75, results[0].Score, 1e-8);
        Assert.AreEqual(8, backend.LastTopK);
        Assert.IsNull(backend.LastFilter!.MinScore);
        Assert.AreEqual(1, backend.VectorCalls);
        Assert.AreEqual(1, backend.TextCalls);
        Assert.AreEqual(0, backend.LegacyCalls);
    }

    [TestMethod]
    public async Task ConfigurableOnlyStore_ZeroWeightDoesNotRequireSeparateTextInterface()
    {
        var backend = new ConfigurableOnlyStore();
        var embeddings = new ProbeEmbeddings { FailOnQuery = true };
        var rag = await RagStore.BuildAsync(b => b.UseStore(backend).UseEmbedding(embeddings)
            .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0 }));
        await rag.QueryAsync("keyword");
        Assert.AreEqual(0, embeddings.QueryCalls);
        Assert.AreEqual(1, backend.HybridCalls);
        Assert.AreEqual(0, backend.LastVector!.Length);
        Assert.AreEqual(0f, backend.LastOptions!.VectorWeight);
    }

    [TestMethod]
    [DataRow(0f, "query")]
    [DataRow(1f, "query")]
    [DataRow(.5f, "")]
    public async Task NativeHybridEndpoints_PreserveConfiguredBackendAndFilterValidation(float weight, string text)
    {
        var backend = new ProbeStore();
        var retriever = new RagRetrievers.Hybrid(new ProbeEmbeddings { FailOnQuery = weight == 0 }, backend,
            new HybridSearchOptions { VectorWeight = weight, CandidateMultiplier = 4, RrfK = 11 });
        await retriever.RetrieveAsync(new RagRetrievalRequest("semantic", text,
            filter: new VectorFilter().Where("tenant", "alpha")));
        Assert.AreEqual(1, backend.HybridCalls, "Configured endpoint/filter semantics must not be bypassed with a legacy dense search.");
        Assert.AreEqual(0, backend.VectorCalls);
        Assert.AreEqual(0, backend.TextCalls);
        Assert.AreEqual(weight, backend.Options!.VectorWeight);
        Assert.AreEqual(11, backend.Options.RrfK);
        Assert.AreEqual(4, backend.Options.CandidateMultiplier);
        Assert.AreEqual(text, backend.Text);
        Assert.AreEqual("alpha", ((MetadataCondition)backend.Filter!.Conditions.Single()).Value);
    }

    [TestMethod]
    [DataRow(RagProgressStage.Embedding)]
    [DataRow(RagProgressStage.Retrieval)]
    public async Task CancellationFromProgress_DoesNotStartNextOperation(RagProgressStage cancelAt)
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new ProbeStore();
        var embeddings = new ProbeEmbeddings();
        var rag = await RagStore.BuildAsync(b => b.UseStore(backend).UseEmbedding(embeddings).UseHybridSearch());
        var options = new RagQueryOptions
        {
            ProgressAsync = stage =>
            {
                if (stage == cancelAt) cancellation.Cancel();
                return Task.CompletedTask;
            }
        };
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => rag.QueryAsync("query", options, cancellation.Token));
        Assert.AreEqual(cancelAt == RagProgressStage.Embedding ? 0 : 1, embeddings.QueryCalls);
        Assert.AreEqual(0, backend.HybridCalls);
    }

    private static VectorRecord Record(string id, string text, string tenant) =>
        new(id, new float[] { 1, 0, 0 }, text) { Metadata = { ["tenant"] = tenant } };

    private sealed class ProbeEmbeddings : IEmbeddingProvider
    {
        public int Dimensions => 3;
        public int QueryCalls, BatchCalls;
        public bool FailOnQuery;
        public string? LastQuery;
        public Action? OnQuery;
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            LastQuery = text;
            if (FailOnQuery) throw new InvalidOperationException("Query embedding must not be called.");
            OnQuery?.Invoke();
            return Task.FromResult(new float[] { 1, 0, 0 });
        }
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            BatchCalls++;
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 1, 0, 0 }).ToList());
        }
    }

    private sealed class ProbeRetriever : IRagRetriever
    {
        public RagRetrievalRequest? Request;
        public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(RagRetrievalRequest request, CancellationToken cancellationToken = default)
        { Request = request; return Task.FromResult<IReadOnlyList<VectorSearchResult>>(Array.Empty<VectorSearchResult>()); }
    }

    private sealed class ProbeLegacyStrategy : IRetrievalStrategy
    {
        public float[]? Vector;
        public string? Text;
        public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(float[] denseVector, string? query, int topK, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        { Vector = denseVector; Text = query; return Task.FromResult<IReadOnlyList<VectorSearchResult>>(Array.Empty<VectorSearchResult>()); }
    }

    private class LegacyStore : IVectorStore
    {
        public int VectorCalls, LegacyCalls;
        public bool FailHybrid;
        public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) => Task.FromResult<VectorRecord?>(null);
        public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public virtual Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        { VectorCalls++; return Empty(); }
        public Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(float[] denseVector, string query, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        { LegacyCalls++; if (FailHybrid) throw new NotSupportedException("native hybrid unavailable"); return Empty(); }
        protected static Task<IReadOnlyList<VectorSearchResult>> Empty() => Task.FromResult<IReadOnlyList<VectorSearchResult>>(Array.Empty<VectorSearchResult>());
    }

    private sealed class SplitSearchStore : LegacyStore, ITextSearchStore
    {
        public int TextCalls, LastTopK;
        public VectorFilter? LastFilter;
        public override Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            VectorCalls++;
            LastTopK = topK;
            LastFilter = filter;
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(new[] { new VectorSearchResult(Record("vector", "content", "alpha"), .01) });
        }
        public Task<IReadOnlyList<VectorSearchResult>> TextSearchAsync(string query, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            TextCalls++;
            LastTopK = topK;
            LastFilter = filter;
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(new[] { new VectorSearchResult(Record("text", "content", "alpha"), 100) });
        }
    }

    private sealed class ConfigurableOnlyStore : LegacyStore, IConfigurableHybridSearchStore
    {
        public int HybridCalls;
        public float[]? LastVector;
        public HybridSearchOptions? LastOptions;
        public Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(float[] denseVector, string query, HybridSearchOptions options,
            int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            HybridCalls++;
            LastVector = denseVector;
            LastOptions = options;
            return Empty();
        }
    }

    private sealed class ProbeStore : LegacyStore, ITextSearchStore, IConfigurableHybridSearchStore
    {
        public int TextCalls, HybridCalls;
        public string? Text;
        public VectorFilter? Filter;
        public HybridSearchOptions? Options;
        public Task<IReadOnlyList<VectorSearchResult>> TextSearchAsync(string query, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        { TextCalls++; Text = query; Filter = filter; return Empty(); }
        public Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(float[] denseVector, string query, HybridSearchOptions options, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        { HybridCalls++; Text = query; Filter = filter; Options = options; return Empty(); }
    }
}
