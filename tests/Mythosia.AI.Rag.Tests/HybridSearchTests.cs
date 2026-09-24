using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Retrieval;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

#region 1. Bm25Tokenizer Tests

[TestClass]
public class Bm25TokenizerTests
{
    [TestMethod]
    public void Tokenize_BasicEnglish_NormalizesPunctuationAndPreservesCoreTokens()
    {
        var tokens = Bm25Tokenizer.Tokenize("The quick brown fox jumps over the lazy dog!");

        Assert.IsTrue(tokens.Count > 0);
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "quick");
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "brown");
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "fox");
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "jumps");
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "lazy");
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "dog");
        CollectionAssert.DoesNotContain((System.Collections.ICollection)tokens, "dog!");
    }

    [TestMethod]
    public void Tokenize_Korean_SplitsByWhitespace()
    {
        var tokens = Bm25Tokenizer.Tokenize("환불 정책은 14일 이내에 가능합니다");

        Assert.IsTrue(tokens.Count > 0);
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "환불");
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "정책은");
    }

    [TestMethod]
    public void Tokenize_EmptyOrNull_ReturnsEmpty()
    {
        Assert.AreEqual(0, Bm25Tokenizer.Tokenize("").Count);
        Assert.AreEqual(0, Bm25Tokenizer.Tokenize("   ").Count);
        Assert.AreEqual(0, Bm25Tokenizer.Tokenize(null!).Count);
    }

    [TestMethod]
    public void Tokenize_SingleCharTokens_Filtered()
    {
        // Single-character tokens (length < 2) should be filtered out
        var tokens = Bm25Tokenizer.Tokenize("I am a test");
        CollectionAssert.DoesNotContain((System.Collections.ICollection)tokens, "a");
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "test");
    }

    [TestMethod]
    public void ComputeTermFrequencies_CountsCorrectly()
    {
        var tokens = Bm25Tokenizer.Tokenize("apple banana apple cherry apple banana");
        var tf = Bm25Tokenizer.ComputeTermFrequencies(tokens);

        Assert.AreEqual(3, tf["apple"]);
        Assert.AreEqual(2, tf["banana"]);
        Assert.AreEqual(1, tf["cherry"]);
    }

    [TestMethod]
    public void Tokenize_PunctuationReplacedWithSpace()
    {
        var tokens = Bm25Tokenizer.Tokenize("hello,world;foo-bar");
        // Punctuation should become spaces, so we get separate tokens
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "hello");
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "world");
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "foo");
        CollectionAssert.Contains((System.Collections.ICollection)tokens, "bar");
    }
}

#endregion

#region 2. Bm25Index Tests

[TestClass]
public class Bm25IndexTests
{
    [TestMethod]
    public void Index_And_Search_ReturnsRelevantDocuments()
    {
        var index = new Bm25Index();
        index.Index("doc1", "The cat sat on the mat");
        index.Index("doc2", "The dog played in the park");
        index.Index("doc3", "A quick brown fox jumps");

        var results = index.Search("cat mat", 3);

        Assert.IsTrue(results.Count > 0);
        Assert.AreEqual("doc1", results[0].Id, "doc1 should rank first for 'cat mat'");
    }

    [TestMethod]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var index = new Bm25Index();
        index.Index("doc1", "apple banana cherry");

        var results = index.Search("xyzzy nonsense", 5);

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var index = new Bm25Index();
        index.Index("doc1", "hello world");

        var results = index.Search("", 5);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        var index = new Bm25Index();
        var results = index.Search("hello", 5);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Index_ReplacesExistingDocument()
    {
        var index = new Bm25Index();
        index.Index("doc1", "apple banana");
        index.Index("doc1", "cherry grape"); // Replace

        Assert.AreEqual(1, index.DocumentCount);

        var results = index.Search("cherry", 5);
        Assert.IsTrue(results.Count > 0);
        Assert.AreEqual("doc1", results[0].Id);
        Assert.IsTrue(results[0].Content.Contains("cherry"));

        // Original content should not match
        var oldResults = index.Search("apple", 5);
        Assert.AreEqual(0, oldResults.Count);
    }

