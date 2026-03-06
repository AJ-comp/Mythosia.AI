using Mythosia.VectorDb;
using Mythosia.VectorDb.Postgres;

namespace Mythosia.VectorDb.Postgres.Tests;

/// <summary>
/// Integration tests for <see cref="PostgresStore"/>.
/// Requires a running PostgreSQL instance with pgvector extension.
/// 
/// Set environment variable MYTHOSIA_PG_CONN to the connection string, e.g.:
///   Host=localhost;Port=5432;Database=mythosia_test;Username=postgres;Password=secret
///
/// To run locally with Docker:
///   docker run -d --name pgvector -p 5432:5432 -e POSTGRES_PASSWORD=secret pgvector/pgvector:pg16
/// </summary>
[TestClass]
public class PostgresStoreTests
{
    private const int TestDimension = 3;
    private const string TestNamespace = "test_namespace";

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

    private PostgresStore CreateStore(bool ensureSchema = true)
    {
        return new PostgresStore(new PostgresOptions
        {
            ConnectionString = _connectionString!,
            Dimension = TestDimension,
            TableName = "mythosia_vectors_test",
            EnsureSchema = ensureSchema
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

        // Should not throw — upsert triggers schema creation
        var record = new VectorRecord("schema-test", new float[] { 1, 0, 0 }, "Test") { Namespace = TestNamespace };
        await store.UpsertAsync(record);
        await store.DeleteAsync("schema-test", new VectorFilter { Namespace = TestNamespace });
    }

    [TestMethod]
    public async Task EnsureSchemaFalse_ThrowsWhenTableMissing()
    {
        SkipIfNoDb();
        using var store = new PostgresStore(new PostgresOptions
        {
            ConnectionString = _connectionString!,
            Dimension = TestDimension,
            TableName = "nonexistent_table_xyz",
            EnsureSchema = false
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.UpsertAsync(new VectorRecord("any", new float[] { 1, 0, 0 }, "Test")));
    }

    #endregion

    #region Upsert / Get Tests

    [TestMethod]
    public async Task UpsertAndGet_RoundTripsRecord()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        var record = new VectorRecord
        {
            Id = "doc-1",
            Content = "Hello world",
            Vector = new float[] { 0.1f, 0.2f, 0.3f },
            Scope = "scope1",
            Metadata = { ["source"] = "test.txt", ["lang"] = "en" }
        };

        record.Namespace = TestNamespace;
        await store.UpsertAsync(record);

        var retrieved = await store.GetAsync("doc-1", new VectorFilter { Namespace = TestNamespace });

        Assert.IsNotNull(retrieved);
        Assert.AreEqual("doc-1", retrieved.Id);
        Assert.AreEqual("Hello world", retrieved.Content);
        Assert.AreEqual("scope1", retrieved.Scope);
        Assert.AreEqual(3, retrieved.Vector.Length);
        Assert.AreEqual("test.txt", retrieved.Metadata["source"]);
        Assert.AreEqual("en", retrieved.Metadata["lang"]);
    }

    [TestMethod]
    public async Task Upsert_UpdatesExistingRecord()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        await store.UpsertAsync(new VectorRecord
        {
            Id = "doc-update",
            Content = "Version 1",
            Vector = new float[] { 0.1f, 0.1f, 0.1f },
            Namespace = TestNamespace
        });

        await store.UpsertAsync(new VectorRecord
        {
            Id = "doc-update",
            Content = "Version 2",
            Vector = new float[] { 0.9f, 0.9f, 0.9f },
            Namespace = TestNamespace
        });

        var retrieved = await store.GetAsync("doc-update", new VectorFilter { Namespace = TestNamespace });
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("Version 2", retrieved.Content);
    }

    [TestMethod]
    public async Task UpsertBatch_InsertsMultipleRecords()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        var records = new[]
        {
            new VectorRecord("batch-1", new float[] { 0.1f, 0.2f, 0.3f }, "First"),
            new VectorRecord("batch-2", new float[] { 0.4f, 0.5f, 0.6f }, "Second"),
            new VectorRecord("batch-3", new float[] { 0.7f, 0.8f, 0.9f }, "Third")
        };

        foreach (var r in records) r.Namespace = TestNamespace;
        await store.UpsertBatchAsync(records);

        var nsFilter = new VectorFilter { Namespace = TestNamespace };
        var r1 = await store.GetAsync("batch-1", nsFilter);
        var r2 = await store.GetAsync("batch-2", nsFilter);
        var r3 = await store.GetAsync("batch-3", nsFilter);

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

        var result = await store.GetAsync("nonexistent-id", new VectorFilter { Namespace = TestNamespace });
        Assert.IsNull(result);
    }

