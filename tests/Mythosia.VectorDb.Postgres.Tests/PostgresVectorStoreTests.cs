using Mythosia.VectorDb;
using Mythosia.VectorDb.Postgres;

namespace Mythosia.VectorDb.Postgres.Tests;

/// <summary>
/// Integration tests for <see cref="PostgresVectorStore"/>.
/// Requires a running PostgreSQL instance with pgvector extension.
/// 
/// Set environment variable MYTHOSIA_PG_CONN to the connection string, e.g.:
///   Host=localhost;Port=5432;Database=mythosia_test;Username=postgres;Password=secret
///
/// To run locally with Docker:
///   docker run -d --name pgvector -p 5432:5432 -e POSTGRES_PASSWORD=secret pgvector/pgvector:pg16
/// </summary>
[TestClass]
public class PostgresVectorStoreTests
{
    private const int TestDimension = 3;
    private const string TestCollection = "test_collection";

    private static string? _connectionString;
    private static bool _skipTests;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _connectionString = Environment.GetEnvironmentVariable("MYTHOSIA_PG_CONN");
        _skipTests = string.IsNullOrWhiteSpace(_connectionString);

        if (_skipTests)
        {
            Console.WriteLine("MYTHOSIA_PG_CONN not set — PostgreSQL integration tests will be skipped.");
        }
    }

    private PostgresVectorStore CreateStore(bool ensureSchema = true)
    {
        return new PostgresVectorStore(new PostgresVectorStoreOptions
        {
            ConnectionString = _connectionString!,
            Dimension = TestDimension,
            TableName = "mythosia_vectors_test",
            EnsureSchema = ensureSchema,
            IvfflatLists = 10
        });
    }

    private static void SkipIfNoDb()
    {
        if (_skipTests)
            Assert.Inconclusive("MYTHOSIA_PG_CONN not set. Skipping integration test.");
    }

    #region Schema Tests

    [TestMethod]
    public async Task EnsureSchema_CreatesTableAndIndexes()
    {
        SkipIfNoDb();
        using var store = CreateStore(ensureSchema: true);

        // Should not throw
        await store.CreateCollectionAsync(TestCollection);
    }

    [TestMethod]
    public async Task EnsureSchemaFalse_ThrowsWhenTableMissing()
    {
        SkipIfNoDb();
        using var store = new PostgresVectorStore(new PostgresVectorStoreOptions
        {
            ConnectionString = _connectionString!,
            Dimension = TestDimension,
            TableName = "nonexistent_table_xyz",
            EnsureSchema = false
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.CollectionExistsAsync("any"));
    }

    #endregion

    #region Upsert / Get Tests

    [TestMethod]
    public async Task UpsertAndGet_RoundTripsRecord()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanCollection(store);

        var record = new VectorRecord
        {
            Id = "doc-1",
            Content = "Hello world",
            Vector = new float[] { 0.1f, 0.2f, 0.3f },
            Namespace = "ns1",
            Metadata = { ["source"] = "test.txt", ["lang"] = "en" }
        };

        await store.UpsertAsync(TestCollection, record);

        var retrieved = await store.GetAsync(TestCollection, "doc-1");

        Assert.IsNotNull(retrieved);
        Assert.AreEqual("doc-1", retrieved.Id);
        Assert.AreEqual("Hello world", retrieved.Content);
        Assert.AreEqual("ns1", retrieved.Namespace);
        Assert.AreEqual(3, retrieved.Vector.Length);
        Assert.AreEqual("test.txt", retrieved.Metadata["source"]);
        Assert.AreEqual("en", retrieved.Metadata["lang"]);
    }

    [TestMethod]
    public async Task Upsert_UpdatesExistingRecord()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanCollection(store);

        await store.UpsertAsync(TestCollection, new VectorRecord
        {
            Id = "doc-update",
            Content = "Version 1",
            Vector = new float[] { 0.1f, 0.1f, 0.1f }
        });

        await store.UpsertAsync(TestCollection, new VectorRecord
        {
            Id = "doc-update",
            Content = "Version 2",
            Vector = new float[] { 0.9f, 0.9f, 0.9f }
        });

        var retrieved = await store.GetAsync(TestCollection, "doc-update");
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("Version 2", retrieved.Content);
    }

    [TestMethod]
    public async Task UpsertBatch_InsertsMultipleRecords()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanCollection(store);

        var records = new[]
        {
            new VectorRecord("batch-1", new float[] { 0.1f, 0.2f, 0.3f }, "First"),
            new VectorRecord("batch-2", new float[] { 0.4f, 0.5f, 0.6f }, "Second"),
            new VectorRecord("batch-3", new float[] { 0.7f, 0.8f, 0.9f }, "Third")
        };

        await store.UpsertBatchAsync(TestCollection, records);

        var r1 = await store.GetAsync(TestCollection, "batch-1");
        var r2 = await store.GetAsync(TestCollection, "batch-2");
        var r3 = await store.GetAsync(TestCollection, "batch-3");

        Assert.IsNotNull(r1);
        Assert.IsNotNull(r2);
        Assert.IsNotNull(r3);
        Assert.AreEqual("First", r1.Content);
        Assert.AreEqual("Second", r2.Content);
        Assert.AreEqual("Third", r3.Content);
    }

    [TestMethod]
    public async Task Get_ReturnsNullForMissingId()
    {
        SkipIfNoDb();
        using var store = CreateStore();

        var result = await store.GetAsync(TestCollection, "nonexistent-id");
        Assert.IsNull(result);
    }

    #endregion

    #region Search Tests

    [TestMethod]
    public async Task Search_ReturnsSimilarityOrderedResults()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanCollection(store);

        // Insert vectors with known similarity to query
        await store.UpsertBatchAsync(TestCollection, new[]
        {
            new VectorRecord("close", new float[] { 0.9f, 0.1f, 0.0f }, "Close match"),
            new VectorRecord("far",   new float[] { 0.0f, 0.0f, 1.0f }, "Far match"),
            new VectorRecord("mid",   new float[] { 0.5f, 0.5f, 0.0f }, "Mid match")
        });

        var queryVector = new float[] { 1.0f, 0.0f, 0.0f };
        var results = await store.SearchAsync(TestCollection, queryVector, topK: 3);

        Assert.IsTrue(results.Count > 0, "Should have results");
        Assert.AreEqual("close", results[0].Record.Id, "Closest vector should be first");
        Assert.IsTrue(results[0].Score > results[results.Count - 1].Score, "Scores should be descending");

        Console.WriteLine("Search results:");
        foreach (var r in results)
            Console.WriteLine($"  {r.Record.Id}: score={r.Score:F4}");
    }

    [TestMethod]
    public async Task Search_NamespaceFilter_FiltersCorrectly()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanCollection(store);

        await store.UpsertBatchAsync(TestCollection, new[]
        {
            new VectorRecord { Id = "ns-a1", Vector = new float[] { 1, 0, 0 }, Content = "A1", Namespace = "alpha" },
            new VectorRecord { Id = "ns-b1", Vector = new float[] { 1, 0, 0 }, Content = "B1", Namespace = "beta" }
        });

        var results = await store.SearchAsync(TestCollection,
            new float[] { 1, 0, 0 }, topK: 10,
            filter: VectorFilter.ByNamespace("alpha"));

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("ns-a1", results[0].Record.Id);
    }

    [TestMethod]
    public async Task Search_MetadataFilter_FiltersCorrectly()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanCollection(store);

        await store.UpsertBatchAsync(TestCollection, new[]
        {
            new VectorRecord
            {
                Id = "meta-1", Vector = new float[] { 1, 0, 0 }, Content = "Doc1",
                Metadata = { ["type"] = "article", ["lang"] = "en" }
            },
            new VectorRecord
            {
                Id = "meta-2", Vector = new float[] { 1, 0, 0 }, Content = "Doc2",
                Metadata = { ["type"] = "faq", ["lang"] = "en" }
            }
        });

        var results = await store.SearchAsync(TestCollection,
            new float[] { 1, 0, 0 }, topK: 10,
            filter: VectorFilter.ByMetadata("type", "article"));

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("meta-1", results[0].Record.Id);
    }

    [TestMethod]
    public async Task Search_MinScoreFilter_ExcludesLowScores()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanCollection(store);

        await store.UpsertBatchAsync(TestCollection, new[]
        {
            new VectorRecord("high", new float[] { 1, 0, 0 }, "High similarity"),
            new VectorRecord("low",  new float[] { 0, 0, 1 }, "Low similarity")
        });

        var results = await store.SearchAsync(TestCollection,
            new float[] { 1, 0, 0 }, topK: 10,
            filter: new VectorFilter { MinScore = 0.8 });

        Assert.IsTrue(results.All(r => r.Score >= 0.8), "All results should be above MinScore");
        Assert.IsTrue(results.Any(r => r.Record.Id == "high"), "High similarity record should be included");
    }

    #endregion

    #region Delete Tests

    [TestMethod]
    public async Task Delete_RemovesSingleRecord()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanCollection(store);

        await store.UpsertAsync(TestCollection, new VectorRecord("del-1", new float[] { 1, 0, 0 }, "To delete"));
        await store.DeleteAsync(TestCollection, "del-1");

        var result = await store.GetAsync(TestCollection, "del-1");
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task DeleteByFilter_RemovesMatchingRecords()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanCollection(store);

        await store.UpsertBatchAsync(TestCollection, new[]
        {
            new VectorRecord { Id = "df-1", Vector = new float[] { 1, 0, 0 }, Content = "Keep", Namespace = "keep" },
            new VectorRecord { Id = "df-2", Vector = new float[] { 0, 1, 0 }, Content = "Delete", Namespace = "remove" },
            new VectorRecord { Id = "df-3", Vector = new float[] { 0, 0, 1 }, Content = "Delete too", Namespace = "remove" }
        });

        await store.DeleteByFilterAsync(TestCollection, VectorFilter.ByNamespace("remove"));

        Assert.IsNotNull(await store.GetAsync(TestCollection, "df-1"), "Kept record should still exist");
        Assert.IsNull(await store.GetAsync(TestCollection, "df-2"), "Deleted record should be gone");
        Assert.IsNull(await store.GetAsync(TestCollection, "df-3"), "Deleted record should be gone");
    }

    [TestMethod]
    public async Task DeleteCollection_RemovesAllRecordsInCollection()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanCollection(store);

        await store.UpsertBatchAsync(TestCollection, new[]
        {
            new VectorRecord("dc-1", new float[] { 1, 0, 0 }, "One"),
            new VectorRecord("dc-2", new float[] { 0, 1, 0 }, "Two")
        });

        // Also insert into a different collection to verify isolation
        await store.UpsertAsync("other_collection", new VectorRecord("other-1", new float[] { 0, 0, 1 }, "Other"));

        await store.DeleteCollectionAsync(TestCollection);

        Assert.IsFalse(await store.CollectionExistsAsync(TestCollection), "Collection should not exist after deletion");
        Assert.IsTrue(await store.CollectionExistsAsync("other_collection"), "Other collection should still exist");

        // Cleanup
        await store.DeleteCollectionAsync("other_collection");
    }

    #endregion

    #region Collection Tests

    [TestMethod]
    public async Task CollectionExists_ReturnsTrueWhenDataPresent()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanCollection(store);

        await store.UpsertAsync(TestCollection, new VectorRecord("exists-1", new float[] { 1, 0, 0 }, "Exists"));

        Assert.IsTrue(await store.CollectionExistsAsync(TestCollection));
    }

    [TestMethod]
    public async Task CollectionExists_ReturnsFalseWhenEmpty()
    {
        SkipIfNoDb();
        using var store = CreateStore();

        Assert.IsFalse(await store.CollectionExistsAsync("definitely_nonexistent_collection_xyz"));
    }

    #endregion

    #region VectorToString / ParseVectorString Unit Tests

    [TestMethod]
    public void VectorToString_FormatsCorrectly()
    {
        var result = PostgresVectorStore.VectorToString(new float[] { 0.1f, 0.2f, 0.3f });
        Assert.AreEqual("[0.1,0.2,0.3]", result);
    }

    [TestMethod]
    public void ParseVectorString_ParsesCorrectly()
    {
        var result = PostgresVectorStore.ParseVectorString("[0.1,0.2,0.3]");
        Assert.AreEqual(3, result.Length);
        Assert.AreEqual(0.1f, result[0], 0.001f);
        Assert.AreEqual(0.2f, result[1], 0.001f);
        Assert.AreEqual(0.3f, result[2], 0.001f);
    }

    [TestMethod]
    public void ParseVectorString_EmptyReturnsEmpty()
    {
        Assert.AreEqual(0, PostgresVectorStore.ParseVectorString("").Length);
        Assert.AreEqual(0, PostgresVectorStore.ParseVectorString("[]").Length);
    }

    [TestMethod]
    public void VectorRoundTrip_PreservesValues()
    {
        var original = new float[] { -1.5f, 0f, 3.14159f, 100.001f };
        var str = PostgresVectorStore.VectorToString(original);
        var parsed = PostgresVectorStore.ParseVectorString(str);

        Assert.AreEqual(original.Length, parsed.Length);
        for (int i = 0; i < original.Length; i++)
            Assert.AreEqual(original[i], parsed[i], 0.0001f, $"Mismatch at index {i}");
    }

    #endregion

    #region Options Validation Tests

    [TestMethod]
    public void Options_EmptyConnectionString_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PostgresVectorStoreOptions { ConnectionString = "", Dimension = 3 }.Validate());
    }

    [TestMethod]
    public void Options_ZeroDimension_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PostgresVectorStoreOptions { ConnectionString = "Host=x", Dimension = 0 }.Validate());
    }

    [TestMethod]
    public void Options_InvalidTableName_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PostgresVectorStoreOptions { ConnectionString = "Host=x", Dimension = 3, TableName = "bad;name" }.Validate());
    }

    [TestMethod]
    public void Options_ValidOptions_DoesNotThrow()
    {
        new PostgresVectorStoreOptions
        {
            ConnectionString = "Host=localhost",
            Dimension = 1536,
            SchemaName = "public",
            TableName = "my_vectors"
        }.Validate();
    }

    #endregion

    private static async Task CleanCollection(PostgresVectorStore store)
    {
        try { await store.DeleteCollectionAsync(TestCollection); }
        catch { /* ignore if table doesn't exist yet */ }
    }
}
