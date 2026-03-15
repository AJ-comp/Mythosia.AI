using Mythosia.AI.Loaders;
using Mythosia.AI.Rag;
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

#region 3. RRF Merge & HybridRetrievalStrategy Tests

[TestClass]
public class HybridRetrievalStrategyTests
{
    /// <summary>
    /// When store does not support native hybrid search, HybridRetrievalStrategy should
    /// use BM25 index + vector search + RRF merge.
    /// </summary>
    [TestMethod]
    public async Task Retrieve_NonHybridStore_UsesBm25AndRrf()
    {
        // Arrange: InMemoryVectorStore does not support native hybrid search.
        var store = new InMemoryVectorStore();
        var bm25 = new Bm25Index();

        // Insert some records into both stores
        var records = new[]
        {
            new VectorRecord("doc1", GenerateVector(128, 1), "환불 정책 안내 14일 이내"),
            new VectorRecord("doc2", GenerateVector(128, 2), "배송 정책 안내 무료 배송"),
            new VectorRecord("doc3", GenerateVector(128, 3), "교환 정책 수령 후 7일"),
        };

        foreach (var r in records)
        {
            await store.UpsertAsync(r);
            bm25.Index(r.Id, r.Content);
        }

        var strategy = new HybridRetrievalStrategy(store, 0.5f, bm25);

        // Act: query with a vector close to doc1 + text query matching doc1
        var queryVector = GenerateVector(128, 1); // similar to doc1
        var results = await strategy.RetrieveAsync(queryVector, "환불 정책", 3);

        // Assert
        Assert.IsTrue(results.Count > 0, "Should return results");
        // doc1 should appear since it matches both vector and keyword
        Assert.IsTrue(results.Any(r => r.Record.Id == "doc1"),
            "doc1 should appear in results (matches both vector and keyword)");
    }

    /// <summary>
    /// When store supports native hybrid search, HybridRetrievalStrategy should delegate natively.
    /// </summary>
    [TestMethod]
    public async Task Retrieve_HybridCapableStore_DelegatesToNative()
    {
        var mockStore = new MockHybridStore();
        var strategy = new HybridRetrievalStrategy(mockStore, 0.7f, bm25Index: null);

        var results = await strategy.RetrieveAsync(new float[] { 1, 2 }, "test query", 5);

        Assert.IsTrue(mockStore.HybridSearchCalled, "Should delegate to IVectorStore.HybridSearchAsync");
        Assert.AreEqual("test query", mockStore.LastQuery);
        Assert.AreEqual(5, mockStore.LastTopK);
    }

    /// <summary>
    /// RRF merge: a document appearing in BOTH vector and BM25 results should rank higher
    /// than a document appearing in only one.
    /// </summary>
    [TestMethod]
    public async Task RrfMerge_DocumentInBothLists_RanksHigher()
    {
        var store = new InMemoryVectorStore();
        var bm25 = new Bm25Index();

        // doc1: appears in both vector and BM25 results
        // doc2: only in vector results
        // doc3: only in BM25 results
        var doc1 = new VectorRecord("doc1", GenerateVector(64, 10), "hybrid search retrieval ranking");
        var doc2 = new VectorRecord("doc2", GenerateVector(64, 10), "unrelated content different topic"); // same vector as doc1
        var doc3 = new VectorRecord("doc3", GenerateVector(64, 99), "hybrid search retrieval ranking advanced"); // different vector

        await store.UpsertAsync(doc1);
        await store.UpsertAsync(doc2);
        await store.UpsertAsync(doc3);

        bm25.Index("doc1", doc1.Content);
        bm25.Index("doc2", doc2.Content);
        bm25.Index("doc3", doc3.Content);

        var strategy = new HybridRetrievalStrategy(store, 0.5f, bm25);
        var queryVector = GenerateVector(64, 10); // matches doc1/doc2 vectors

        var results = await strategy.RetrieveAsync(queryVector, "hybrid search retrieval ranking", 3);

        Assert.IsTrue(results.Count > 0);
        // doc1 should rank highest because it matches BOTH vector (rank ~1) and keyword (rank 1)
        Assert.AreEqual("doc1", results[0].Record.Id,
            "Document matching both vector and keyword should rank first via RRF");
    }

    /// <summary>
    /// Without BM25 index and without native hybrid support, should fall back to pure vector search.
    /// </summary>
    [TestMethod]
    public async Task Retrieve_NoBm25NorHybridCapable_FallsBackToVector()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(new VectorRecord("doc1", GenerateVector(32, 1), "hello world"));

        // No BM25 index
        var strategy = new HybridRetrievalStrategy(store, 0.5f, bm25Index: null);

        var results = await strategy.RetrieveAsync(GenerateVector(32, 1), "hello", 5);