    [TestMethod]
    public void Remove_DeletesDocument()
    {
        var index = new Bm25Index();
        index.Index("doc1", "apple banana");
        index.Index("doc2", "cherry grape");

        Assert.AreEqual(2, index.DocumentCount);

        index.Remove("doc1");

        Assert.AreEqual(1, index.DocumentCount);
        var results = index.Search("apple", 5);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Search_TopK_LimitsResults()
    {
        var index = new Bm25Index();
        for (int i = 0; i < 20; i++)
            index.Index($"doc{i}", $"common keyword document number {i}");

        var results = index.Search("common keyword", 5);
        Assert.IsTrue(results.Count <= 5);
    }

    [TestMethod]
    public void Search_ScoresDescending()
    {
        var index = new Bm25Index();
        index.Index("doc1", "machine learning deep learning neural network");
        index.Index("doc2", "machine learning basics introduction");
        index.Index("doc3", "cooking recipe pasta");

        var results = index.Search("machine learning", 3);

        Assert.IsTrue(results.Count >= 2);
        for (int i = 1; i < results.Count; i++)
        {
            Assert.IsTrue(results[i - 1].Score >= results[i].Score,
                $"Results should be sorted descending by score: [{i - 1}]={results[i - 1].Score} vs [{i}]={results[i].Score}");
        }
    }

    [TestMethod]
    public void Search_Korean_FindsRelevantDocuments()
    {
        var index = new Bm25Index();
        index.Index("refund", "환불은 구매일로부터 14일 이내에 요청 가능합니다");
        index.Index("shipping", "배송은 주문 후 2일에서 3일 이내 도착합니다");
        index.Index("exchange", "교환은 제품 수령 후 7일 이내 가능합니다");

        var results = index.Search("환불 요청", 3);

        Assert.IsTrue(results.Count > 0);
        Assert.AreEqual("refund", results[0].Id);
    }
}

#endregion

#region 3. Production Hybrid and Vector Retrieval Tests

[TestClass]
public class HybridRetrieverPipelineTests
{
    [TestMethod]
    public async Task Retrieve_TextCapableStoreWithoutNativeHybrid_UsesSharedFusion()
    {
        using var memory = new InMemoryVectorStore();
        var store = new TextOnlyCapabilityStore(memory);
        await store.UpsertAsync(new VectorRecord("both", new[] { 1f, 0f }, "refund policy"));
        await store.UpsertAsync(new VectorRecord("dense", new[] { 1f, 0f }, "different topic"));
        var rag = await RagStore.BuildAsync(b => b.UseStore(store).UseEmbedding(new FixedEmbedding())
            .UseHybridSearch(new HybridSearchOptions { VectorWeight = .5f }));

        var result = await rag.QueryAsync("refund");

        Assert.AreEqual("both", result.References[0].Record.Id);
        Assert.AreEqual(1, store.TextCalls);
        Assert.AreEqual(1.0, result.References[0].Score, 1e-10);
    }

    [TestMethod]
    public async Task Retrieve_ConfigurableStore_ForwardsWeightAndQuery()
    {
        var store = new MockHybridStore();
        var rag = await RagStore.BuildAsync(b => b.UseStore(store).UseEmbedding(new FixedEmbedding())
            .WithTopK(5).UseHybridSearch(.7f));

        await rag.QueryAsync("test query");

        Assert.IsTrue(store.HybridSearchCalled);
        Assert.AreEqual("test query", store.LastQuery);
        Assert.AreEqual(.7f, store.LastOptions!.VectorWeight);
        Assert.AreEqual(5, store.LastTopK);
    }

    [TestMethod]
    public async Task Hybrid_DocumentMatchingBothLegs_RanksFirst()
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertBatchAsync(new[]
        {
            new VectorRecord("both", new[] { 1f, 0f }, "hybrid search"),
            new VectorRecord("dense", new[] { 1f, 0f }, "unrelated content"),
            new VectorRecord("text", new[] { 0f, 1f }, "hybrid search with extra words")
        });
        var rag = await RagStore.BuildAsync(b => b.UseStore(store).UseEmbedding(new FixedEmbedding()).UseHybridSearch());
        var result = await rag.QueryAsync("hybrid search");
        Assert.AreEqual("both", result.References[0].Record.Id);
    }

