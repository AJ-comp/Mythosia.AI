using Mythosia.AI.Rag.Splitters;
using Mythosia.Documents;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class PipelineIndexingValidationTests
{
    [TestMethod]
    [DataRow("character")]
    [DataRow("recursive")]
    [DataRow("markdown")]
    [DataRow("token")]
    public async Task ReservedDocumentId_IsNormalizedForReindexAndEmptyUpdate(string kind)
    {
        using var vectors = new InMemoryVectorStore();
        var pipeline = CreatePipeline(vectors, splitter: CreateSplitter(kind));
        var document = Document("company-a", "alpha beta gamma delta epsilon");
        document.Metadata["document_id"] = "company-b";
        document.Metadata["tenant"] = "tenant-a";
        await pipeline.IndexDocumentAsync(Document("company-b", "KEEP"));
        await pipeline.IndexDocumentAsync(document);

        var originalA = (await vectors.ListAllRecordsAsync()).Where(IsCompanyA).ToArray();
        Assert.IsGreaterThan(1, originalA.Length, "The fixture must produce multiple chunks.");
        Assert.IsTrue(originalA.All(record => record.Metadata["document_id"] == "company-a"));
        Assert.IsTrue(originalA.All(record => record.Metadata["tenant"] == "tenant-a"));
        Assert.AreEqual("company-b", document.Metadata["document_id"], "The caller's input metadata is unchanged.");

        await pipeline.IndexDocumentAsync(Document("company-b", "BNEW"));
        var afterOtherUpdate = await vectors.ListAllRecordsAsync();
        CollectionAssert.AreEqual(Snapshot(originalA), Snapshot(afterOtherUpdate.Where(IsCompanyA)),
            "Replacing company B must not remove company A's chunks.");

        document.Content = "NEW";
        await pipeline.IndexDocumentAsync(document);
        var shorterA = (await vectors.ListAllRecordsAsync()).Where(IsCompanyA).ToArray();
        Assert.AreEqual(1, shorterA.Length, "A shorter replacement must remove old trailing chunks.");
        Assert.AreEqual("NEW", shorterA[0].Content);
        Assert.AreEqual("company-a", shorterA[0].Metadata["document_id"]);

        document.Content = "";
        await pipeline.IndexDocumentAsync(document);
        var remaining = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("company-b", remaining[0].Metadata["document_id"]);
        Assert.AreEqual("BNEW", remaining[0].Content);
        Assert.AreEqual("company-b", document.Metadata["document_id"]);
    }

    [TestMethod]
    public async Task ReservedDocumentId_DoesNotMutateCustomSplitterOrDocumentMetadata()
    {
        using var vectors = new InMemoryVectorStore();
        var document = Document("canonical", "policy");
        document.Metadata["document_id"] = "caller-value";
        document.Metadata["tenant"] = "tenant-a";
        var chunk = new RagChunk
        {
            Id = "custom-chunk", DocumentId = document.Id, Content = document.Content,
            Metadata = document.Metadata
        };
        var splitter = new DelegateSplitter(_ => new[] { chunk });
        var pipeline = CreatePipeline(vectors, splitter: splitter);

        await pipeline.IndexDocumentAsync(document);

        var stored = (await vectors.ListAllRecordsAsync()).Single();
        Assert.AreEqual("canonical", stored.Metadata["document_id"]);
        Assert.AreEqual("caller-value", chunk.Metadata["document_id"]);
        Assert.AreEqual("caller-value", document.Metadata["document_id"]);
        Assert.AreNotSame(chunk.Metadata, stored.Metadata);
        stored.Metadata["tenant"] = "stored-value";
        Assert.AreEqual("tenant-a", chunk.Metadata["tenant"]);
        chunk.Metadata["tenant"] = "input-value";
        Assert.AreEqual("stored-value", stored.Metadata["tenant"]);
    }

    [TestMethod]
    public async Task DocumentId_IsCapturedBeforeAwaitingEmbedding()
    {
        using var vectors = new InMemoryVectorStore();
        var embedding = new RecordingEmbedding();
        var pipeline = CreatePipeline(vectors, embedding, new CharacterTextSplitter(4, 0, null));
        await pipeline.IndexDocumentAsync(Document("company-a", "OLD POLICY WITH TRAILING CHUNKS"));
        await pipeline.IndexDocumentAsync(Document("company-b", "KEEP"));
        var document = Document("company-a", "NEW");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        embedding.BeforeReturnAsync = async _ =>
        {
            entered.SetResult();
            await release.Task;
        };

        var indexing = pipeline.IndexDocumentAsync(document);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        document.Id = "company-b";
        release.SetResult();
        await indexing.WaitAsync(TimeSpan.FromSeconds(5));

        var records = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(2, records.Count);
        var replaced = records.Single(record => record.Id == "company-a_chunk_0");
        Assert.AreEqual("NEW", replaced.Content);
        Assert.AreEqual("company-a", replaced.Metadata["document_id"]);
        Assert.AreEqual("KEEP", records.Single(record => record.Id == "company-b_chunk_0").Content);
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(-1, false)]
    [DataRow(int.MinValue, false)]
    [DataRow(0, true)]
    [DataRow(-1, true)]
    [DataRow(int.MinValue, true)]
    public async Task InvalidEmbeddingBatchSize_IsRejectedBeforeSplitEmbeddingOrDeletion(int batchSize, bool empty)
    {
        using var vectors = new InMemoryVectorStore();
        var embedding = new RecordingEmbedding();
        int splitCalls = 0;
        var splitter = new DelegateSplitter(document =>
        {
            splitCalls++;
            return new CharacterTextSplitter(8, 0, null).Split(document);
        });
        var pipeline = CreatePipeline(vectors, embedding, splitter);
        await pipeline.IndexDocumentAsync(Document("policy", "OLD POLICY"));
        var before = Snapshot(await vectors.ListAllRecordsAsync());
        var embeddingCalls = embedding.Batches.Count;
        var previousSplitCalls = splitCalls;
        pipeline.Options.EmbeddingBatchSize = batchSize;

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            pipeline.IndexDocumentAsync(Document("policy", empty ? "" : "replacement")));

        Assert.AreEqual(nameof(RagPipelineOptions.EmbeddingBatchSize), exception.ParamName);
        Assert.AreEqual(previousSplitCalls, splitCalls);
        Assert.AreEqual(embeddingCalls, embedding.Batches.Count);
        CollectionAssert.AreEqual(before, Snapshot(await vectors.ListAllRecordsAsync()));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public async Task InvalidEmbeddingBatchSize_IsRejectedBeforeLoadingOrEnumerating(int batchSize)
    {
        using var vectors = new InMemoryVectorStore();
        var embedding = new RecordingEmbedding();
        var pipeline = CreatePipeline(vectors, embedding);
        pipeline.Options.EmbeddingBatchSize = batchSize;
        var loader = new DelegateLoader((_, _) => throw new AssertFailedException("The loader must not run."));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pipeline.IndexAsync(loader, "source"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pipeline.IndexDocumentsAsync(Documents()));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pipeline.IndexDocumentsAsync(Array.Empty<RagDocument>()));
        Assert.AreEqual(0, embedding.Batches.Count);
        Assert.AreEqual(0, (await vectors.ListAllRecordsAsync()).Count);

        static IEnumerable<RagDocument> Documents()
        {
            yield return ThrowIfEnumerated();
        }
        static RagDocument ThrowIfEnumerated() => throw new AssertFailedException("Documents must not be enumerated.");
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 0)]
    [DataRow(false, 1)]
    [DataRow(true, 3)]
    public async Task IndexDocuments_CapturesBatchSizeAcrossAwaitAndDocuments(bool replaceOptions, int newBatchSize)
    {
        using var vectors = new InMemoryVectorStore();
        var embedding = new RecordingEmbedding();
        var pipeline = CreatePipeline(vectors, embedding, new CharacterTextSplitter(1, 0, null));
        pipeline.Options.EmbeddingBatchSize = 2;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        embedding.BeforeReturnAsync = async call =>
        {
            if (call == 1)
            {
                entered.SetResult();
                await release.Task;
            }
        };

        var indexing = pipeline.IndexDocumentsAsync(new[] { Document("a", "ABCDE"), Document("b", "FGHIJ") });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (replaceOptions)
            pipeline.Options = new RagPipelineOptions { EmbeddingBatchSize = newBatchSize };
        else
            pipeline.Options.EmbeddingBatchSize = newBatchSize;
        release.SetResult();
        await indexing.WaitAsync(TimeSpan.FromSeconds(5));

        CollectionAssert.AreEqual(new[] { 2, 2, 1, 2, 2, 1 }, embedding.Batches.Select(b => b.Length).ToArray());
        var records = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(10, records.Count);
        foreach (var record in records)
            Assert.AreEqual((float)record.Content[0], record.Vector[0], "Each vector must still describe its own chunk.");

        if (newBatchSize == 0)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pipeline.IndexDocumentAsync(Document("next", "KLMNO")));
            Assert.AreEqual(6, embedding.Batches.Count);
        }
        else
        {
            await pipeline.IndexDocumentAsync(Document("next", "KLMNO"));
            var expectedNextBatches = newBatchSize == 1 ? new[] { 1, 1, 1, 1, 1 } : new[] { 3, 2 };
            CollectionAssert.AreEqual(expectedNextBatches, embedding.Batches.Skip(6).Select(b => b.Length).ToArray(),
                "A subsequent indexing call must observe the updated configuration.");
        }
    }

    [TestMethod]
    public async Task IndexAsync_CapturesBatchSizeBeforeAwaitingLoader()
    {
        using var vectors = new InMemoryVectorStore();
        var embedding = new RecordingEmbedding();
        var pipeline = CreatePipeline(vectors, embedding, new CharacterTextSplitter(1, 0, null));
        pipeline.Options.EmbeddingBatchSize = 2;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = new DelegateLoader(async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return new[] { new DoclingDocument { Name = "loaded", Source = "loaded", RawContent = "ABCDE" } };
        });

        var indexing = pipeline.IndexAsync(loader, "loaded");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pipeline.Options.EmbeddingBatchSize = 0;
        release.SetResult();
        await indexing.WaitAsync(TimeSpan.FromSeconds(5));

        CollectionAssert.AreEqual(new[] { 2, 2, 1 }, embedding.Batches.Select(b => b.Length).ToArray());
        Assert.AreEqual(5, (await vectors.ListAllRecordsAsync()).Count);
    }

    [TestMethod]
    [DataRow(int.MaxValue)]
    [DataRow(2147483646)]
    public async Task VeryLargeEmbeddingBatchSize_UsesOnlyTheRemainingChunks(int batchSize)
    {
        using var vectors = new InMemoryVectorStore();
        var embedding = new RecordingEmbedding();
        var pipeline = CreatePipeline(vectors, embedding, new CharacterTextSplitter(1, 0, null));
        pipeline.Options.EmbeddingBatchSize = batchSize;

        await pipeline.IndexDocumentAsync(Document("large-batch", "ABCDE"));

        CollectionAssert.AreEqual(new[] { 5 }, embedding.Batches.Select(b => b.Length).ToArray());
        Assert.AreEqual(5, (await vectors.ListAllRecordsAsync()).Count);
    }

    private static RagDocument Document(string id, string content) => new(id, content, id + ".txt");
    private static bool IsCompanyA(VectorRecord record) => record.Id.StartsWith("company-a_chunk_", StringComparison.Ordinal);
    private static string[] Snapshot(IEnumerable<VectorRecord> records) => records.OrderBy(r => r.Id, StringComparer.Ordinal)
        .Select(r => $"{r.Id}|{r.Metadata["document_id"]}|{r.Content}").ToArray();
    private static RagPipeline CreatePipeline(IVectorStore vectors, IEmbeddingProvider? embedding = null, ITextSplitter? splitter = null) =>
        new(embedding ?? new RecordingEmbedding(), vectors, splitter ?? new CharacterTextSplitter(8, 0, null), new DefaultContextBuilder());
    private static ITextSplitter CreateSplitter(string kind) => kind switch
    {
        "character" => new CharacterTextSplitter(8, 0, null),
        "recursive" => new RecursiveTextSplitter(8, 0),
        "markdown" => new MarkdownTextSplitter(8),
        "token" => new TokenTextSplitter(2, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed class RecordingEmbedding : IEmbeddingProvider
    {
        public int Dimensions => 1;
        public List<string[]> Batches { get; } = new();
        public Func<int, Task>? BeforeReturnAsync { get; set; }
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new[] { (float)text[0] });
        public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = texts.ToArray();
            Batches.Add(batch);
            if (Batches.Count > 100) throw new AssertFailedException("Unexpected repeating embedding loop.");
            if (BeforeReturnAsync != null) await BeforeReturnAsync(Batches.Count);
            return batch.Select(text => new[] { (float)text[0] }).ToArray();
        }
    }

    private sealed class DelegateSplitter(Func<RagDocument, IReadOnlyList<RagChunk>> split) : ITextSplitter
    {
        public IReadOnlyList<RagChunk> Split(RagDocument document) => split(document);
    }

    private sealed class DelegateLoader(Func<string, CancellationToken, Task<IReadOnlyList<DoclingDocument>>> load) : IDocumentLoader
    {
        public Task<IReadOnlyList<DoclingDocument>> LoadAsync(string source, CancellationToken cancellationToken = default) => load(source, cancellationToken);
    }
}