        Assert.IsTrue(results.Count > 0, "Should fall back to vector search");
        Assert.AreEqual("doc1", results[0].Record.Id);
    }

    /// <summary>
    /// RRF scores should be descending.
    /// </summary>
    [TestMethod]
    public async Task RrfMerge_ResultsAreDescendingByScore()
    {
        var store = new InMemoryVectorStore();
        var bm25 = new Bm25Index();

        for (int i = 0; i < 10; i++)
        {
            var r = new VectorRecord($"doc{i}", GenerateVector(32, i), $"document number {i} keyword search content");
            await store.UpsertAsync(r);
            bm25.Index(r.Id, r.Content);
        }

        var strategy = new HybridRetrievalStrategy(store, 0.5f, bm25);
        var results = await strategy.RetrieveAsync(GenerateVector(32, 0), "keyword search content", 5);

        for (int i = 1; i < results.Count; i++)
        {
            Assert.IsTrue(results[i - 1].Score >= results[i].Score,
                $"RRF results should be descending: [{i - 1}]={results[i - 1].Score:F6} vs [{i}]={results[i].Score:F6}");
        }
    }

    /// <summary>
    /// Delegated hybrid search currently does not support caller-controlled vector weighting.
    /// Even with vectorWeight=1.0, the native hybrid implementation may still rank keyword matches first.
    /// </summary>
    [TestMethod]
    public async Task HybridSearch_VectorWeight1_CurrentlyDoesNotOverrideNativeWeighting()
    {
        var store = new InMemoryVectorStore();
        var bm25 = new Bm25Index();

        var doc1 = new VectorRecord("vec-match", GenerateVector(32, 1), "unrelated text");
        var doc2 = new VectorRecord("kw-match", GenerateVector(32, 99), "specific keyword query terms");

        await store.UpsertAsync(doc1);
        await store.UpsertAsync(doc2);
        bm25.Index(doc1.Id, doc1.Content);
        bm25.Index(doc2.Id, doc2.Content);

        var strategy = new HybridRetrievalStrategy(store, 1.0f, bm25);
        var results = await strategy.RetrieveAsync(GenerateVector(32, 1), "specific keyword query terms", 2);

        Assert.IsTrue(results.Count > 0);
        Assert.AreEqual("kw-match", results[0].Record.Id,
            "Current native hybrid behavior does not apply caller-provided vectorWeight overrides");
    }

    private static float[] GenerateVector(int dim, int seed)
    {
        var rng = new Random(seed);
        var vec = new float[dim];
        double norm = 0;
        for (int i = 0; i < dim; i++)
        {
            vec[i] = (float)(rng.NextDouble() - 0.5);
            norm += vec[i] * vec[i];
        }
        norm = Math.Sqrt(norm);
        for (int i = 0; i < dim; i++)
            vec[i] /= (float)norm;
        return vec;
    }
}

#endregion

#region 4. VectorRetrievalStrategy Tests

[TestClass]
public class VectorRetrievalStrategyTests
{
    [TestMethod]
    public async Task Retrieve_DelegatesToVectorStore()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(new VectorRecord("doc1", GenerateUnitVector(64, 42), "test content"));

        var strategy = new VectorRetrievalStrategy(store);
        var results = await strategy.RetrieveAsync(
            GenerateUnitVector(64, 42), "ignored query text", 5);

        Assert.IsTrue(results.Count > 0);
        Assert.AreEqual("doc1", results[0].Record.Id);
    }

    [TestMethod]
    public async Task Retrieve_IgnoresQueryText()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(new VectorRecord("doc1", GenerateUnitVector(64, 1), "apple banana"));

        var strategy = new VectorRetrievalStrategy(store);

        // Different query texts with same vector should give same results
        var results1 = await strategy.RetrieveAsync(GenerateUnitVector(64, 1), "apple", 5);
        var results2 = await strategy.RetrieveAsync(GenerateUnitVector(64, 1), "completely different", 5);

        Assert.AreEqual(results1.Count, results2.Count);
        Assert.AreEqual(results1[0].Record.Id, results2[0].Record.Id);
    }

    private static float[] GenerateUnitVector(int dim, int seed)
    {
        var rng = new Random(seed);
        var vec = new float[dim];
        double norm = 0;
        for (int i = 0; i < dim; i++)
        {
            vec[i] = (float)(rng.NextDouble() - 0.5);
            norm += vec[i] * vec[i];
        }
        norm = Math.Sqrt(norm);
        for (int i = 0; i < dim; i++)
            vec[i] /= (float)norm;
        return vec;
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

        // No retrieval strategy, no reranker → defaults to VectorRetrievalStrategy
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
    /// Pipeline with HybridRetrievalStrategy should combine BM25 and vector search.
    /// </summary>
    [TestMethod]
    public async Task Pipeline_WithHybridStrategy_CombinesBm25AndVector()
    {
        var embedding = new LocalEmbeddingProvider(dimensions: 128);
        var store = new InMemoryVectorStore();
        var splitter = new CharacterTextSplitter(500, 50);
        var contextBuilder = new DefaultContextBuilder();
        var bm25 = new Bm25Index();

        var strategy = new HybridRetrievalStrategy(store, 0.5f, bm25);

        var pipeline = new RagPipeline(
            embedding, store, splitter, contextBuilder,
            retrievalStrategy: strategy, reranker: null,
            options: new RagPipelineOptions { DefaultQuery = new RagQueryOptions { FinalFilter = new RagFilter { TopK = 3 } } });

        // Index documents
        var doc = new RagDocument
        {
            Id = "hybrid-doc", Content = "하이브리드 검색 테스트 문서입니다.", Source = "test.txt"
        };
        await pipeline.IndexDocumentAsync(doc);

        // Also index in BM25
        var allRecords = await store.ListAllRecordsAsync();
        foreach (var r in allRecords)
            bm25.Index(r.Id, r.Content);

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
internal class MockHybridStore : IVectorStore
{
    public bool HybridSearchCalled { get; private set; }
    public string? LastQuery { get; private set; }
    public int LastTopK { get; private set; }

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
        string query, IReadOnlyList<VectorSearchResult> results, int topK,
        CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        var reversed = results.Reverse().Take(topK).ToList();
        // Reassign scores in descending order
        for (int i = 0; i < reversed.Count; i++)
            reversed[i] = new VectorSearchResult(reversed[i].Record, 1.0 - (i * 0.1));
        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(reversed);
    }
}

#endregion