    [TestMethod]
    public async Task Hybrid_ResultsAreDescendingAndBounded()
    {
        using var store = new InMemoryVectorStore();
        for (var i = 0; i < 10; i++)
            await store.UpsertAsync(new VectorRecord("doc-" + i, new[] { 1f, (float)i }, "keyword search " + i));
        var rag = await RagStore.BuildAsync(b => b.UseStore(store).UseEmbedding(new FixedEmbedding()).WithTopK(5).UseHybridSearch());
        var result = await rag.QueryAsync("keyword search");
        Assert.AreEqual(5, result.References.Count);
        Assert.IsTrue(result.References.All(item => item.Score >= 0 && item.Score <= 1));
        for (var i = 1; i < result.References.Count; i++)
            Assert.IsTrue(result.References[i - 1].Score >= result.References[i].Score);
    }

    [TestMethod]
    public async Task Hybrid_VectorWeightOne_ActuallyOverridesKeywordPreference()
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(new VectorRecord("dense", new[] { 1f, 0f }, "unrelated text"));
        await store.UpsertAsync(new VectorRecord("text", new[] { 0f, 1f }, "specific keyword"));
        var rag = await RagStore.BuildAsync(b => b.UseStore(store).UseEmbedding(new FixedEmbedding()).UseHybridSearch(1));
        var result = await rag.QueryAsync("specific keyword");
        Assert.AreEqual("dense", result.References[0].Record.Id);
    }

    [TestMethod]
    public async Task VectorMode_UsesEmbeddingAndIgnoresLexicalOverride()
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(new VectorRecord("dense", new[] { 1f, 0f }, "unrelated text"));
        await store.UpsertAsync(new VectorRecord("text", new[] { 0f, 1f }, "specific keyword"));
        var rag = await RagStore.BuildAsync(b => b.UseStore(store).UseEmbedding(new FixedEmbedding()).UseVectorSearch());
        var first = await ((RagPipeline)rag.Pipeline).ProcessAsync("semantic", "specific keyword", null);
        var second = await ((RagPipeline)rag.Pipeline).ProcessAsync("semantic", "unrelated", null);
        Assert.AreEqual("dense", first.References[0].Record.Id);
        Assert.AreEqual(first.References[0].Score, second.References[0].Score);
    }

    private sealed class FixedEmbedding : IEmbeddingProvider
    {
        public int Dimensions => 2;
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new[] { 1f, 0f });
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f, 0f }).ToList());
    }

    // Deliberately exposes text+vector capabilities without configurable native hybrid,
    // exercising the real application-level fusion path and its persisted text index.
    private sealed class TextOnlyCapabilityStore : IVectorStore, ITextSearchStore
    {
        private readonly InMemoryVectorStore _inner;
        public int TextCalls;
        public TextOnlyCapabilityStore(InMemoryVectorStore inner) => _inner = inner;
        public Task<IReadOnlyList<VectorSearchResult>> TextSearchAsync(string query, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        { TextCalls++; return _inner.TextSearchAsync(query, topK, filter, cancellationToken); }
        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
            => _inner.SearchAsync(queryVector, topK, filter, cancellationToken);
        public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default) => _inner.UpsertAsync(record, cancellationToken);
        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default) => _inner.UpsertBatchAsync(records, cancellationToken);
        public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) => _inner.GetAsync(id, filter, cancellationToken);
        public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) => _inner.DeleteAsync(id, filter, cancellationToken);
        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default) => _inner.DeleteByFilterAsync(filter, cancellationToken);
    }
}

#endregion

#region 5. RagPipeline Reranker Integration Tests

[TestClass]
public class RagPipelineRerankerTests
{
    /// <summary>
    /// Pipeline without reranker should work as before (v3.x backward compat).
    /// </summary>
    [TestMethod]
    public async Task Pipeline_WithoutReranker_WorksAsV3()
    {
        var embedding = new LocalEmbeddingProvider(dimensions: 128);
        var store = new InMemoryVectorStore();
        var splitter = new CharacterTextSplitter(500, 50);
        var contextBuilder = new DefaultContextBuilder();

        // No retrieval strategy, no reranker → defaults to request-based vector retrieval
        var pipeline = new RagPipeline(embedding, store, splitter, contextBuilder,
            new RagPipelineOptions { DefaultQuery = new RagQueryOptions { FinalFilter = new RagFilter { TopK = 3 } } });

        await pipeline.IndexDocumentAsync(new RagDocument
        {
            Id = "doc1",
            Content = "환불은 14일 이내 가능합니다.",
            Source = "policy.txt"
        });

        var result = await pipeline.QueryAsync("환불 정책");

        Assert.IsNotNull(result);
        Assert.IsTrue(result.SearchResults.Count > 0);
    }

