using System.Text.Json;
using Mythosia.VectorDb.Pinecone;

namespace Mythosia.VectorDb.Tests.Pinecone;

[TestClass]
public class PineconeStoreTests
{
    private const string TestApiKey = "pcsk_test_key";
    private const string TestHost = "https://test-index.svc.pinecone.io";

    private static PineconeOptions DefaultOptions(string? ns = null) => new PineconeOptions
    {
        IndexHost = TestHost,
        ApiKey = TestApiKey,
        Namespace = ns
    };

    private static (PineconeStore store, MockHttpMessageHandler handler) CreateStore(string? ns = null)
    {
        var handler = new MockHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri(TestHost + "/") };
        var store = new PineconeStore(DefaultOptions(ns), http);
        return (store, handler);
    }

    #region Options Validation

    [TestMethod]
    public void Validate_MissingApiKey_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PineconeStore(new PineconeOptions { IndexHost = TestHost, ApiKey = "" }));
    }

    [TestMethod]
    public void Validate_MissingIndexHost_WhenNotAutoCreate_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PineconeStore(new PineconeOptions { ApiKey = TestApiKey, IndexHost = "" }));
    }

    [TestMethod]
    public void Validate_AutoCreate_MissingIndexName_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PineconeStore(new PineconeOptions
            {
                ApiKey = TestApiKey,
                AutoCreateIndex = true,
                Dimension = 3,
                Cloud = "aws",
                Region = "us-east-1"
            }));
    }

    [TestMethod]
    public void Validate_ValidOptions_DoesNotThrow()
    {
        var (store, _) = CreateStore();
        store.Dispose();
    }

    #endregion

    #region Upsert

    [TestMethod]
    public async Task UpsertAsync_SendsCorrectPayload()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk();

        var record = new VectorRecord("id-1", new float[] { 0.1f, 0.2f, 0.3f }, "hello world")
        {
            Metadata = { ["category"] = "test" }
        };

        await store.UpsertAsync(record);

        Assert.AreEqual(1, handler.Requests.Count);
        var req = handler.Requests[0];
        Assert.AreEqual(HttpMethod.Post, req.Method);
        Assert.IsTrue(req.Uri.PathAndQuery.Contains("vectors/upsert"));
        Assert.IsTrue(req.HasHeader("Api-Key", TestApiKey));

        using var doc = JsonDocument.Parse(req.Body!);
        var root = doc.RootElement;
        Assert.IsFalse(root.TryGetProperty("namespace", out _), "Namespace should be omitted when null");

        var vectors = root.GetProperty("vectors");
        Assert.AreEqual(1, vectors.GetArrayLength());
        Assert.AreEqual("id-1", vectors[0].GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task UpsertAsync_WithNamespace_IncludesNamespaceInPayload()
    {
        var (store, handler) = CreateStore(ns: "production");
        handler.EnqueueOk();

        var record = new VectorRecord("id-1", new float[] { 0.1f, 0.2f, 0.3f }, "test");
        await store.UpsertAsync(record);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        var ns = doc.RootElement.GetProperty("namespace").GetString();
        Assert.AreEqual("production", ns);
    }

    [TestMethod]
    public async Task UpsertBatchAsync_SplitsIntoBatches()
    {
        var options = DefaultOptions();
        options.UpsertBatchSize = 2;

        var handler = new MockHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri(TestHost + "/") };
        var store = new PineconeStore(options, http);

        handler.EnqueueOk(); // batch 1
        handler.EnqueueOk(); // batch 2

        var records = Enumerable.Range(0, 3)
            .Select(i => new VectorRecord($"id-{i}", new float[] { i, 0, 0 }, $"content-{i}"))
            .ToList();

        await store.UpsertBatchAsync(records);

        Assert.AreEqual(2, handler.Requests.Count, "3 records with batch size 2 = 2 requests");

        using var batch1 = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.AreEqual(2, batch1.RootElement.GetProperty("vectors").GetArrayLength());

        using var batch2 = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.AreEqual(1, batch2.RootElement.GetProperty("vectors").GetArrayLength());
    }

    [TestMethod]
    public async Task UpsertBatchAsync_EmptyList_NoRequests()
    {
        var (store, handler) = CreateStore();
        await store.UpsertBatchAsync(Array.Empty<VectorRecord>());
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task UpsertAsync_StoresContentAsMetadata()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk();

        await store.UpsertAsync(new VectorRecord("id-1", new float[] { 1, 0, 0 }, "my content"));

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        var metadata = doc.RootElement.GetProperty("vectors")[0].GetProperty("metadata");
        Assert.AreEqual("my content", metadata.GetProperty("_content").GetString());
    }

    #endregion

    #region Get / Fetch

    [TestMethod]
    public async Task GetAsync_ReturnsRecord()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new
        {
            vectors = new Dictionary<string, object>
            {
                ["id-1"] = new
                {
                    id = "id-1",
                    values = new float[] { 0.1f, 0.2f, 0.3f },
                    metadata = new Dictionary<string, object>
                    {
                        ["_content"] = "hello",
                        ["category"] = "test"
                    }
                }
            }
        });

        var result = await store.GetAsync("id-1");

        Assert.IsNotNull(result);
        Assert.AreEqual("id-1", result.Id);
        Assert.AreEqual("hello", result.Content);
        Assert.AreEqual("test", result.Metadata["category"]);

        var req = handler.Requests[0];
        Assert.AreEqual(HttpMethod.Get, req.Method);
        Assert.IsTrue(req.Uri.PathAndQuery.Contains("vectors/fetch?ids=id-1"));
    }

    [TestMethod]
    public async Task GetAsync_WithNamespace_IncludesNamespaceInPath()
    {
        var (store, handler) = CreateStore(ns: "staging");
        handler.EnqueueOk(new { vectors = new Dictionary<string, object>() });

        await store.GetAsync("id-1");

        var path = handler.Requests[0].Uri.PathAndQuery;
        Assert.IsTrue(path.Contains("namespace=staging"), $"Path should contain namespace: {path}");
    }

    [TestMethod]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new { vectors = new Dictionary<string, object>() });

        var result = await store.GetAsync("non-existent");
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetAsync_WithFilter_ExcludesNonMatching()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new
        {
            vectors = new Dictionary<string, object>
            {
                ["id-1"] = new
                {
                    id = "id-1",
                    values = new float[] { 1, 0, 0 },
                    metadata = new Dictionary<string, object> { ["_content"] = "test", ["category"] = "A" }
                }
            }
        });

        var filter = new VectorFilter().Where("category", "B");
        var result = await store.GetAsync("id-1", filter);

        Assert.IsNull(result, "Record metadata does not match filter, should return null");
    }

    [TestMethod]
    public async Task GetBatchAsync_ReturnsMultipleRecords()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new
        {
            vectors = new Dictionary<string, object>
            {
                ["id-1"] = new { id = "id-1", values = new float[] { 1, 0, 0 }, metadata = new Dictionary<string, object> { ["_content"] = "a" } },
                ["id-2"] = new { id = "id-2", values = new float[] { 0, 1, 0 }, metadata = new Dictionary<string, object> { ["_content"] = "b" } }
            }
        });

        var results = await store.GetBatchAsync(new[] { "id-1", "id-2" });

        Assert.AreEqual(2, results.Count);
        var path = handler.Requests[0].Uri.PathAndQuery;
        Assert.IsTrue(path.Contains("ids=id-1"), $"Path should contain id-1: {path}");
        Assert.IsTrue(path.Contains("ids=id-2"), $"Path should contain id-2: {path}");
    }

    [TestMethod]
    public async Task GetBatchAsync_EmptyIds_ReturnsEmpty()
    {
        var (store, handler) = CreateStore();
        var results = await store.GetBatchAsync(Array.Empty<string>());
        Assert.AreEqual(0, results.Count);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task GetBatchAsync_WithNamespace_IncludesNamespaceInPath()
    {
        var (store, handler) = CreateStore(ns: "tenant-a");
        handler.EnqueueOk(new { vectors = new Dictionary<string, object>() });

        await store.GetBatchAsync(new[] { "id-1" });

        var path = handler.Requests[0].Uri.PathAndQuery;
        Assert.IsTrue(path.Contains("namespace=tenant-a"), $"Path should contain namespace: {path}");
    }

    #endregion

    #region Delete

    [TestMethod]
    public async Task DeleteAsync_SendsDeleteRequest()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk();

        await store.DeleteAsync("id-1");

        var req = handler.Requests[0];
        Assert.AreEqual(HttpMethod.Post, req.Method);
        Assert.IsTrue(req.Uri.PathAndQuery.Contains("vectors/delete"));

        using var doc = JsonDocument.Parse(req.Body!);
        var ids = doc.RootElement.GetProperty("ids");
        Assert.AreEqual("id-1", ids[0].GetString());
    }

    [TestMethod]
    public async Task DeleteAsync_WithNamespace_IncludesNamespace()
    {
        var (store, handler) = CreateStore(ns: "production");
        handler.EnqueueOk();

        await store.DeleteAsync("id-1");

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.AreEqual("production", doc.RootElement.GetProperty("namespace").GetString());
    }

    [TestMethod]
    public async Task DeleteAsync_WithFilter_ChecksExistenceFirst()
    {
        var (store, handler) = CreateStore();

        // First call: GetAsync (fetch) to check existence
        handler.EnqueueOk(new
        {
            vectors = new Dictionary<string, object>
            {
                ["id-1"] = new
                {
                    id = "id-1",
                    values = new float[] { 1, 0, 0 },
                    metadata = new Dictionary<string, object> { ["_content"] = "x", ["env"] = "prod" }
                }
            }
        });
        // Second call: actual delete
        handler.EnqueueOk();

        var filter = new VectorFilter().Where("env", "prod");
        await store.DeleteAsync("id-1", filter);

        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Requests[0].Uri.PathAndQuery.Contains("vectors/fetch"));
        Assert.IsTrue(handler.Requests[1].Uri.PathAndQuery.Contains("vectors/delete"));
    }

    [TestMethod]
    public async Task DeleteAsync_WithFilter_NoMatch_SkipsDelete()
    {
        var (store, handler) = CreateStore();

        handler.EnqueueOk(new
        {
            vectors = new Dictionary<string, object>
            {
                ["id-1"] = new
                {
                    id = "id-1",
                    values = new float[] { 1, 0, 0 },
                    metadata = new Dictionary<string, object> { ["_content"] = "x", ["env"] = "staging" }
                }
            }
        });

        var filter = new VectorFilter().Where("env", "prod");
        await store.DeleteAsync("id-1", filter);

        Assert.AreEqual(1, handler.Requests.Count, "Should only fetch, not delete (filter mismatch)");
    }

    [TestMethod]
    public async Task DeleteByFilterAsync_WithConditions_SendsMetadataFilter()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk();

        var filter = new VectorFilter().Where("category", "old");
        await store.DeleteByFilterAsync(filter);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.IsTrue(doc.RootElement.TryGetProperty("filter", out _));
        Assert.IsFalse(doc.RootElement.TryGetProperty("deleteAll", out _));
    }

    [TestMethod]
    public async Task DeleteByFilterAsync_EmptyConditions_SendsDeleteAll()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk();

        await store.DeleteByFilterAsync(new VectorFilter());

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.IsTrue(doc.RootElement.GetProperty("deleteAll").GetBoolean());
    }

    [TestMethod]
    public async Task DeleteByFilterAsync_WithNamespace_IncludesNamespace()
    {
        var (store, handler) = CreateStore(ns: "tenant-b");
        handler.EnqueueOk();

        await store.DeleteByFilterAsync(new VectorFilter());

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.AreEqual("tenant-b", doc.RootElement.GetProperty("namespace").GetString());
    }

    #endregion

    #region Search

    [TestMethod]
    public async Task SearchAsync_SendsCorrectPayload()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new
        {
            matches = new[]
            {
                new { id = "id-1", score = 0.95, values = new float[] { 1, 0, 0 }, metadata = new Dictionary<string, object> { ["_content"] = "result" } }
            }
        });

        var results = await store.SearchAsync(new float[] { 1, 0, 0 }, topK: 5);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("id-1", results[0].Record.Id);
        Assert.AreEqual(0.95, results[0].Score, 0.001);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        var root = doc.RootElement;
        Assert.AreEqual(5, root.GetProperty("topK").GetInt32());
        Assert.IsTrue(root.GetProperty("includeMetadata").GetBoolean());
        Assert.IsTrue(root.GetProperty("includeValues").GetBoolean());
    }

    [TestMethod]
    public async Task SearchAsync_WithNamespace_IncludesNamespace()
    {
        var (store, handler) = CreateStore(ns: "search-ns");
        handler.EnqueueOk(new { matches = Array.Empty<object>() });

        await store.SearchAsync(new float[] { 1, 0, 0 }, topK: 3);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.AreEqual("search-ns", doc.RootElement.GetProperty("namespace").GetString());
    }

    [TestMethod]
    public async Task SearchAsync_WithMinScore_FiltersLowScores()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new
        {
            matches = new[]
            {
                new { id = "high", score = 0.9, values = new float[] { 1, 0, 0 }, metadata = new Dictionary<string, object> { ["_content"] = "a" } },
                new { id = "low", score = 0.2, values = new float[] { 0, 1, 0 }, metadata = new Dictionary<string, object> { ["_content"] = "b" } }
            }
        });

        var filter = new VectorFilter { MinScore = 0.5 };
        var results = await store.SearchAsync(new float[] { 1, 0, 0 }, topK: 10, filter: filter);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("high", results[0].Record.Id);
    }

    [TestMethod]
    public async Task SearchAsync_WithMetadataFilter_SendsFilter()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new { matches = Array.Empty<object>() });

        var filter = new VectorFilter().Where("env", "prod");
        await store.SearchAsync(new float[] { 1, 0, 0 }, topK: 5, filter: filter);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.IsTrue(doc.RootElement.TryGetProperty("filter", out var filterProp));
        Assert.IsTrue(filterProp.GetRawText().Contains("$eq"));
    }

    [TestMethod]
    public async Task SearchAsync_EmptyMatches_ReturnsEmpty()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new { matches = Array.Empty<object>() });

        var results = await store.SearchAsync(new float[] { 1, 0, 0 });
        Assert.AreEqual(0, results.Count);
    }

    #endregion

    #region Hybrid Search

    [TestMethod]
    public async Task HybridSearchAsync_IncludesSparseVector()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new
        {
            matches = new[]
            {
                new { id = "id-1", score = 0.85, values = new float[] { 1, 0, 0 }, metadata = new Dictionary<string, object> { ["_content"] = "result" } }
            }
        });

        var results = await store.HybridSearchAsync(
            new float[] { 1, 0, 0 }, "test query", topK: 5);

        Assert.AreEqual(1, results.Count);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        var root = doc.RootElement;
        Assert.IsTrue(root.TryGetProperty("sparseVector", out var sparse));
        Assert.IsTrue(sparse.TryGetProperty("indices", out _));
        Assert.IsTrue(sparse.TryGetProperty("values", out _));
    }

    [TestMethod]
    public async Task HybridSearchAsync_WithNamespace_IncludesNamespace()
    {
        var (store, handler) = CreateStore(ns: "hybrid-ns");
        handler.EnqueueOk(new { matches = Array.Empty<object>() });

        await store.HybridSearchAsync(new float[] { 1, 0, 0 }, "query", topK: 3);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.AreEqual("hybrid-ns", doc.RootElement.GetProperty("namespace").GetString());
    }

    #endregion

    #region Count

    [TestMethod]
    public async Task CountAsync_NoNamespace_ReturnsDefaultNamespaceCount()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new
        {
            totalVectorCount = 100,
            namespaces = new Dictionary<string, object>
            {
                [""] = new { vectorCount = 50 },
                ["other"] = new { vectorCount = 50 }
            }
        });

        var count = await store.CountAsync();

        Assert.AreEqual(50, count, "Should return default namespace count, not total");
    }

    [TestMethod]
    public async Task CountAsync_WithNamespace_ReturnsNamespaceCount()
    {
        var (store, handler) = CreateStore(ns: "production");
        handler.EnqueueOk(new
        {
            totalVectorCount = 200,
            namespaces = new Dictionary<string, object>
            {
                [""] = new { vectorCount = 50 },
                ["production"] = new { vectorCount = 120 },
                ["staging"] = new { vectorCount = 30 }
            }
        });

        var count = await store.CountAsync();

        Assert.AreEqual(120, count, "Should return only 'production' namespace count");
    }

    [TestMethod]
    public async Task CountAsync_NamespaceNotFound_ReturnsZero()
    {
        var (store, handler) = CreateStore(ns: "empty-ns");
        handler.EnqueueOk(new
        {
            totalVectorCount = 100,
            namespaces = new Dictionary<string, object>
            {
                [""] = new { vectorCount = 100 }
            }
        });

        var count = await store.CountAsync();

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task CountAsync_WithFilter_UsesPOST()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new
        {
            totalVectorCount = 10,
            namespaces = new Dictionary<string, object> { [""] = new { vectorCount = 10 } }
        });

        var filter = new VectorFilter().Where("category", "docs");
        await store.CountAsync(filter);

        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
    }

    [TestMethod]
    public async Task CountAsync_WithoutFilter_UsesGET()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new
        {
            totalVectorCount = 10,
            namespaces = new Dictionary<string, object> { [""] = new { vectorCount = 10 } }
        });

        await store.CountAsync();

        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
    }

    #endregion

    #region Connection Verification

    [TestMethod]
    public async Task VerifyConnectionAsync_SendsDescribeIndexStats()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk();

        await store.VerifyConnectionAsync();

        Assert.AreEqual(1, handler.Requests.Count);
        Assert.IsTrue(handler.Requests[0].Uri.PathAndQuery.Contains("describe_index_stats"));
    }

    #endregion

    #region Error Handling

    [TestMethod]
    public async Task SendAsync_NonSuccess_ThrowsWithDetails()
    {
        var (store, handler) = CreateStore();
        handler.Enqueue(System.Net.HttpStatusCode.BadRequest, new { error = "invalid request" });

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SearchAsync(new float[] { 1, 0, 0 }));

        Assert.IsTrue(ex.Message.Contains("400"), "Should include status code");
    }

    [TestMethod]
    public async Task UpsertAsync_NullRecord_Throws()
    {
        var (store, _) = CreateStore();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.UpsertAsync(null!));
    }

    [TestMethod]
    public async Task SearchAsync_InvalidTopK_Throws()
    {
        var (store, _) = CreateStore();
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.SearchAsync(new float[] { 1 }, topK: 0));
    }

    [TestMethod]
    public async Task GetAsync_EmptyId_Throws()
    {
        var (store, _) = CreateStore();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.GetAsync(""));
    }

    [TestMethod]
    public async Task DeleteAsync_EmptyId_Throws()
    {
        var (store, _) = CreateStore();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.DeleteAsync(""));
    }

    #endregion

    #region Metadata / Content Round-trip

    [TestMethod]
    public async Task ContentField_SeparatedFromMetadata()
    {
        var (store, handler) = CreateStore();
        handler.EnqueueOk(new
        {
            vectors = new Dictionary<string, object>
            {
                ["id-1"] = new
                {
                    id = "id-1",
                    values = new float[] { 1, 0, 0 },
                    metadata = new Dictionary<string, object>
                    {
                        ["_content"] = "my content",
                        ["author"] = "alice",
                        ["year"] = "2024"
                    }
                }
            }
        });

        var result = await store.GetAsync("id-1");

        Assert.IsNotNull(result);
        Assert.AreEqual("my content", result.Content);
        Assert.AreEqual("alice", result.Metadata["author"]);
        Assert.AreEqual("2024", result.Metadata["year"]);
        Assert.IsFalse(result.Metadata.ContainsKey("_content"), "_content should not appear in user metadata");
    }

    #endregion

    #region Dispose

    [TestMethod]
    public void Dispose_OwnsHttpClient_DisposesIt()
    {
        var store = new PineconeStore(new PineconeOptions
        {
            IndexHost = TestHost,
            ApiKey = TestApiKey
        });

        store.Dispose();
        // No exception = success; HttpClient is disposed internally
    }

    [TestMethod]
    public async Task Dispose_ExternalHttpClient_DoesNotDisposeIt()
    {
        var handler = new MockHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri(TestHost + "/") };
        var store = new PineconeStore(DefaultOptions(), http);

        store.Dispose();

        // HttpClient should still be usable — no ObjectDisposedException
        handler.EnqueueOk();
        await http.GetAsync("/test");
    }

    #endregion
}