    #endregion

    #region Search Tests

    [TestMethod]
    public async Task Search_ReturnsSimilarityOrderedResults()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        // Insert vectors with known similarity to query
        var records = new[]
        {
            new VectorRecord("close", new float[] { 0.9f, 0.1f, 0.0f }, "Close match") { Namespace = TestNamespace },
            new VectorRecord("far",   new float[] { 0.0f, 0.0f, 1.0f }, "Far match") { Namespace = TestNamespace },
            new VectorRecord("mid",   new float[] { 0.5f, 0.5f, 0.0f }, "Mid match") { Namespace = TestNamespace }
        };
        await store.UpsertBatchAsync(records);

        var queryVector = new float[] { 1.0f, 0.0f, 0.0f };
        var results = await store.SearchAsync(queryVector, topK: 3, filter: new VectorFilter { Namespace = TestNamespace });

        Assert.IsTrue(results.Count > 0, "Should have results");
        Assert.AreEqual("close", results[0].Record.Id, "Closest vector should be first");
        Assert.IsTrue(results[0].Score > results[results.Count - 1].Score, "Scores should be descending");

        Console.WriteLine("Search results:");
        foreach (var r in results)
            Console.WriteLine($"  {r.Record.Id}: score={r.Score:F4}");
    }

    [TestMethod]
    public async Task Search_ScopeFilter_FiltersCorrectly()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        await store.UpsertBatchAsync(new[]
        {
            new VectorRecord { Id = "sc-a1", Vector = new float[] { 1, 0, 0 }, Content = "A1", Scope = "alpha", Namespace = TestNamespace },
            new VectorRecord { Id = "sc-b1", Vector = new float[] { 1, 0, 0 }, Content = "B1", Scope = "beta", Namespace = TestNamespace }
        });

        var results = await store.SearchAsync(
            new float[] { 1, 0, 0 }, topK: 10,
            filter: new VectorFilter { Namespace = TestNamespace, Scope = "alpha" });

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("sc-a1", results[0].Record.Id);
    }

    [TestMethod]
    public async Task Search_MetadataFilter_FiltersCorrectly()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        await store.UpsertBatchAsync(new[]
        {
            new VectorRecord
            {
                Id = "meta-1", Vector = new float[] { 1, 0, 0 }, Content = "Doc1",
                Namespace = TestNamespace,
                Metadata = { ["type"] = "article", ["lang"] = "en" }
            },
            new VectorRecord
            {
                Id = "meta-2", Vector = new float[] { 1, 0, 0 }, Content = "Doc2",
                Namespace = TestNamespace,
                Metadata = { ["type"] = "faq", ["lang"] = "en" }
            }
        });

        var filter = VectorFilter.ByMetadata("type", "article");
        filter.Namespace = TestNamespace;
        var results = await store.SearchAsync(
            new float[] { 1, 0, 0 }, topK: 10,
            filter: filter);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("meta-1", results[0].Record.Id);
    }

    [TestMethod]
    public async Task Search_MinScoreFilter_ExcludesLowScores()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        await store.UpsertBatchAsync(new[]
        {
            new VectorRecord("high", new float[] { 1, 0, 0 }, "High similarity") { Namespace = TestNamespace },
            new VectorRecord("low",  new float[] { 0, 0, 1 }, "Low similarity") { Namespace = TestNamespace }
        });

        var results = await store.SearchAsync(
            new float[] { 1, 0, 0 }, topK: 10,
            filter: new VectorFilter { Namespace = TestNamespace, MinScore = 0.8 });

        Assert.IsTrue(results.All(r => r.Score >= 0.8), "All results should be above MinScore");
        Assert.IsTrue(results.Any(r => r.Record.Id == "high"), "High similarity record should be included");
    }

    [TestMethod]
    public async Task Search_RuntimeOptions_WorksWithProfileAndOverride()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        await store.UpsertBatchAsync(new[]
        {
            new VectorRecord("p1", new float[] { 1, 0, 0 }, "P1") { Namespace = TestNamespace },
            new VectorRecord("p2", new float[] { 0.9f, 0.1f, 0 }, "P2") { Namespace = TestNamespace }
        });

        var results = await store.SearchAsync(
            new float[] { 1, 0, 0 },
            topK: 2,
            filter: new VectorFilter { Namespace = TestNamespace },
            runtimeOptions: new HnswSearchRuntimeOptions
            {
                Profile = SearchProfile.Fast,
                EfSearch = 32
            });

        Assert.IsTrue(results.Count > 0);
    }

    #endregion

    #region Delete Tests

    [TestMethod]
    public async Task Delete_RemovesSingleRecord()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        await store.UpsertAsync(new VectorRecord("del-1", new float[] { 1, 0, 0 }, "To delete") { Namespace = TestNamespace });
        await store.DeleteAsync("del-1", new VectorFilter { Namespace = TestNamespace });

        var result = await store.GetAsync("del-1", new VectorFilter { Namespace = TestNamespace });
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task DeleteByFilter_RemovesMatchingRecords()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        await store.UpsertBatchAsync(new[]
        {
            new VectorRecord { Id = "df-1", Vector = new float[] { 1, 0, 0 }, Content = "Keep", Scope = "keep", Namespace = TestNamespace },
            new VectorRecord { Id = "df-2", Vector = new float[] { 0, 1, 0 }, Content = "Delete", Scope = "remove", Namespace = TestNamespace },
            new VectorRecord { Id = "df-3", Vector = new float[] { 0, 0, 1 }, Content = "Delete too", Scope = "remove", Namespace = TestNamespace }
        });

        await store.DeleteByFilterAsync(new VectorFilter { Namespace = TestNamespace, Scope = "remove" });

        var nsFilter = new VectorFilter { Namespace = TestNamespace };
        Assert.IsNotNull(await store.GetAsync("df-1", nsFilter), "Kept record should still exist");
        Assert.IsNull(await store.GetAsync("df-2", nsFilter), "Deleted record should be gone");
        Assert.IsNull(await store.GetAsync("df-3", nsFilter), "Deleted record should be gone");
    }

    [TestMethod]
    public async Task DeleteByNamespaceFilter_RemovesAllRecordsInNamespace()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        await store.UpsertBatchAsync(new[]
        {
            new VectorRecord("dc-1", new float[] { 1, 0, 0 }, "One") { Namespace = TestNamespace },
            new VectorRecord("dc-2", new float[] { 0, 1, 0 }, "Two") { Namespace = TestNamespace }
        });

        // Also insert into a different namespace to verify isolation
        await store.UpsertAsync(new VectorRecord("other-1", new float[] { 0, 0, 1 }, "Other") { Namespace = "other_namespace" });

        await store.DeleteByFilterAsync(new VectorFilter { Namespace = TestNamespace });

        var testResult = await store.GetAsync("dc-1", new VectorFilter { Namespace = TestNamespace });
        Assert.IsNull(testResult, "Record should not exist after namespace deletion");

        var otherResult = await store.GetAsync("other-1", new VectorFilter { Namespace = "other_namespace" });
        Assert.IsNotNull(otherResult, "Other namespace record should still exist");

        // Cleanup
        await store.DeleteByFilterAsync(new VectorFilter { Namespace = "other_namespace" });
    }

    #endregion

    #region Namespace Filter Tests

    [TestMethod]
    public async Task SearchWithNamespaceFilter_ReturnsOnlyMatchingNamespace()
    {
        SkipIfNoDb();
        using var store = CreateStore();
        await CleanNamespace(store);

        await store.UpsertAsync(new VectorRecord("exists-1", new float[] { 1, 0, 0 }, "Exists") { Namespace = TestNamespace });
        await store.UpsertAsync(new VectorRecord("other-1", new float[] { 1, 0, 0 }, "Other") { Namespace = "other_ns" });

        var results = await store.SearchAsync(new float[] { 1, 0, 0 }, topK: 10,
            filter: new VectorFilter { Namespace = TestNamespace });

        Assert.IsTrue(results.All(r => r.Record.Namespace == TestNamespace));

        // Cleanup
        await store.DeleteByFilterAsync(new VectorFilter { Namespace = "other_ns" });
    }

    #endregion

    #region VectorToString / ParseVectorString Unit Tests

    [TestMethod]
    public void VectorToString_FormatsCorrectly()
    {
        var result = PostgresStore.VectorToString(new float[] { 0.1f, 0.2f, 0.3f });
        Assert.AreEqual("[0.1,0.2,0.3]", result);
    }

    [TestMethod]
    public void ParseVectorString_ParsesCorrectly()
    {
        var result = PostgresStore.ParseVectorString("[0.1,0.2,0.3]");
        Assert.AreEqual(3, result.Length);
        Assert.AreEqual(0.1f, result[0], 0.001f);
        Assert.AreEqual(0.2f, result[1], 0.001f);
        Assert.AreEqual(0.3f, result[2], 0.001f);
    }

    [TestMethod]
    public void ParseVectorString_EmptyReturnsEmpty()
    {
        Assert.AreEqual(0, PostgresStore.ParseVectorString("").Length);
        Assert.AreEqual(0, PostgresStore.ParseVectorString("[]").Length);
    }

    [TestMethod]
    public void VectorRoundTrip_PreservesValues()
    {
        var original = new float[] { -1.5f, 0f, 3.14159f, 100.001f };
        var str = PostgresStore.VectorToString(original);
        var parsed = PostgresStore.ParseVectorString(str);

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
            new PostgresOptions { ConnectionString = "", Dimension = 3 }.Validate());
    }

    [TestMethod]
    public void Options_ZeroDimension_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PostgresOptions { ConnectionString = "Host=x", Dimension = 0 }.Validate());
    }

    [TestMethod]
    public void Options_InvalidTableName_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PostgresOptions { ConnectionString = "Host=x", Dimension = 3, TableName = "bad;name" }.Validate());
    }

    [TestMethod]
    public void Options_ValidOptions_DoesNotThrow()
    {
        new PostgresOptions
        {
            ConnectionString = "Host=localhost",
            Dimension = 1536,
            SchemaName = "public",
            TableName = "my_vectors"
        }.Validate();
    }

    [TestMethod]
    public void Options_ZeroIvfflatLists_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PostgresOptions
            {
                ConnectionString = "Host=x",
                Dimension = 3,
                Index = new IvfFlatIndexOptions
                {
                    Lists = 0
                }
            }.Validate());
    }

    [TestMethod]
    public void Options_ZeroHnswEfConstruction_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PostgresOptions
            {
                ConnectionString = "Host=x",
                Dimension = 3,
                Index = new HnswIndexOptions
                {
                    EfConstruction = 0
                }
            }.Validate());
    }

    [TestMethod]
    public void Options_ZeroHnswM_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PostgresOptions
            {
                ConnectionString = "Host=x",
                Dimension = 3,
                Index = new HnswIndexOptions
                {
                    M = 0
                }
            }.Validate());
    }

    [TestMethod]
    public void Options_ZeroIvfflatProbes_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PostgresOptions
            {
                ConnectionString = "Host=x",
                Dimension = 3,
                Index = new IvfFlatIndexOptions
                {
                    Probes = 0
                }
            }.Validate());
    }

    [TestMethod]
    public void Options_ZeroHnswEfSearch_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PostgresOptions
            {
                ConnectionString = "Host=x",
                Dimension = 3,
                Index = new HnswIndexOptions
                {
                    EfSearch = 0
                }
            }.Validate());
    }

    #endregion

    private static async Task CleanNamespace(PostgresStore store)
    {
        try { await store.DeleteByFilterAsync(new VectorFilter { Namespace = TestNamespace }); }
        catch { /* ignore if table doesn't exist yet */ }
    }
}