    /// <summary>
    /// Pipeline with a mock reranker should reorder results.
    /// </summary>
    [TestMethod]
    public async Task Pipeline_WithReranker_ReordersResults()
    {
        var embedding = new LocalEmbeddingProvider(dimensions: 128);
        var store = new InMemoryVectorStore();
        var splitter = new CharacterTextSplitter(500, 50);
        var contextBuilder = new DefaultContextBuilder();

        // Use a mock reranker that reverses the result order
        var reranker = new ReversingReranker();

        var pipeline = new RagPipeline(
            embedding, store, splitter, contextBuilder,
            retrievalStrategy: null, reranker: reranker,
            options: new RagPipelineOptions { DefaultQuery = new RagQueryOptions { FinalFilter = new RagFilter { TopK = 3 } } });

        await pipeline.IndexDocumentAsync(new RagDocument
        {
            Id = "doc1", Content = "첫 번째 문서 내용입니다.", Source = "a.txt"
        });
        await pipeline.IndexDocumentAsync(new RagDocument
        {
            Id = "doc2", Content = "두 번째 문서 내용입니다.", Source = "b.txt"
        });

        var result = await pipeline.QueryAsync("문서");

        Assert.IsTrue(reranker.WasCalled, "Reranker should have been called");
        Assert.IsNotNull(result);
    }

    /// <summary>
    /// Pipeline with the hybrid retriever should combine BM25 and vector search.
    /// </summary>
    [TestMethod]
    public async Task Pipeline_WithHybridStrategy_CombinesBm25AndVector()
    {
        var embedding = new LocalEmbeddingProvider(dimensions: 128);
        var store = new InMemoryVectorStore();
        var splitter = new CharacterTextSplitter(500, 50);
        var contextBuilder = new DefaultContextBuilder();
        var pipeline = new RagPipeline(
            embedding, store, splitter, contextBuilder,
            retrievalStrategy: null, reranker: null,
            options: new RagPipelineOptions { DefaultQuery = new RagQueryOptions { FinalFilter = new RagFilter { TopK = 3 } } });
        pipeline.SetRetriever(new RagRetrievers.Hybrid(embedding, store, new HybridSearchOptions()));

        // Index documents
        var doc = new RagDocument
        {
            Id = "hybrid-doc", Content = "하이브리드 검색 테스트 문서입니다.", Source = "test.txt"
        };
        await pipeline.IndexDocumentAsync(doc);

        var result = await pipeline.QueryAsync("하이브리드 검색");

        Assert.IsNotNull(result);
        Assert.IsTrue(result.SearchResults.Count > 0);
    }
}

#endregion

#region 6. RagBuilder API Tests

[TestClass]
public class RagBuilderHybridApiTests
{
    /// <summary>
    /// UseHybridSearch() should build successfully and return results.
    /// </summary>
    [TestMethod]
    public async Task UseHybridSearch_BuildsAndQueries()
    {
        var ragStore = await RagStore.BuildAsync(config => config
            .AddText("환불은 14일 이내 가능합니다.", id: "refund")
            .AddText("배송은 2-3일 소요됩니다.", id: "shipping")
            .AddText("교환은 7일 이내 가능합니다.", id: "exchange")
            .UseLocalEmbedding(128)
            .UseInMemoryStore()
            .WithTopK(3)
            .UseHybridSearch()
        );

        var result = await ragStore.QueryAsync("환불 정책");

        Assert.IsNotNull(result);
        Assert.IsTrue(result.References.Count > 0, "Hybrid search should return results");
    }

    /// <summary>
    /// UseHybridSearch(vectorWeight: 0.7) should work with custom weight.
    /// </summary>
    [TestMethod]
    public async Task UseHybridSearch_CustomWeight_BuildsSuccessfully()
    {
        var ragStore = await RagStore.BuildAsync(config => config
            .AddText("테스트 문서입니다.", id: "test")
            .UseLocalEmbedding(128)
            .UseInMemoryStore()
            .UseHybridSearch(vectorWeight: 0.7f)
        );

        var result = await ragStore.QueryAsync("테스트");

        Assert.IsNotNull(result);
        Assert.IsTrue(result.References.Count > 0);
    }

