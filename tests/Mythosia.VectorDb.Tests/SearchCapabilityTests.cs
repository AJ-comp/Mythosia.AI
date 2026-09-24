using Mythosia.VectorDb.InMemory;

namespace Mythosia.VectorDb.Tests;

[TestClass]
public class SearchCapabilityTests
{
    [TestMethod]
    public async Task TextSearch_FiltersBeforeTopK_AndRetainsFullRecord()
    {
        using var store = new InMemoryVectorStore();
        for (var i = 0; i < 20; i++)
            await store.UpsertAsync(Record("foreign-" + i, "refund refund refund", "other"));
        var owned = Record("owned", "refund policy for customers with a valid receipt", "mine");
        owned.Metadata["source"] = "policy.md";
        await store.UpsertAsync(owned);

        var results = await store.TextSearchAsync("refund", 1, new VectorFilter().Where("tenant", "mine"));

        Assert.HasCount(1, results);
        Assert.AreEqual("owned", results[0].Record.Id);
        Assert.AreEqual("policy.md", results[0].Record.Metadata["source"]);
        CollectionAssert.AreEqual(owned.Vector, results[0].Record.Vector);
        Assert.IsTrue(results[0].Score > 0);
        Assert.HasCount(0, await store.TextSearchAsync("refund", 1,
            new VectorFilter().Where("tenant", "mine").WithMinScore(results[0].Score + 1)));
    }

    [TestMethod]
    public async Task TextSearch_SafePlainTextAndEmptyInput()
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("hello", "hello world"));
        Assert.HasCount(1, await store.TextSearchAsync("hello !"));
        Assert.HasCount(0, await store.TextSearchAsync("!!!"));
        Assert.HasCount(0, await store.TextSearchAsync("  "));
    }

    [TestMethod]
    public async Task Hybrid_EndpointWeightsExecuteOnlyActiveLeg_WithoutFallback()
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("match", "hello world"));
        Assert.HasCount(0, await store.HybridSearchAsync(null!, "absent",
            new HybridSearchOptions { VectorWeight = 0 }));
        var textOnly = await store.HybridSearchAsync(null!, "hello",
            new HybridSearchOptions { VectorWeight = 0 });
        Assert.AreEqual(1.0, textOnly[0].Score, 1e-10);
        var vectorOnly = await store.HybridSearchAsync(new[] { 1f, 0f }, null!,
            new HybridSearchOptions { VectorWeight = 1 });
        Assert.AreEqual(1.0, vectorOnly[0].Score, 1e-10);
    }

    [TestMethod]
    public async Task Hybrid_WeightChangesRanking_AndMinScoreUsesFusionScore()
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("dense", "different topic"));
        await store.UpsertAsync(new VectorRecord("text", new[] { 0f, 1f }, "needle"));
        var dense = await store.HybridSearchAsync(new[] { 1f, 0f }, "needle",
            new HybridSearchOptions { VectorWeight = .9f, RrfK = 1 });
        var text = await store.HybridSearchAsync(new[] { 1f, 0f }, "needle",
            new HybridSearchOptions { VectorWeight = .1f, RrfK = 1 });
        Assert.AreEqual("dense", dense[0].Record.Id);
        Assert.AreEqual("text", text[0].Record.Id);
        var filtered = await store.HybridSearchAsync(new[] { 1f, 0f }, "needle",
            new HybridSearchOptions { VectorWeight = .9f, RrfK = 1 }, filter: new VectorFilter().WithMinScore(.8));
        Assert.HasCount(1, filtered);
        Assert.AreEqual("dense", filtered[0].Record.Id);
    }

    [TestMethod]
    public async Task Hybrid_NoTextCandidates_DoesNotChangeScoreMeaning()
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("dense", "hello"));
        var result = await store.HybridSearchAsync(new[] { 1f, 0f }, "absent");
        Assert.AreEqual(.5, result[0].Score, 1e-10);
    }

    [TestMethod]
    public async Task SearchTies_UseStableOrdinalRecordId()
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("z", "identical content"));
        await store.UpsertAsync(Record("a", "identical content"));
        Assert.AreEqual("a", (await store.TextSearchAsync("identical", 1))[0].Record.Id);
        Assert.AreEqual("a", (await store.SearchAsync(new[] { 1f, 0f }, 1))[0].Record.Id);
        Assert.AreEqual("a", (await store.HybridSearchAsync(new[] { 1f, 0f }, "identical", 1))[0].Record.Id);
    }

    [TestMethod]
    public async Task TextAndHybrid_ObservePreCancellation()
    {
        using var store = new InMemoryVectorStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.TextSearchAsync("test", cancellationToken: cts.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.HybridSearchAsync(new[] { 1f, 0f }, "test", cancellationToken: cts.Token));
    }

    [TestMethod]
    public void Options_RejectInvalidValuesAndOverflow_AndSnapshotIsIndependent()
    {
        foreach (var value in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -.1f, 1.1f })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HybridSearchOptions { VectorWeight = value }.Snapshot());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HybridSearchOptions { CandidateMultiplier = 0 }.Snapshot());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HybridSearchOptions { RrfK = 0 }.Snapshot());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HybridSearchOptions().GetCandidateCount(int.MaxValue));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HybridSearchOptions().GetCandidateCount(0));
        var options = new HybridSearchOptions();
        var snapshot = options.Snapshot();
        options.VectorWeight = 1;
        Assert.AreEqual(.5f, snapshot.VectorWeight);
    }

    [TestMethod]
    public void Fusion_DeduplicatesAndHandlesLargeRrfConstant()
    {
        var a = new VectorSearchResult(Record("a", "first"), 2);
        var b = new VectorSearchResult(Record("b", "second"), 1);
        var results = HybridSearchFusion.Merge(new[] { a, a, b }, new[] { a },
            new HybridSearchOptions { RrfK = int.MaxValue }, 5);
        Assert.HasCount(2, results);
        Assert.AreEqual("a", results[0].Record.Id);
        Assert.AreEqual(1.0, results[0].Score, 1e-10);
        Assert.IsTrue(results.All(result => double.IsFinite(result.Score) && result.Score >= 0 && result.Score <= 1));
    }

    private static VectorRecord Record(string id, string content, string tenant = "mine")
        => new VectorRecord(id, new[] { 1f, 0f }, content) { Metadata = { ["tenant"] = tenant } };
}
