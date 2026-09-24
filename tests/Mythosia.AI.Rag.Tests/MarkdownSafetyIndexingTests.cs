using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class MarkdownSafetyIndexingTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task ExcessiveRepeatedContext_FailsBeforeEmbeddingOrPersistence_AndCanBeRetried(
        bool headings, bool customPersistence)
    {
        using var vectors = new InMemoryVectorStore();
        var embedding = new RecordingEmbedding();
        var splitter = new MarkdownTextSplitter();
        var pipeline = new RagPipeline(embedding, vectors, splitter, new DefaultContextBuilder());
        await pipeline.IndexDocumentAsync(new RagDocument("policy", "Old valid policy.", "policy.md"));
        await pipeline.IndexDocumentAsync(new RagDocument("other", "Keep other document.", "other.md"));
        var before = Snapshot(await vectors.ListAllRecordsAsync());
        int previousEmbeddingCalls = embedding.Calls;
        int persistenceCalls = 0;

        var input = headings
            ? "# " + new string('h', 32_000) + "\n" + string.Concat(Enumerable.Repeat("## Child\nx\n", 2_000))
            : "|" + new string('h', 32_000) + "|value|\n|---|---|\n" + string.Concat(Enumerable.Repeat("|x|y|\n", 2_000));

        if (customPersistence)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => RagStore.BuildAsync(
                builder => builder.AddText(input, "policy").WithTextSplitter(splitter)
                    .UseStore(vectors).UseEmbedding(embedding),
                onDocumentEmbedded: _ =>
                {
                    persistenceCalls++;
                    return Task.CompletedTask;
                }));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pipeline.IndexDocumentAsync(new RagDocument("policy", input, "policy.md")));
        }

        Assert.AreEqual(previousEmbeddingCalls, embedding.Calls,
            "No embedding may begin for a document rejected by the splitter.");
        Assert.AreEqual(0, persistenceCalls);
        CollectionAssert.AreEqual(before, Snapshot(await vectors.ListAllRecordsAsync()),
            "The failed update must preserve the old document and unrelated records.");

        // A rejected split must not consume a shared allowance or poison the same splitter.
        await pipeline.IndexDocumentAsync(new RagDocument("policy", "New valid policy.", "policy.md"));
        var after = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(2, after.Count);
        Assert.AreEqual("New valid policy.", after.Single(r => r.Metadata["document_id"] == "policy").Content);
        Assert.AreEqual("Keep other document.", after.Single(r => r.Metadata["document_id"] == "other").Content);
    }

    private static string[] Snapshot(IEnumerable<VectorRecord> records) => records.OrderBy(r => r.Id)
        .Select(r => $"{r.Id}|{r.Content}|{r.Metadata["document_id"]}|{string.Join(",", r.Vector)}").ToArray();

    private sealed class RecordingEmbedding : IEmbeddingProvider
    {
        public int Dimensions => 1;
        public int Calls { get; private set; }
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new[] { 1f });
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f }).ToArray());
        }
    }
}
