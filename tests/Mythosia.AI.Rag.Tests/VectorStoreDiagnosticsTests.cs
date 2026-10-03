using Mythosia.AI.Rag.Diagnostics;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class VectorStoreDiagnosticsTests
{
    [TestMethod]
    [DataRow("neutral", false)]
    [DataRow("neutral", true)]
    [DataRow("legacy", false)]
    [DataRow("legacy", true)]
    [DataRow("legacy-with-public-helpers", false)]
    [DataRow("legacy-with-public-helpers", true)]
    [DataRow("in-memory", false)]
    [DataRow("in-memory", true)]
    public async Task DiagnosticConsumers_UseOptionalContractForAllAnalysis(string contract, bool useRagStore)
    {
        using var inner = new InMemoryVectorStore();
        await SeedAsync(inner);
        IVectorStore vectors = contract switch
        {
            "neutral" => new NeutralDiagnosticsStore(inner),
            "legacy" => new LegacyDiagnosticsStore(inner),
            "legacy-with-public-helpers" => new LegacyWithPublicHelpersStore(inner),
            _ => inner
        };
        var (diagnostics, session) = await CreateDiagnosticsAsync(vectors, useRagStore);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        var matches = await diagnostics.FindChunksContainingAsync("TARGET DETAIL", token);
        Assert.AreEqual("target", matches.Single().Record.Id);
        Assert.AreEqual(4, matches[0].MatchIndex);

        AssertScores(await diagnostics.DiagnoseQueryAsync("query", "target detail", token));

        var missing = await session.WhyMissingAsync("query", "target detail", token);
        Assert.AreEqual(DiagnosticStatus.Pass, missing.Steps.Single(s => s.StepName == "Indexing").Status);
        Assert.AreEqual(DiagnosticStatus.Fail, missing.Steps.Single(s => s.StepName == "TopK Ranking").Status);
        Assert.AreEqual(DiagnosticStatus.Fail, missing.Steps.Single(s => s.StepName == "MinScore Filter").Status);
        Assert.IsNotNull(missing.QueryDetail);
        AssertScores(missing.QueryDetail);

        var health = await session.HealthCheckAsync(token);
        Assert.AreEqual(3, health.TotalChunks);
        Assert.AreEqual(DiagnosticStatus.Pass, health.Items.Single(i => i.Category == "Chunk Count").Status);
        Assert.AreEqual(DiagnosticStatus.Warning, health.Items.Single(i => i.Category == "Duplicates").Status);

        if (vectors is StoreWithoutDiagnostics probe)
        {
            Assert.HasCount(0, probe.SearchTopKs, "A diagnostics-capable store must not use the ordinary search fallback.");
            Assert.AreEqual(3, probe.ListCalls);
            Assert.AreEqual(2, probe.ScoredCalls);
            Assert.AreEqual(token, probe.LastListToken);
            Assert.AreEqual(token, probe.LastScoredToken);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StoreWithoutDiagnostics_PreservesSearchFallbackAndLimitedAnalysis(bool useRagStore)
    {
        using var inner = new InMemoryVectorStore();
        await SeedAsync(inner);
        var vectors = new StoreWithoutDiagnostics(inner);
        var (diagnostics, session) = await CreateDiagnosticsAsync(vectors, useRagStore);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => diagnostics.FindChunksContainingAsync("target detail"));
        StringAssert.Contains(error.Message, nameof(IVectorStoreDiagnostics));
        AssertScores(await diagnostics.DiagnoseQueryAsync("query", "target detail"));

        var missing = await session.WhyMissingAsync("query", "target detail");
        Assert.AreEqual(DiagnosticStatus.Info, missing.Steps.Single(s => s.StepName == "Indexing").Status);
        Assert.IsNotNull(missing.QueryDetail);
        AssertScores(missing.QueryDetail);

        var health = await session.HealthCheckAsync();
        Assert.AreEqual(0, health.TotalChunks);
        Assert.HasCount(1, health.Items);
        Assert.AreEqual("Store Type", health.Items[0].Category);
        Assert.AreEqual(DiagnosticStatus.Info, health.Items[0].Status);
        CollectionAssert.AreEqual(new[] { int.MaxValue, int.MaxValue }, vectors.SearchTopKs);
        Assert.IsNotNull(vectors.LastSearchFilter);
        Assert.IsNull(vectors.LastSearchFilter.MinScore);
        Assert.HasCount(0, vectors.LastSearchFilter.Conditions);
    }

#pragma warning disable CS0618 // Exercise the supported compatibility interface deliberately.
    [TestMethod]
    public async Task ExplicitLegacyImplementation_BridgesBothMethodsToNeutralContract()
    {
        using var inner = new InMemoryVectorStore();
        await SeedAsync(inner);
        var legacy = new LegacyDiagnosticsStore(inner);
        IRagDiagnosticsStore oldContract = legacy;
        IVectorStoreDiagnostics neutralContract = oldContract;
        using var cancellation = new CancellationTokenSource();

        Assert.HasCount(3, await neutralContract.ListAllRecordsAsync(cancellation.Token));
        var scores = await neutralContract.ScoredListAsync([1, 0], cancellation.Token);
        CollectionAssert.AreEqual(new[] { "leader", "target", "opposite" }, scores.Select(s => s.Record.Id).ToArray());
        Assert.AreEqual(cancellation.Token, legacy.LastListToken);
        Assert.AreEqual(cancellation.Token, legacy.LastScoredToken);

        Assert.IsTrue(typeof(IVectorStoreDiagnostics).IsAssignableFrom(typeof(IRagDiagnosticsStore)));
        var obsolete = (ObsoleteAttribute)typeof(IRagDiagnosticsStore)
            .GetCustomAttributes(typeof(ObsoleteAttribute), false).Single();
        Assert.IsFalse(obsolete.IsError);
        StringAssert.Contains(obsolete.Message!, nameof(IVectorStoreDiagnostics));
        Assert.IsFalse(typeof(IRagDiagnosticsStore).IsAssignableFrom(typeof(InMemoryVectorStore)));
    }

    // Keep these implementations explicit: merely inheriting the new interface would
    // break existing stores that implemented only the original interface's slots.
    private sealed class LegacyDiagnosticsStore(InMemoryVectorStore inner)
        : StoreWithoutDiagnostics(inner), IRagDiagnosticsStore
    {
        Task<IReadOnlyList<VectorRecord>> IRagDiagnosticsStore.ListAllRecordsAsync(CancellationToken cancellationToken)
            => ListRecordsAsync(cancellationToken);

        Task<IReadOnlyList<VectorSearchResult>> IRagDiagnosticsStore.ScoredListAsync(float[] queryVector, CancellationToken cancellationToken)
            => ScoreRecordsAsync(queryVector, cancellationToken);
    }

    private sealed class LegacyWithPublicHelpersStore(InMemoryVectorStore inner)
        : StoreWithoutDiagnostics(inner), IRagDiagnosticsStore
    {
        // The public helpers must not supersede the explicitly implemented legacy slots.
        public Task<IReadOnlyList<VectorRecord>> ListAllRecordsAsync(CancellationToken cancellationToken = default)
            => throw new AssertFailedException("RAG called the public helper instead of the legacy interface.");

        public Task<IReadOnlyList<VectorSearchResult>> ScoredListAsync(float[] queryVector, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("RAG called the public helper instead of the legacy interface.");

        Task<IReadOnlyList<VectorRecord>> IRagDiagnosticsStore.ListAllRecordsAsync(CancellationToken cancellationToken)
            => ListRecordsAsync(cancellationToken);

        Task<IReadOnlyList<VectorSearchResult>> IRagDiagnosticsStore.ScoredListAsync(float[] queryVector, CancellationToken cancellationToken)
            => ScoreRecordsAsync(queryVector, cancellationToken);
    }
#pragma warning restore CS0618

    private static async Task SeedAsync(IVectorStore vectors)
    {
        const string leader = "The leading chunk contains ordinary background information for this diagnostic test.";
        await vectors.UpsertBatchAsync(new[]
        {
            new VectorRecord("leader", [1, 0], leader),
            new VectorRecord("target", [0, 1], "The target detail is present in this indexed chunk despite its lower query score."),
            new VectorRecord("opposite", [-1, 0], leader)
        });
    }

    private static async Task<(RagDiagnostics Diagnostics, RagDiagnosticSession Session)> CreateDiagnosticsAsync(
        IVectorStore vectors, bool useRagStore)
    {
        var embedding = new FixedEmbeddingProvider();
        if (useRagStore)
        {
            var store = await RagStore.BuildAsync(builder => builder.UseEmbedding(embedding).UseStore(vectors)
                .WithTopK(1).WithScoreThreshold(0.5));
            return (new RagDiagnostics(store), store.Diagnose());
        }

        var options = new RagPipelineOptions();
        options.DefaultQuery.FinalFilter.TopK = 1;
        options.DefaultQuery.FinalFilter.MinScore = 0.5;
        var pipeline = new RagPipeline(embedding, vectors, new CharacterTextSplitter(), new DefaultContextBuilder(), options);
        return (new RagDiagnostics(pipeline), pipeline.Diagnose());
    }

    private static void AssertScores(QueryDiagnosticResult result)
    {
        Assert.AreEqual(3, result.TotalChunks);
        Assert.AreEqual(1, result.TopK);
        Assert.AreEqual(0.5, result.MinScore);
        CollectionAssert.AreEqual(new[] { "leader", "target", "opposite" },
            result.AllScoredResults.Select(s => s.Record.Id).ToArray());
        CollectionAssert.AreEqual(new double[] { 1, 0, -1 }, result.AllScoredResults.Select(s => s.Score).ToArray());
        Assert.IsNotNull(result.TargetChunkInfo);
        Assert.AreEqual(2, result.TargetChunkInfo.Rank);
        Assert.IsTrue(result.TargetChunkInfo.ContainsTarget);
        Assert.IsFalse(result.TargetChunkInfo.IsInTopK);
        Assert.IsFalse(result.TargetChunkInfo.PassesMinScore);
    }

    private class StoreWithoutDiagnostics(InMemoryVectorStore inner) : IVectorStore
    {
        public List<int> SearchTopKs { get; } = [];
        public VectorFilter? LastSearchFilter { get; private set; }
        public int ListCalls { get; private set; }
        public int ScoredCalls { get; private set; }
        public CancellationToken LastListToken { get; private set; }
        public CancellationToken LastScoredToken { get; private set; }

        public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
            => inner.UpsertAsync(record, cancellationToken);
        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
            => inner.UpsertBatchAsync(records, cancellationToken);
        public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
            => inner.GetAsync(id, filter, cancellationToken);
        public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(id, filter, cancellationToken);
        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
            => inner.DeleteByFilterAsync(filter, cancellationToken);
        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 5,
            VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            SearchTopKs.Add(topK);
            LastSearchFilter = filter;
            return inner.SearchAsync(queryVector, topK, filter, cancellationToken);
        }

        protected Task<IReadOnlyList<VectorRecord>> ListRecordsAsync(CancellationToken token)
        {
            ListCalls++;
            LastListToken = token;
            return inner.ListAllRecordsAsync(token);
        }

        protected Task<IReadOnlyList<VectorSearchResult>> ScoreRecordsAsync(float[] vector, CancellationToken token)
        {
            ScoredCalls++;
            LastScoredToken = token;
            return inner.ScoredListAsync(vector, token);
        }
    }

    private sealed class NeutralDiagnosticsStore(InMemoryVectorStore inner)
        : StoreWithoutDiagnostics(inner), IVectorStoreDiagnostics
    {
        Task<IReadOnlyList<VectorRecord>> IVectorStoreDiagnostics.ListAllRecordsAsync(CancellationToken cancellationToken)
            => ListRecordsAsync(cancellationToken);
        Task<IReadOnlyList<VectorSearchResult>> IVectorStoreDiagnostics.ScoredListAsync(float[] queryVector, CancellationToken cancellationToken)
            => ScoreRecordsAsync(queryVector, cancellationToken);
    }

    private sealed class FixedEmbeddingProvider : IEmbeddingProvider
    {
        public int Dimensions => 2;
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new float[] { 1, 0 });
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("These diagnostics must not index documents.");
    }
}
