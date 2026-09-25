using System.Text.Json;
using Mythosia.VectorDb.Postgres;
using Npgsql;

namespace Mythosia.VectorDb.Tests.Postgres;

/// <summary>Opt-in checks of runtime settings inside actual PostgreSQL search transactions.</summary>
[TestClass]
[TestCategory("Integration")]
public class PostgresHybridRuntimeSettingsTests
{
    [TestMethod]
    [DataRow(TextSearchMode.TsVector)]
    [DataRow(TextSearchMode.Trigram)]
    public async Task Hybrid_UsesConfiguredHnswEfSearch_WhenBothLegsAreActive(TextSearchMode mode)
    {
        await using var fixture = new Fixture(new HnswIndexOptions { EfSearch = 120 }, mode);
        await fixture.Store.UpsertBatchAsync(Enumerable.Range(0, 150)
            .Select(i => new VectorRecord($"v{i:D3}", new[] { 1f, (i + 1) / 1000f, .01f }, "hotel")));
        await fixture.ExecuteAsync($"ANALYZE public.\"{fixture.Table}\"");
        await fixture.AssertHnswPlanAsync();

        var query = new[] { 1f, 0f, 0f };
        Assert.HasCount(0, await fixture.Store.TextSearchAsync("nomatchingword", 100));
        var normal = await fixture.Store.SearchAsync(query, 50);
        var mixed = await fixture.Store.HybridSearchAsync(query, "nomatchingword", 50);
        var vectorOnly = await fixture.Store.HybridSearchAsync(query, null!,
            new HybridSearchOptions { VectorWeight = 1 }, 50);

        Assert.HasCount(50, normal);
        Assert.HasCount(50, mixed, "Mixed hybrid search must honor configured EfSearch instead of the connection's default 40.");
        CollectionAssert.AreEqual(normal.Select(r => r.Record.Id).ToArray(), mixed.Select(r => r.Record.Id).ToArray());
        CollectionAssert.AreEqual(normal.Select(r => r.Record.Id).ToArray(), vectorOnly.Select(r => r.Record.Id).ToArray());
    }

    [TestMethod]
    public async Task Hybrid_UsesConfiguredIvfflatProbes_InItsOwnTransaction()
    {
        await using var fixture = new Fixture(new NoIndexOptions());
        await fixture.Store.UpsertAsync(new VectorRecord("hotel", new[] { 1f, 0f, 0f }, "hotel"));
        // Observe the setting from the SELECT that reads each record, not from an unrelated connection.
        // A view avoids relying on random IVFFlat training to expose an incorrect probe count.
        await fixture.ExecuteAsync($"""
            CREATE VIEW public."{fixture.SettingsView}" AS
            SELECT id, current_setting('ivfflat.probes') AS content, content_tsv,
                   metadata, embedding, created_at, updated_at
            FROM public."{fixture.Table}"
            """);
        using var store = fixture.CreateStore(new IvfFlatIndexOptions { Probes = 7 }, fixture.SettingsView);
        var query = new[] { 1f, 0f, 0f };

        Assert.AreEqual("7", (await store.SearchAsync(query)).Single().Record.Content);
        Assert.AreEqual("7", (await store.HybridSearchAsync(query, "nomatchingword", 5)).Single().Record.Content);
        Assert.AreEqual("7", (await store.HybridSearchAsync(query, null!,
            new HybridSearchOptions { VectorWeight = 1 })).Single().Record.Content);
        Assert.AreEqual("1", (await store.HybridSearchAsync(null!, "hotel",
            new HybridSearchOptions { VectorWeight = 0 })).Single().Record.Content,
            "An inactive vector leg must not apply vector settings, and SET LOCAL must not leak after commit.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly TextSearchMode _mode;
        public string Table { get; } = "hybrid_runtime_" + Guid.NewGuid().ToString("N");
        public string SettingsView => Table + "_settings";
        public PostgresStore Store { get; }

        public Fixture(VectorIndexOptions index, TextSearchMode mode = TextSearchMode.TsVector)
        {
            var connectionString = Environment.GetEnvironmentVariable("MYTHOSIA_PG_CONN");
            if (string.IsNullOrWhiteSpace(connectionString))
                Assert.Inconclusive("Set MYTHOSIA_PG_CONN to run PostgreSQL integration checks.");
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            builder.Options = (builder.Options + " -c enable_seqscan=off -c enable_bitmapscan=off -c enable_sort=off"
                + " -c hnsw.ef_search=40 -c hnsw.iterative_scan=off -c ivfflat.probes=1").Trim();
            _connectionString = builder.ConnectionString;
            _mode = mode;
            Store = CreateStore(index, Table, ensureSchema: true);
        }

        public PostgresStore CreateStore(VectorIndexOptions index, string table, bool ensureSchema = false)
            => new(new PostgresOptions
            {
                ConnectionString = _connectionString, Dimension = 3, TableName = table,
                EnsureSchema = ensureSchema, Index = index, TextSearchMode = _mode
            });

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task AssertHnswPlanAsync()
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""
                EXPLAIN (ANALYZE, FORMAT JSON)
                SELECT id FROM public."{Table}"
                ORDER BY embedding <=> '[1,0,0]'::vector, id COLLATE "C"
                LIMIT 100
                """, connection);
            var plan = (string)(await command.ExecuteScalarAsync())!;
            Console.WriteLine(plan);
            using var document = JsonDocument.Parse(plan);
            Assert.IsTrue(UsesIndex(document.RootElement[0].GetProperty("Plan"), "idx_" + Table + "_embedding"),
                "The regression must exercise the HNSW index, not an exact sequential scan.");
        }

        private static bool UsesIndex(JsonElement node, string name)
        {
            if (node.TryGetProperty("Index Name", out var index) && index.GetString() == name) return true;
            return node.TryGetProperty("Plans", out var children) && children.EnumerateArray().Any(child => UsesIndex(child, name));
        }

        public async ValueTask DisposeAsync()
        {
            Store.Dispose();
            await ExecuteAsync($"DROP VIEW IF EXISTS public.\"{SettingsView}\"; DROP TABLE IF EXISTS public.\"{Table}\"");
        }
    }
}
