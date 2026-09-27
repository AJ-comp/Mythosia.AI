using Mythosia.AI.Rag.Diagnostics;
using Mythosia.AI.Rag.Retrieval;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RetrievalEmbeddingContractTests
{
    [TestMethod]
    public async Task WholeDocuments_BypassFlatBatchLimitAndRetainOrderIdentityTitleAndOriginalText()
    {
        using var vectors = new InMemoryVectorStore();
        var provider = new RetrievalProbe();
        var pipeline = Pipeline(provider, vectors, batchSize: 2);
        var chunks = Enumerable.Range(0, 101).Select(i => $"alpha {i}").ToArray();
        var first = Document("a", string.Join("|", chunks));
        first.Metadata["title"] = "Alpha guide";
        await pipeline.IndexDocumentsAsync(new[] { first, Document("b", "beta one|beta two") });

        Assert.HasCount(2, provider.Documents);
        Assert.AreEqual("a", provider.Documents[0].DocumentId);
        Assert.AreEqual("Alpha guide", provider.Documents[0].Title);
        CollectionAssert.AreEqual(chunks, provider.Documents[0].Chunks.ToArray());
        CollectionAssert.AreEqual(new[] { "beta one", "beta two" }, provider.Documents[1].Chunks.ToArray());
        Assert.AreEqual("b", provider.Documents[1].DocumentId);
        Assert.AreEqual(103L, await vectors.CountAsync());
        for (var i = 0; i < chunks.Length; i++)
            Assert.AreEqual(chunks[i], (await vectors.GetAsync($"a:{i}"))!.Content);
        Assert.AreEqual(0, provider.LegacyCalls);
        Assert.AreEqual("Alpha guide", first.Metadata["title"]);
    }

    [TestMethod]
    public async Task LegacyProvider_RetainsBatchSizesAndQueryMethod()
    {
        using var vectors = new InMemoryVectorStore();
        var legacy = new LegacyProbe();
        var pipeline = Pipeline(legacy, vectors, batchSize: 2);
        await pipeline.IndexDocumentAsync(Document("a", "alpha|two|three|four|five"));
        CollectionAssert.AreEqual(new[] { 2, 2, 1 }, legacy.BatchSizes.ToArray());
        Assert.HasCount(1, (await pipeline.QueryAsync("alpha", 1)).SearchResults);
        Assert.AreEqual(1, legacy.QueryCalls);
    }

    [TestMethod]
    [DataRow("vector")]
    [DataRow("hybrid")]
    [DataRow("legacy")]
    public async Task QueryPurpose_IsUsedByEveryDenseRetrievalPath(string mode)
    {
        using var vectors = new InMemoryVectorStore();
        var provider = new RetrievalProbe();
        await vectors.UpsertAsync(new VectorRecord { Id = "a", Content = "alpha", Vector = [1, 0] });
        await vectors.UpsertAsync(new VectorRecord { Id = "b", Content = "beta", Vector = [0, 1] });
        IRagRetriever retriever = mode switch
        {
            "hybrid" => new RagRetrievers.Hybrid(provider, vectors, new HybridSearchOptions()),
            "legacy" => new RagRetrievers.Legacy(new LegacyStrategy(vectors), provider),
            _ => new RagRetrievers.Vector(provider, vectors)
        };
        var results = await retriever.RetrieveAsync(new RagRetrievalRequest("alpha", "alpha", 1), default);
        Assert.AreEqual("a", results.Single().Record.Id);
        CollectionAssert.AreEqual(new[] { "alpha" }, provider.Queries.ToArray());
        Assert.AreEqual(0, provider.LegacyCalls);
    }

    [TestMethod]
    public async Task Diagnostics_UsesSameQueryPurposeAndValidationAsRetrieval()
    {
        using var vectors = new InMemoryVectorStore();
        var provider = new RetrievalProbe();
        var pipeline = Pipeline(provider, vectors);
        await pipeline.IndexDocumentAsync(Document("a", "alpha"));
        await new RagDiagnostics(pipeline).DiagnoseQueryAsync("alpha");
        Assert.AreEqual(1, provider.Queries.Count);
        provider.QueryResult = [float.NaN, 0];
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RagDiagnostics(pipeline).DiagnoseQueryAsync("alpha"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KeywordOnly_DoesNotCallRetrievalAwareEmbedding(bool zeroVectorWeight)
    {
        using var vectors = new InMemoryVectorStore();
        await vectors.UpsertAsync(new VectorRecord { Id = "a", Content = "alpha", Vector = [1, 0] });
        var provider = new RetrievalProbe();
        var store = await RagStore.BuildAsync(builder =>
        {
            builder.UseEmbedding(provider).UseStore(vectors);
            if (zeroVectorWeight) builder.UseHybridSearch(0);
            else builder.UseKeywordSearch();
        });
        Assert.AreEqual("a", (await store.QueryAsync("alpha")).References.Single().Record.Id);
        Assert.HasCount(0, provider.Queries);
        Assert.HasCount(0, provider.Documents);
        Assert.AreEqual(0, provider.LegacyCalls);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("extra")]
    [DataRow("dimension")]
    [DataRow("nan")]
    [DataRow("infinity")]
    [DataRow("null")]
    [DataRow("http")]
    [DataRow("cancel")]
    public async Task InvalidOrCanceledDocumentEmbedding_LeavesExistingDocumentUntouched(string failure)
    {
        using var vectors = new InMemoryVectorStore();
        var provider = new RetrievalProbe();
        var pipeline = Pipeline(provider, vectors);
        await pipeline.IndexDocumentAsync(Document("a", "old text"));
        using var cancel = new CancellationTokenSource();
        provider.DocumentResult = (_, _) => failure switch
        {
            "missing" => Task.FromResult<IReadOnlyList<float[]>>([]),
            "extra" => Task.FromResult<IReadOnlyList<float[]>>([[1, 0], [1, 0]]),
            "dimension" => Task.FromResult<IReadOnlyList<float[]>>([[1]]),
            "nan" => Task.FromResult<IReadOnlyList<float[]>>([[float.NaN, 0]]),
            "infinity" => Task.FromResult<IReadOnlyList<float[]>>([[float.PositiveInfinity, 0]]),
            "null" => Task.FromResult<IReadOnlyList<float[]>>(null!),
            "http" => throw new HttpRequestException("Synthetic transport failure"),
            _ => CancelAfterResult(cancel)
        };
        await Assert.ThrowsAsync<Exception>(() => pipeline.IndexDocumentAsync(Document("a", "replacement"), cancel.Token));
        Assert.AreEqual("old text", (await vectors.GetAsync("a:0"))!.Content);
        Assert.AreEqual(1L, await vectors.CountAsync());
    }

    [TestMethod]
    public async Task EmptyDocument_ClearsPreviousIndexWithoutRequestingEmbeddings()
    {
        using var vectors = new InMemoryVectorStore();
        var provider = new RetrievalProbe();
        var pipeline = Pipeline(provider, vectors);
        await pipeline.IndexDocumentAsync(Document("a", "old text"));
        await pipeline.IndexDocumentAsync(Document("a", ""));
        Assert.HasCount(1, provider.Documents);
        Assert.AreEqual(0L, await vectors.CountAsync());
    }

    [TestMethod]
    public async Task ReusedDocumentVectors_AreCopiedBeforeNextDocumentCall()
    {
        using var vectors = new InMemoryVectorStore();
        var provider = new RetrievalProbe();
        float[] buffer = [1, 0];
        provider.DocumentResult = (document, _) =>
        {
            buffer[0] = document.DocumentId == "a" ? 1 : 0;
            buffer[1] = document.DocumentId == "a" ? 0 : 1;
            return Task.FromResult<IReadOnlyList<float[]>>([buffer]);
        };
        await Pipeline(provider, vectors).IndexDocumentsAsync(new[] { Document("a", "alpha"), Document("b", "beta") });
        CollectionAssert.AreEqual(new float[] { 1, 0 }, (await vectors.GetAsync("a:0"))!.Vector);
        CollectionAssert.AreEqual(new float[] { 0, 1 }, (await vectors.GetAsync("b:0"))!.Vector);
    }

    [TestMethod]
    public void EmbeddingDocument_SnapshotsInputAndDoesNotExposeMutableCollection()
    {
        var texts = new[] { "first", "second" };
        var document = new EmbeddingDocument("a", texts, "title");
        texts[0] = "changed";
        Assert.AreEqual("first", document.Chunks[0]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)document.Chunks)[0] = "changed");
        Assert.ThrowsExactly<ArgumentException>(() => new EmbeddingDocument(" ", []));
        Assert.ThrowsExactly<ArgumentException>(() => new EmbeddingDocument("a", [null!]));
    }

    private static Task<IReadOnlyList<float[]>> CancelAfterResult(CancellationTokenSource source)
    { source.Cancel(); return Task.FromResult<IReadOnlyList<float[]>>([[1, 0]]); }
    private static RagDocument Document(string id, string text) => new(id, text, id + ".txt");
    private static RagPipeline Pipeline(IEmbeddingProvider provider, IVectorStore vectors, int batchSize = 100)
        => new(provider, vectors, new OrderedSplitter(), new DefaultContextBuilder(), new RagPipelineOptions { EmbeddingBatchSize = batchSize });

    private sealed class OrderedSplitter : ITextSplitter
    {
        public IReadOnlyList<RagChunk> Split(RagDocument document)
            => document.Content.Length == 0 ? [] : document.Content.Split('|').Select((text, i) =>
                new RagChunk($"{document.Id}:{i}", document.Id, text, i) { Metadata = new(document.Metadata) }).ToArray();
    }
    private sealed class RetrievalProbe : IRetrievalEmbeddingProvider
    {
        public int Dimensions => 2;
        public int LegacyCalls;
        public List<EmbeddingDocument> Documents { get; } = [];
        public List<string> Queries { get; } = [];
        public float[] QueryResult { get; set; } = [1, 0];
        public Func<EmbeddingDocument, CancellationToken, Task<IReadOnlyList<float[]>>>? DocumentResult;
        public Task<IReadOnlyList<float[]>> GetDocumentEmbeddingsAsync(EmbeddingDocument document, CancellationToken cancellationToken = default)
        { Documents.Add(document); return DocumentResult?.Invoke(document, cancellationToken) ?? Task.FromResult<IReadOnlyList<float[]>>(document.Chunks.Select(_ => new float[] { 1, 0 }).ToArray()); }
        public Task<float[]> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default)
        { Queries.Add(query); return Task.FromResult(QueryResult); }
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        { LegacyCalls++; throw new AssertFailedException("RAG must use the explicit query method."); }
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        { LegacyCalls++; throw new AssertFailedException("RAG must preserve the whole document."); }
    }
    private sealed class LegacyProbe : IEmbeddingProvider
    {
        public int Dimensions => 2;
        public List<int> BatchSizes { get; } = [];
        public int QueryCalls;
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        { QueryCalls++; return Task.FromResult(new float[] { 1, 0 }); }
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        { var values = texts.ToArray(); BatchSizes.Add(values.Length); return Task.FromResult<IReadOnlyList<float[]>>(values.Select(_ => new float[] { 1, 0 }).ToArray()); }
    }
    private sealed class LegacyStrategy(IVectorStore store) : IRetrievalStrategy
    {
        public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(float[] denseVector, string? query, int topK = 5,
            VectorFilter? filter = null, CancellationToken cancellationToken = default)
            => store.SearchAsync(denseVector, topK, filter, cancellationToken);
    }
}