    /// <summary>
    /// Without UseHybridSearch(), should behave identically to v3.x (pure vector).
    /// </summary>
    [TestMethod]
    public async Task WithoutHybrid_WorksIdenticalToV3()
    {
        var ragStore = await RagStore.BuildAsync(config => config
            .AddText("기본 벡터 검색 테스트", id: "default")
            .UseLocalEmbedding(128)
            .UseInMemoryStore()
            .WithTopK(3)
        );

        var result = await ragStore.QueryAsync("벡터 검색");

        Assert.IsNotNull(result);
        Assert.IsTrue(result.References.Count > 0);
    }

    /// <summary>
    /// WithReranker() should integrate reranker into the pipeline.
    /// </summary>
    [TestMethod]
    public async Task WithReranker_IntegratesReranker()
    {
        var reranker = new ReversingReranker();

        var ragStore = await RagStore.BuildAsync(config => config
            .AddText("첫 번째 문서", id: "doc1")
            .AddText("두 번째 문서", id: "doc2")
            .UseLocalEmbedding(128)
            .UseInMemoryStore()
            .WithTopK(2)
            .WithReranker(reranker)
        );

        var result = await ragStore.QueryAsync("문서");

        Assert.IsTrue(reranker.WasCalled, "Reranker should have been invoked");
        Assert.IsNotNull(result);
    }

    /// <summary>
    /// UseHybridSearch() + WithReranker() combined should work.
    /// </summary>
    [TestMethod]
    public async Task UseHybridSearch_WithReranker_CombinedWorks()
    {
        var reranker = new ReversingReranker();

        var ragStore = await RagStore.BuildAsync(config => config
            .AddText("환불 정책 문서", id: "refund")
            .AddText("배송 정책 문서", id: "shipping")
            .UseLocalEmbedding(128)
            .UseInMemoryStore()
            .WithTopK(2)
            .UseHybridSearch(vectorWeight: 0.6f)
            .WithReranker(reranker)
        );

        var result = await ragStore.QueryAsync("환불");

        Assert.IsTrue(reranker.WasCalled, "Reranker should be called after hybrid search");
        Assert.IsNotNull(result);
        Assert.IsTrue(result.References.Count > 0);
    }

    /// <summary>
    /// UseVectorSearch() should reset to pure vector mode after UseHybridSearch() was called.
    /// </summary>
    [TestMethod]
    public async Task UseVectorSearch_ResetsToVectorMode()
    {
        var ragStore = await RagStore.BuildAsync(config => config
            .AddText("테스트 문서", id: "test")
            .UseLocalEmbedding(128)
            .UseInMemoryStore()
            .UseHybridSearch()
            .UseVectorSearch() // Reset to pure vector
        );

        var result = await ragStore.QueryAsync("테스트");

        Assert.IsNotNull(result);
        Assert.IsTrue(result.References.Count > 0);
    }
}

#endregion

#region Test Helpers

/// <summary>
/// Mock native-hybrid store for testing strategy branching.
/// </summary>
internal class MockHybridStore : IVectorStore, IConfigurableHybridSearchStore
{
    public bool HybridSearchCalled { get; private set; }
    public string? LastQuery { get; private set; }
    public int LastTopK { get; private set; }
    public HybridSearchOptions? LastOptions { get; private set; }

    public Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
        float[] denseVector, string query, HybridSearchOptions options, int topK = 5,
        VectorFilter? filter = null, CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        return HybridSearchAsync(denseVector, query, topK, filter, cancellationToken);
    }

    public Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
        float[] denseVector, string query, int topK,
        VectorFilter? filter = null, CancellationToken cancellationToken = default)
    {
        HybridSearchCalled = true;
        LastQuery = query;
        LastTopK = topK;

        var results = new List<VectorSearchResult>
        {
            new VectorSearchResult(new VectorRecord("hybrid-result", denseVector, "hybrid content"), 0.95)
        };
        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int topK = 5, VectorFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(new List<VectorSearchResult>());
    }

    public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        => Task.FromResult<VectorRecord?>(null);

    public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// Mock reranker that reverses the result order (for testing that reranker is actually called).
/// </summary>
internal class ReversingReranker : IReranker
{
    public bool WasCalled { get; private set; }

    public Task<IReadOnlyList<VectorSearchResult>> RerankAsync(
        string query, IReadOnlyList<VectorSearchResult> results,
        CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        var reversed = results.Reverse().ToList();
        // Reassign scores in descending order
        for (int i = 0; i < reversed.Count; i++)
            reversed[i] = new VectorSearchResult(reversed[i].Record, 1.0 - (i * 0.1));
        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(reversed);
    }
}

#endregion
