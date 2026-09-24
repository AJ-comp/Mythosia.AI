using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class IndexingOutputContractTests
{
    [TestMethod]
    [DataRow("missing-id", false)] [DataRow("missing-id", true)]
    [DataRow("null-id", false)] [DataRow("null-id", true)]
    [DataRow("whitespace-id", false)] [DataRow("whitespace-id", true)]
    [DataRow("duplicate-id", false)] [DataRow("duplicate-id", true)]
    [DataRow("null-list", false)] [DataRow("null-list", true)]
    [DataRow("null-chunk", false)] [DataRow("null-chunk", true)]
    [DataRow("null-content", false)] [DataRow("null-content", true)]
    [DataRow("null-metadata", false)] [DataRow("null-metadata", true)]
    public async Task InvalidSplitterOutput_IsRejectedBeforeEmbeddingAndPersistence(string mode, bool callback)
    {
        using var store = new InMemoryVectorStore();
        await Seed(store);
        var before = Snapshot(await store.ListAllRecordsAsync());
        var embedding = new ProbeEmbedding();
        var splitter = new DelegateSplitter(document =>
        {
            var chunks = ValidChunks(document).ToArray();
            switch (mode)
            {
                case "missing-id": chunks[1].Id = ""; break;
                case "null-id": chunks[1].Id = null!; break;
                case "whitespace-id": chunks[1].Id = " \t"; break;
                case "duplicate-id": chunks[1].Id = chunks[0].Id; break;
                case "null-list": return null!;
                case "null-chunk": chunks[1] = null!; break;
                case "null-content": chunks[1].Content = null!; break;
                case "null-metadata": chunks[1].Metadata = null!; break;
            }
            return chunks;
        });
        int callbacks = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Index(store, embedding, splitter, callback,
            _ => { callbacks++; return Task.CompletedTask; }));
        Assert.AreEqual(0, embedding.Calls);
        Assert.AreEqual(0, callbacks);
        CollectionAssert.AreEqual(before, Snapshot(await store.ListAllRecordsAsync()));
    }

    [TestMethod]
    [DataRow(null, false)] [DataRow(null, true)]
    [DataRow("", false)] [DataRow("", true)]
    [DataRow(" \t", false)] [DataRow(" \t", true)]
    public async Task InvalidDocumentIdentity_IsRejectedEvenForEmptyUpdates(string? id, bool empty)
    {
        using var store = new InMemoryVectorStore();
        await Seed(store);
        var before = Snapshot(await store.ListAllRecordsAsync());
        var embedding = new ProbeEmbedding();
        int splitCalls = 0;
        var splitter = new DelegateSplitter(document => { splitCalls++; return ValidChunks(document); });
        var pipeline = Pipeline(store, embedding, splitter);
        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.IndexDocumentAsync(
            new RagDocument(id!, empty ? "" : "Alpha|Beta", "input.txt")));
        Assert.AreEqual(0, splitCalls);
        Assert.AreEqual(0, embedding.Calls);
        CollectionAssert.AreEqual(before, Snapshot(await store.ListAllRecordsAsync()));
    }

    [TestMethod]
    public async Task CorrectedDocumentedSplitter_PreservesAllDocumentsAndInheritedMetadata()
    {
        using var store = new InMemoryVectorStore();
        var splitter = new DelegateSplitter(document => document.Content.Split(". ")
            .Select((sentence, i) => new RagChunk
            {
                Id = $"{document.Id}_chunk_{i}", Content = sentence, Index = i, DocumentId = document.Id,
                Metadata = new Dictionary<string, string>(document.Metadata)
            }).ToArray());
        var pipeline = Pipeline(store, new ProbeEmbedding(), splitter);
        var a = new RagDocument("A", "First. Second. Third", "a.txt"); a.Metadata["tenant"] = "company-a";
        var b = new RagDocument("B", "Other first. Other second", "b.txt"); b.Metadata["tenant"] = "company-b";
        await pipeline.IndexDocumentAsync(a);
        await pipeline.IndexDocumentAsync(b);
        var records = await store.ListAllRecordsAsync();
        Assert.AreEqual(5, records.Count);
        Assert.AreEqual(3, records.Count(r => r.Metadata["document_id"] == "A" && r.Metadata["tenant"] == "company-a"));
        Assert.AreEqual(2, records.Count(r => r.Metadata["document_id"] == "B" && r.Metadata["tenant"] == "company-b"));
        CollectionAssert.AreEquivalent(new[] { "First", "Second", "Third", "Other first", "Other second" },
            records.Select(r => r.Content).ToArray());
    }

    [TestMethod]
    public async Task ValidCustomIds_ArePreservedUsingOrdinalUniqueness()
    {
        using var store = new InMemoryVectorStore();
        var splitter = new DelegateSplitter(document => new[]
        {
            new RagChunk("Case", document.Id, "Alpha", 0),
            new RagChunk("case", document.Id, "Beta", 1)
        });
        await Index(store, new ProbeEmbedding(), splitter, false, _ => throw new AssertFailedException());
        CollectionAssert.AreEquivalent(new[] { "Case", "case" }, (await store.ListAllRecordsAsync()).Select(r => r.Id).ToArray());
    }

    [TestMethod]
    [DataRow("extra", false)] [DataRow("extra", true)]
    [DataRow("short", false)] [DataRow("short", true)]
    [DataRow("balanced-bad-counts", false)] [DataRow("balanced-bad-counts", true)]
    [DataRow("null-list", false)] [DataRow("null-list", true)]
    [DataRow("null-vector", false)] [DataRow("null-vector", true)]
    [DataRow("dimension", false)] [DataRow("dimension", true)]
    [DataRow("nan", false)] [DataRow("nan", true)]
    [DataRow("positive-infinity", false)] [DataRow("positive-infinity", true)]
    [DataRow("negative-infinity", false)] [DataRow("negative-infinity", true)]
    public async Task InvalidEmbeddingBatch_IsRejectedWithoutReplacingOldRecords(string mode, bool callback)
    {
        using var store = new InMemoryVectorStore();
        await Seed(store);
        var before = Snapshot(await store.ListAllRecordsAsync());
        var embedding = new ProbeEmbedding
        {
            Transform = (call, vectors) =>
            {
                // A first valid batch must not be persisted if a later batch is malformed.
                if (call == 1 && mode != "balanced-bad-counts") return vectors;
                switch (mode)
                {
                    case "extra": return vectors.Append(new[] { 9f, 9f }).ToArray();
                    case "short": return vectors.Take(1).ToArray();
                    case "balanced-bad-counts": return call == 1 ? vectors.Take(1).ToArray() : vectors.Append(new[] { 9f, 9f }).ToArray();
                    case "null-list": return null!;
                    case "null-vector": vectors[1] = null!; break;
                    case "dimension": vectors[1] = new[] { 1f }; break;
                    case "nan": vectors[1][0] = float.NaN; break;
                    case "positive-infinity": vectors[1][0] = float.PositiveInfinity; break;
                    case "negative-infinity": vectors[1][0] = float.NegativeInfinity; break;
                }
                return vectors;
            }
        };
        int callbacks = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Index(store, embedding,
            new DelegateSplitter(ValidChunks), callback, _ => { callbacks++; return Task.CompletedTask; }, batchSize: 2));
        Assert.AreEqual(mode == "balanced-bad-counts" ? 1 : 2, embedding.Calls);
        Assert.AreEqual(0, callbacks);
        CollectionAssert.AreEqual(before, Snapshot(await store.ListAllRecordsAsync()));
    }

    [TestMethod]
    [DataRow(0)] [DataRow(-1)] [DataRow(int.MinValue)]
    public async Task InvalidProviderDimensions_AreRejectedBeforeEmbedding(int dimensions)
    {
        using var store = new InMemoryVectorStore();
        await Seed(store);
        var before = Snapshot(await store.ListAllRecordsAsync());
        var embedding = new ProbeEmbedding { Dimensions = dimensions };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Index(store, embedding,
            new DelegateSplitter(ValidChunks), false, _ => throw new AssertFailedException()));
        Assert.AreEqual(0, embedding.Calls);
        CollectionAssert.AreEqual(before, Snapshot(await store.ListAllRecordsAsync()));
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ReusedBatchBuffers_AreCopiedBeforeNextRequest(bool callback)
    {
        using var store = new InMemoryVectorStore();
        var buffers = new[] { new float[2], new float[2] };
        var embedding = new ProbeEmbedding
        {
            Transform = (call, _) =>
            {
                for (int i = 0; i < buffers.Length; i++)
                {
                    buffers[i][0] = call * 10 + i;
                    buffers[i][1] = call;
                }
                return buffers;
            }
        };
        IReadOnlyList<VectorRecord>? captured = null;
        await Index(store, embedding, new DelegateSplitter(ValidChunks), callback,
            records => { captured = records; return Task.CompletedTask; }, batchSize: 2);
        var records = callback ? captured! : await store.ListAllRecordsAsync();
        CollectionAssert.AreEqual(new[] { 10f, 11f, 20f, 21f }, records.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Vector[0]).ToArray());
        foreach (var buffer in buffers) Array.Fill(buffer, 999f);
        CollectionAssert.AreEqual(new[] { 10f, 11f, 20f, 21f }, records.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Vector[0]).ToArray());
        Assert.IsTrue(records.All(r => buffers.All(buffer => !ReferenceEquals(r.Vector, buffer))));
    }

    [TestMethod]
    public async Task SplitterOutput_IsCapturedBeforeAwaitingExternalEmbedding()
    {
        using var store = new InMemoryVectorStore();
        await Seed(store);
        var chunks = ValidChunks(new RagDocument("policy", "Alpha|Beta", "policy.txt")).ToArray();
        chunks[0].Metadata["tenant"] = "company-a";
        var embedding = new ProbeEmbedding
        {
            Transform = (_, vectors) =>
            {
                chunks[0].Id = "other_chunk_0";
                chunks[0].Content = "WRONG";
                chunks[0].Metadata["tenant"] = "changed";
                chunks[1] = null!;
                return vectors;
            }
        };
        await Pipeline(store, embedding, new DelegateSplitter(_ => chunks)).IndexDocumentAsync(
            new RagDocument("policy", "Alpha|Beta", "policy.txt"));
        var records = await store.ListAllRecordsAsync();
        Assert.AreEqual("KEEP", records.Single(r => r.Id == "other_chunk_0").Content);
        Assert.AreEqual("Alpha", records.Single(r => r.Id == "policy_chunk_0").Content);
        Assert.AreEqual("company-a", records.Single(r => r.Id == "policy_chunk_0").Metadata["tenant"]);
        Assert.AreEqual("Beta", records.Single(r => r.Id == "policy_chunk_1").Content);
    }

    private static IReadOnlyList<RagChunk> ValidChunks(RagDocument document) => document.Content.Split('|')
        .Select((text, i) => new RagChunk($"{document.Id}_chunk_{i}", document.Id, text, i)
        { Metadata = new Dictionary<string, string>(document.Metadata) }).ToArray();

    private static RagPipeline Pipeline(InMemoryVectorStore store, IEmbeddingProvider embedding, ITextSplitter splitter, int batchSize = 100) =>
        new(embedding, store, splitter, new DefaultContextBuilder(), new RagPipelineOptions { EmbeddingBatchSize = batchSize });

    private static async Task Seed(InMemoryVectorStore store)
    {
        var pipeline = Pipeline(store, new ProbeEmbedding(), new CharacterTextSplitter(100, 0));
        await pipeline.IndexDocumentAsync(new RagDocument("policy", "OLD", "policy.txt"));
        await pipeline.IndexDocumentAsync(new RagDocument("other", "KEEP", "other.txt"));
    }

    private static Task Index(InMemoryVectorStore store, IEmbeddingProvider embedding, ITextSplitter splitter,
        bool callback, Func<IReadOnlyList<VectorRecord>, Task> persist, int batchSize = 100)
    {
        const string input = "Alpha|Beta|Gamma|Delta";
        if (callback && batchSize != 100)
            return Pipeline(store, embedding, splitter, batchSize).IndexDocumentsAsync(
                new[] { new RagDocument("policy", input, "policy.txt") }, textSplitter: null,
                onDocumentEmbedded: persist, cancellationToken: CancellationToken.None);
        return callback
            ? RagStore.BuildAsync(builder => builder.AddText(input, "policy").UseStore(store).UseEmbedding(embedding)
                .WithTextSplitter(splitter), onDocumentEmbedded: persist)
            : Pipeline(store, embedding, splitter, batchSize).IndexDocumentAsync(new RagDocument("policy", input, "policy.txt"));
    }

    private static string[] Snapshot(IEnumerable<VectorRecord> records) => records.OrderBy(r => r.Id, StringComparer.Ordinal)
        .Select(r => $"{r.Id}|{r.Content}|{r.Metadata["document_id"]}|{string.Join(",", r.Vector)}").ToArray();

    private sealed class DelegateSplitter(Func<RagDocument, IReadOnlyList<RagChunk>> split) : ITextSplitter
    {
        public IReadOnlyList<RagChunk> Split(RagDocument document) => split(document);
    }

    private sealed class ProbeEmbedding : IEmbeddingProvider
    {
        public int Dimensions { get; set; } = 2;
        public int Calls { get; private set; }
        public Func<int, float[][], IReadOnlyList<float[]>>? Transform { get; init; }
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(new[] { 1f, 0f });
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var vectors = texts.Select((_, i) => new[] { 1f, (float)i }).ToArray();
            return Task.FromResult<IReadOnlyList<float[]>>(Transform == null ? vectors : Transform(Calls, vectors));
        }
    }
}
