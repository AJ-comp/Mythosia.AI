using Mythosia.VectorDb.InMemory;

namespace Mythosia.VectorDb.Tests.InMemory;

[TestClass]
public sealed class InMemoryDiagnosticsContractTests
{
    [TestMethod]
    public async Task NeutralContract_ProvidesAllRecordsAndDescendingUnfilteredScores()
    {
        using var store = new InMemoryVectorStore();
        IVectorStoreDiagnostics diagnostics = store;
        await store.UpsertBatchAsync(new[]
        {
            new VectorRecord("opposite", [-1, 0], "opposite"),
            new VectorRecord("same", [1, 0], "same"),
            new VectorRecord("orthogonal", [0, 1], "orthogonal")
        });

        CollectionAssert.AreEquivalent(new[] { "same", "orthogonal", "opposite" },
            (await diagnostics.ListAllRecordsAsync()).Select(r => r.Id).ToArray());
        var scored = await diagnostics.ScoredListAsync([1, 0]);
        CollectionAssert.AreEqual(new[] { "same", "orthogonal", "opposite" }, scored.Select(r => r.Record.Id).ToArray());
        CollectionAssert.AreEqual(new double[] { 1, 0, -1 }, scored.Select(r => r.Score).ToArray());
    }

    [TestMethod]
    public void VectorAssemblies_DoNotReferenceRagAssemblies()
    {
        foreach (var assembly in new[] { typeof(IVectorStoreDiagnostics).Assembly, typeof(InMemoryVectorStore).Assembly })
        {
            Assert.IsFalse(assembly.GetReferencedAssemblies().Any(reference =>
                reference.Name?.StartsWith("Mythosia.AI.Rag", StringComparison.Ordinal) == true),
                $"{assembly.GetName().Name} must remain usable without a RAG assembly dependency.");
        }
    }
}
