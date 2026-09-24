using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Loaders;
using Mythosia.AI.Rag.Splitters;
using Mythosia.Documents;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class EmptyDocumentUpdateTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow(" \t\r\n   ")]
    public async Task IndexDocumentAsync_EmptyUpdate_RemovesOnlyItsChunksWithoutEmbedding(string emptyContent)
    {
        using var vectors = new InMemoryVectorStore();
        var embedding = new CountingEmbeddingProvider();
        var pipeline = CreatePipeline(vectors, embedding);
        await pipeline.IndexDocumentAsync(Document("policy", "abcdefghijkl"));
        await pipeline.IndexDocumentAsync(Document("other", "KEEP"));
        var before = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(3, before.Count(record => DocumentId(record) == "policy"));
        var embeddingCalls = embedding.Calls;

        await pipeline.IndexDocumentAsync(Document("policy", emptyContent));
        await pipeline.IndexDocumentAsync(Document("policy", emptyContent));

        var remaining = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(1, remaining.Count, "Both initial and repeated empty updates must remove all old chunks of the target only.");
        Assert.AreEqual("other", DocumentId(remaining[0]));
        Assert.AreEqual("KEEP", remaining[0].Content);
        Assert.AreEqual(embeddingCalls, embedding.Calls, "An empty replacement must not request any embeddings.");

        await pipeline.IndexDocumentAsync(Document("policy", "NEW"));
        var restored = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(2, restored.Count);
        Assert.AreEqual("NEW", restored.Single(record => DocumentId(record) == "policy").Content,
            "The same explicit document ID must remain usable after an empty update.");
    }

    [TestMethod]
    public async Task IndexDocumentsAsync_EmptyDocument_DeletesItsOldChunksAndContinuesIndexing()
    {
        using var vectors = new InMemoryVectorStore();
        var pipeline = CreatePipeline(vectors);
        await pipeline.IndexDocumentsAsync(new[] { Document("policy", "OLD"), Document("other", "KEEP") });

        await pipeline.IndexDocumentsAsync(new[] { Document("policy", ""), Document("new", "NEW") });

        var records = await vectors.ListAllRecordsAsync();
        CollectionAssert.AreEquivalent(new[] { "other", "new" }, records.Select(DocumentId).ToArray());
        CollectionAssert.AreEquivalent(new[] { "KEEP", "NEW" }, records.Select(record => record.Content).ToArray());
    }

    [TestMethod]
    public async Task IndexDocumentAsync_PerSourceSplitterProducesNoChunks_ReplacesPreviousVersion()
    {
        using var vectors = new InMemoryVectorStore();
        var embedding = new CountingEmbeddingProvider();
        var pipeline = CreatePipeline(vectors, embedding);
        await pipeline.IndexDocumentAsync(Document("policy", "OLD"));
        var embeddingCalls = embedding.Calls;

        await pipeline.IndexDocumentAsync(Document("policy", "This content is excluded by the splitter."),
            new DelegateSplitter(_ => Array.Empty<RagChunk>()));

        Assert.AreEqual(0, (await vectors.ListAllRecordsAsync()).Count);
        Assert.AreEqual(embeddingCalls, embedding.Calls);
    }

    [TestMethod]
    public async Task IndexAsync_LoaderReturnsEmptyDocument_RemovesItsPreviousVersion()
    {
        using var vectors = new InMemoryVectorStore();
        var pipeline = CreatePipeline(vectors);
        await pipeline.IndexDocumentAsync(Document("policy", "OLD"));
        await pipeline.IndexDocumentAsync(Document("other", "KEEP"));
        var loader = new DelegateLoader((_, _) => Task.FromResult<IReadOnlyList<DoclingDocument>>(
            new[] { new DoclingDocument { Name = "Policy", Source = "policy", RawContent = "" } }));

        await pipeline.IndexAsync(loader, "logical-source");

        var remaining = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("other", DocumentId(remaining[0]));
    }

    [TestMethod]
    public async Task IndexAsync_EmptyDocumentList_DoesNotInferAnyDeletion()
    {
        using var vectors = new InMemoryVectorStore();
        var pipeline = CreatePipeline(vectors);
        await pipeline.IndexDocumentAsync(Document("policy", "OLD"));
        var before = Snapshot(await vectors.ListAllRecordsAsync());
        var loader = new DelegateLoader((_, _) => Task.FromResult<IReadOnlyList<DoclingDocument>>(
            Array.Empty<DoclingDocument>()));

        await pipeline.IndexAsync(loader, "policy");
        await pipeline.IndexDocumentsAsync(Array.Empty<RagDocument>());

        CollectionAssert.AreEqual(before, Snapshot(await vectors.ListAllRecordsAsync()),
            "No returned documents supplies no document identity to delete.");
    }

    [TestMethod]
    public async Task BuildAsync_FileBecomesEmpty_RemovesItsChunksAndPreservesOtherFiles()
    {
        using var temporary = new TemporaryDocuments();
        var policyPath = temporary.Write("policy.txt", "abcdefghijkl");
        temporary.Write("other.txt", "KEEP");
        using var vectors = new InMemoryVectorStore();
        var embedding = new CountingEmbeddingProvider();
        await BuildDirectoryAsync(temporary.Root, vectors, embedding);
        var original = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(4, original.Count);
        var policyId = DocumentId(original.First(record => record.Metadata["filename"] == "policy.txt"));
        var otherId = DocumentId(original.Single(record => record.Metadata["filename"] == "other.txt"));
        var embeddingCalls = embedding.Calls;
        File.WriteAllText(policyPath, "");

        await RagStore.BuildAsync(builder => builder.AddDocument(policyPath)
            .UseStore(vectors).UseEmbedding(embedding).WithChunkSize(4).WithChunkOverlap(0));

        var current = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(1, current.Count);
        Assert.AreEqual(otherId, DocumentId(current[0]));
        Assert.IsFalse(current.Any(record => DocumentId(record) == policyId));
        Assert.AreEqual(embeddingCalls, embedding.Calls);
    }

    [TestMethod]
    public async Task IndexAsync_FileReadFailure_PreservesPreviousVersion()
    {
        using var temporary = new TemporaryDocuments();
        var file = temporary.Write("policy.txt", "OLD");
        using var vectors = new InMemoryVectorStore();
        var pipeline = CreatePipeline(vectors);
        var loader = new PlainTextDocumentLoader();
        await pipeline.IndexAsync(loader, file);
        var before = Snapshot(await vectors.ListAllRecordsAsync());
        File.Delete(file);

        await Assert.ThrowsAsync<FileNotFoundException>(() => pipeline.IndexAsync(loader, file));

        CollectionAssert.AreEqual(before, Snapshot(await vectors.ListAllRecordsAsync()));
    }

    [TestMethod]
    public async Task IndexAsync_ParserThrows_PreservesPreviousVersion()
    {
        using var vectors = new InMemoryVectorStore();
        var pipeline = CreatePipeline(vectors);
        await pipeline.IndexDocumentAsync(Document("policy", "OLD"));
        var before = Snapshot(await vectors.ListAllRecordsAsync());
        var loader = new DelegateLoader((_, _) =>
            Task.FromException<IReadOnlyList<DoclingDocument>>(new FormatException("Source cannot be parsed.")));

        await Assert.ThrowsAsync<FormatException>(() => pipeline.IndexAsync(loader, "policy"));

        CollectionAssert.AreEqual(before, Snapshot(await vectors.ListAllRecordsAsync()));
    }

    [TestMethod]
    public async Task IndexDocumentAsync_SplitterThrows_PreservesPreviousVersion()
    {
        using var vectors = new InMemoryVectorStore();
        var pipeline = CreatePipeline(vectors);
        await pipeline.IndexDocumentAsync(Document("policy", "OLD"));
        var before = Snapshot(await vectors.ListAllRecordsAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.IndexDocumentAsync(
            Document("policy", ""), new DelegateSplitter(_ => throw new InvalidOperationException("Split failed."))));

        CollectionAssert.AreEqual(before, Snapshot(await vectors.ListAllRecordsAsync()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyUpdate_AlreadyCanceled_PreservesPreviousVersion(bool collectionEntryPoint)
    {
        using var vectors = new MutationObservingStore();
        var pipeline = CreatePipeline(vectors);
        await pipeline.IndexDocumentAsync(Document("policy", "OLD"));
        var before = Snapshot(await vectors.RecordsAsync());
        var mutationCalls = vectors.MutationCalls;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => collectionEntryPoint
            ? pipeline.IndexDocumentsAsync(new[] { Document("policy", "") }, cancellation.Token)
            : pipeline.IndexDocumentAsync(Document("policy", ""), cancellation.Token));

        CollectionAssert.AreEqual(before, Snapshot(await vectors.RecordsAsync()));
        Assert.AreEqual(mutationCalls, vectors.MutationCalls, "A canceled update must not reach persistence.");
    }

    [TestMethod]
    public async Task EmptyUpdate_CanceledDuringSplit_DoesNotReachPersistence()
    {
        using var vectors = new MutationObservingStore();
        var pipeline = CreatePipeline(vectors);
        await pipeline.IndexDocumentAsync(Document("policy", "OLD"));
        var before = Snapshot(await vectors.RecordsAsync());
        var mutationCalls = vectors.MutationCalls;
        using var cancellation = new CancellationTokenSource();
        var splitter = new DelegateSplitter(_ =>
        {
            cancellation.Cancel();
            return Array.Empty<RagChunk>();
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.IndexDocumentAsync(Document("policy", ""), splitter, cancellation.Token));

        CollectionAssert.AreEqual(before, Snapshot(await vectors.RecordsAsync()));
        Assert.AreEqual(mutationCalls, vectors.MutationCalls,
            "Cancellation that occurs while splitting must be observed before deleting any old records.");
    }

    [TestMethod]
    public async Task EmptyUpdate_StoreObservesCancellation_ReceivesCallerTokenAndPreservesPreviousVersion()
    {
        using var vectors = new MutationObservingStore();
        var pipeline = CreatePipeline(vectors);
        await pipeline.IndexDocumentAsync(Document("policy", "OLD"));
        var before = Snapshot(await vectors.RecordsAsync());
        using var cancellation = new CancellationTokenSource();
        vectors.BeforeMutation = token =>
        {
            Assert.AreEqual(cancellation.Token, token, "Empty updates must forward the caller token to persistence.");
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
        };

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.IndexDocumentAsync(Document("policy", ""), cancellation.Token));

        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        CollectionAssert.AreEqual(before, Snapshot(await vectors.RecordsAsync()));
    }

    [TestMethod]
    public async Task IndexDocumentAsync_CanceledAfterEmbedding_DoesNotReplacePreviousVersion()
    {
        using var vectors = new MutationObservingStore();
        var embedding = new CountingEmbeddingProvider();
        var pipeline = CreatePipeline(vectors, embedding);
        await pipeline.IndexDocumentAsync(Document("policy", "OLD"));
        var before = Snapshot(await vectors.RecordsAsync());
        var mutationCalls = vectors.MutationCalls;
        using var cancellation = new CancellationTokenSource();
        embedding.AfterBatch = cancellation.Cancel;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.IndexDocumentAsync(Document("policy", "NEW"), cancellation.Token));

        CollectionAssert.AreEqual(before, Snapshot(await vectors.RecordsAsync()));
        Assert.AreEqual(mutationCalls, vectors.MutationCalls,
            "A provider returning successfully after cancellation must not allow the update to reach persistence.");
    }

    [TestMethod]
    public async Task EmptyUpdate_RemovesOldTextFromVectorAndHybridSearch()
    {
        using var vectors = new InMemoryVectorStore();
        var embedding = new LocalEmbeddingProvider(32);
        var pipeline = new RagPipeline(embedding, vectors,
            new CharacterTextSplitter(1000, 0, null), new DefaultContextBuilder());
        await pipeline.IndexDocumentAsync(Document("policy", "Refunds are available for fourteen days."));
        await pipeline.IndexDocumentAsync(Document("other", "Shipping takes three business days."));
        var queryVector = await embedding.GetEmbeddingAsync("Refunds");
        var original = await vectors.HybridSearchAsync(queryVector, "Refunds", topK: 10);
        Assert.IsTrue(original.Any(result => DocumentId(result.Record) == "policy"));

        await pipeline.IndexDocumentAsync(Document("policy", ""));

        var vectorResults = await vectors.SearchAsync(queryVector, topK: 10);
        var hybridResults = await vectors.HybridSearchAsync(queryVector, "Refunds", topK: 10);
        Assert.AreEqual(1, vectorResults.Count);
        Assert.AreEqual(1, hybridResults.Count);
        Assert.AreEqual("other", DocumentId(vectorResults[0].Record));
        Assert.AreEqual("other", DocumentId(hybridResults[0].Record),
            "The keyword index must not reintroduce the retired policy into hybrid search results.");
    }

    [TestMethod]
    public async Task BuildAsync_CustomPersistenceCallback_EmptyDocumentDoesNotMutateDefaultStore()
    {
        using var vectors = new InMemoryVectorStore();
        var embedding = new CountingEmbeddingProvider();
        await CreatePipeline(vectors, embedding).IndexDocumentAsync(Document("policy", "OLD"));
        var before = Snapshot(await vectors.ListAllRecordsAsync());
        var embeddingCalls = embedding.Calls;
        var callbackCalls = 0;

        await RagStore.BuildAsync(builder => builder.AddText("", id: "policy")
            .UseStore(vectors).UseEmbedding(embedding), onDocumentEmbedded: records =>
            {
                callbackCalls++;
                return Task.CompletedTask;
            });

        CollectionAssert.AreEqual(before, Snapshot(await vectors.ListAllRecordsAsync()),
            "The pipeline must not delete from its store when the caller owns persistence.");
        Assert.AreEqual(0, callbackCalls, "An empty embedding callback cannot identify the document; preserve its existing contract.");
        Assert.AreEqual(embeddingCalls, embedding.Calls);
    }

    private static RagDocument Document(string id, string content) =>
        new() { Id = id, Content = content, Source = "shared-logical-source" };

    private static string DocumentId(VectorRecord record) => record.Metadata["document_id"];

    private static string[] Snapshot(IReadOnlyList<VectorRecord> records) => records
        .OrderBy(record => record.Id, StringComparer.Ordinal)
        .Select(record => $"{record.Id}|{DocumentId(record)}|{record.Content}").ToArray();

    private static RagPipeline CreatePipeline(IVectorStore vectors, IEmbeddingProvider? embedding = null) =>
        new(embedding ?? new LocalEmbeddingProvider(32), vectors,
            new CharacterTextSplitter(4, 0, null), new DefaultContextBuilder());

    private static Task<RagStore> BuildDirectoryAsync(string directory, IVectorStore vectors, IEmbeddingProvider embedding) =>
        RagStore.BuildAsync(builder => builder.AddDocuments(directory).UseStore(vectors).UseEmbedding(embedding)
            .WithChunkSize(4).WithChunkOverlap(0));

    private sealed class CountingEmbeddingProvider : IEmbeddingProvider
    {
        private readonly LocalEmbeddingProvider _inner = new(32);
        public int Dimensions => _inner.Dimensions;
        public int Calls { get; private set; }
        public Action? AfterBatch { get; set; }

        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            return _inner.GetEmbeddingAsync(text, cancellationToken);
        }

        public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            Calls++;
            var result = await _inner.GetEmbeddingsAsync(texts, cancellationToken);
            AfterBatch?.Invoke();
            return result;
        }
    }

    private sealed class DelegateSplitter(Func<RagDocument, IReadOnlyList<RagChunk>> split) : ITextSplitter
    {
        public IReadOnlyList<RagChunk> Split(RagDocument document) => split(document);
    }

    private sealed class DelegateLoader(Func<string, CancellationToken, Task<IReadOnlyList<DoclingDocument>>> load) : IDocumentLoader
    {
        public Task<IReadOnlyList<DoclingDocument>> LoadAsync(string source, CancellationToken cancellationToken = default) =>
            load(source, cancellationToken);
    }

    private sealed class MutationObservingStore : IVectorStore, IDisposable
    {
        private readonly InMemoryVectorStore _inner = new();
        public int MutationCalls { get; private set; }
        public Action<CancellationToken>? BeforeMutation { get; set; }

        public Task<IReadOnlyList<VectorRecord>> RecordsAsync() => _inner.ListAllRecordsAsync();
        public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default) =>
            _inner.UpsertAsync(record, cancellationToken);
        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default) =>
            _inner.UpsertBatchAsync(records, cancellationToken);
        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 5,
            VectorFilter? filter = null, CancellationToken cancellationToken = default) =>
            _inner.SearchAsync(queryVector, topK, filter, cancellationToken);
        public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) =>
            _inner.GetAsync(id, filter, cancellationToken);
        public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(id, filter, cancellationToken);

        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            BeforeMutation?.Invoke(cancellationToken);
            return _inner.DeleteByFilterAsync(filter, cancellationToken);
        }

        public Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
            CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            BeforeMutation?.Invoke(cancellationToken);
            return ((IVectorStore)_inner).ReplaceByFilterAsync(filter, records, cancellationToken);
        }

        public void Dispose() => _inner.Dispose();
    }

    private sealed class TemporaryDocuments : IDisposable
    {
        private readonly string _parent = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        private readonly string _name = "mythosia-empty-document-" + Guid.NewGuid().ToString("N");
        public string Root { get; }

        public TemporaryDocuments()
        {
            Root = Path.GetFullPath(Path.Combine(_parent, _name));
            Directory.CreateDirectory(Root);
        }

        public string Write(string name, string content)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            var target = Path.GetFullPath(Root);
            if (!target.StartsWith(_parent, StringComparison.Ordinal)
                || !string.Equals(Path.GetFileName(target), _name, StringComparison.Ordinal))
                throw new InvalidOperationException("Empty-document test cleanup escaped its temporary directory.");
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
        }
    }
}
