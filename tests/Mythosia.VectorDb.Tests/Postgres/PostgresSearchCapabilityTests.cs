using Mythosia.VectorDb.Postgres;
using Npgsql;

namespace Mythosia.VectorDb.Tests.Postgres;

/// <summary>Opt-in database checks using one disposable table per test.</summary>
[TestClass]
[TestCategory("Integration")]
public class PostgresSearchCapabilityTests
{
    [TestMethod]
    public async Task TextSearch_PunctuationIsData_AndTermsKeepOrSemantics()
    {
        await using var fixture = new Fixture();
        await fixture.Store.UpsertBatchAsync(new[] { Record("hello", "hello"), Record("world", "world") });
        foreach (var query in new[] { "hello !", "hello |", "hello &", "hello :*", "'hello'", "hello \\" })
        {
            var result = await fixture.Store.TextSearchAsync(query);
            Assert.IsTrue(result.Any(item => item.Record.Id == "hello"), query);
        }
        foreach (var query in new[] { "", " ", "!!!", "| & :* ' \\" })
            Assert.HasCount(0, await fixture.Store.TextSearchAsync(query), query);
        var or = await fixture.Store.TextSearchAsync("hello world !");
        CollectionAssert.AreEquivalent(new[] { "hello", "world" }, or.Select(item => item.Record.Id).ToArray());
    }

    [TestMethod]
    public async Task TextSearch_UsesIndexConfigurationAndScriptBoundaryNormalization()
    {
        await using var fixture = new Fixture("english");
        await fixture.Store.UpsertBatchAsync(new[] { Record("english", "relational databases"), Record("mixed", "Vector검색") });
        Assert.AreEqual("english", (await fixture.Store.TextSearchAsync("relational"))[0].Record.Id);
        Assert.IsTrue((await fixture.Store.TextSearchAsync("Vector검색")).Any(item => item.Record.Id == "mixed"));
    }

    [TestMethod]
    public async Task TextAndHybrid_RestrictCandidatesBeforeTopK_AndRetainMetadata()
    {
        await using var fixture = new Fixture();
        var records = Enumerable.Range(0, 20).Select(i => Record("foreign-" + i, "refund refund refund", "other")).ToList();
        records.Add(Record("owned", "refund policy with a valid receipt", "mine"));
        await fixture.Store.UpsertBatchAsync(records);
        var filter = new VectorFilter().Where("tenant", "mine");
        var text = await fixture.Store.TextSearchAsync("refund", 1, filter);
        Assert.HasCount(1, text);
        Assert.AreEqual("owned", text[0].Record.Id);
        Assert.AreEqual("policy.md", text[0].Record.Metadata["source"]);
        CollectionAssert.AreEqual(new[] { 1f, 0f }, text[0].Record.Vector);
        Assert.HasCount(0, await fixture.Store.TextSearchAsync("refund", 1, filter.WithMinScore(text[0].Score + 1)));
        var hybrid = await fixture.Store.HybridSearchAsync(new[] { 1f, 0f }, "refund",
            new HybridSearchOptions(), 1, new VectorFilter().Where("tenant", "mine"));
        Assert.AreEqual("owned", hybrid[0].Record.Id);
    }

    [TestMethod]
    public async Task Hybrid_WeightsChangeRanking_AndThresholdIsAppliedAfterFusion()
    {
        await using var fixture = new Fixture();
        await fixture.Store.UpsertBatchAsync(new[] { Record("dense", "different"), new VectorRecord("text", new[] { 0f, 1f }, "needle") });
        var dense = await fixture.Store.HybridSearchAsync(new[] { 1f, 0f }, "needle",
            new HybridSearchOptions { VectorWeight = .9f, RrfK = 1 });
        var text = await fixture.Store.HybridSearchAsync(new[] { 1f, 0f }, "needle",
            new HybridSearchOptions { VectorWeight = .1f, RrfK = 1 });
        Assert.AreEqual("dense", dense[0].Record.Id);
        Assert.AreEqual("text", text[0].Record.Id);
        Assert.AreEqual(.9, dense[0].Score, 1e-6);
        var threshold = await fixture.Store.HybridSearchAsync(new[] { 1f, 0f }, "needle",
            new HybridSearchOptions { VectorWeight = .9f, RrfK = 1 }, filter: new VectorFilter().WithMinScore(.8));
        Assert.HasCount(1, threshold);
        Assert.AreEqual("dense", threshold[0].Record.Id);
    }

    [TestMethod]
    public async Task Hybrid_EndpointWeightsSkipInactiveLeg_AndEmptyTextKeepsScoreScale()
    {
        await using var fixture = new Fixture();
        await fixture.Store.UpsertAsync(Record("hello", "hello"));
        Assert.HasCount(0, await fixture.Store.HybridSearchAsync(null!, "absent", new HybridSearchOptions { VectorWeight = 0 }));
        Assert.AreEqual(1.0, (await fixture.Store.HybridSearchAsync(null!, "hello", new HybridSearchOptions { VectorWeight = 0 }))[0].Score, 1e-10);
        Assert.AreEqual(1.0, (await fixture.Store.HybridSearchAsync(new[] { 1f, 0f }, null!, new HybridSearchOptions { VectorWeight = 1 }))[0].Score, 1e-10);
        Assert.AreEqual(.5, (await fixture.Store.HybridSearchAsync(new[] { 1f, 0f }, "!", 5))[0].Score, 1e-10);
    }

    [TestMethod]
    public async Task Hybrid_DeterministicTies_AndLargeRrfConstant()
    {
        await using var fixture = new Fixture();
        await fixture.Store.UpsertBatchAsync(new[] { Record("z", "identical content"), Record("a", "identical content") });
        Assert.AreEqual("a", (await fixture.Store.TextSearchAsync("identical", 1))[0].Record.Id);
        Assert.AreEqual("a", (await fixture.Store.SearchAsync(new[] { 1f, 0f }, 1))[0].Record.Id);
        var hybrid = await fixture.Store.HybridSearchAsync(new[] { 1f, 0f }, "identical", new HybridSearchOptions { RrfK = int.MaxValue }, 1);
        Assert.AreEqual("a", hybrid[0].Record.Id);
        Assert.AreEqual(1.0, hybrid[0].Score, 1e-10);
    }

    [TestMethod]
    public async Task TrigramTextSearch_HonorsFilterAndThreshold()
    {
        await using var fixture = new Fixture(mode: TextSearchMode.Trigram);
        await fixture.Store.UpsertBatchAsync(new[] { Record("mine", "refund policy", "mine"), Record("other", "refund", "other") });
        var result = await fixture.Store.TextSearchAsync("refund", 1, new VectorFilter().Where("tenant", "mine"));
        Assert.HasCount(1, result);
        Assert.AreEqual("mine", result[0].Record.Id);
        Assert.HasCount(0, await fixture.Store.TextSearchAsync("refund", 1, new VectorFilter().WithMinScore(2)));
    }

    [TestMethod]
    public async Task EmptyNotInSet_ExcludesMissingKeysAcrossTextAndHybridSearch()
    {
        await using var fixture = new Fixture();
        await fixture.Store.UpsertBatchAsync(new[]
        {
            Record("owned", "refund policy", "mine"),
            new VectorRecord("missing", new[] { 1f, 0f }, "refund policy")
        });
        var filter = new VectorFilter().WhereNotIn("tenant");
        Assert.AreEqual("owned", (await fixture.Store.TextSearchAsync("refund", filter: filter)).Single().Record.Id);
        foreach (float weight in new[] { 0f, .5f, 1f })
        {
            var results = await fixture.Store.HybridSearchAsync(new[] { 1f, 0f }, "refund",
                new HybridSearchOptions { VectorWeight = weight }, filter: filter);
            Assert.AreEqual("owned", results.Single().Record.Id, $"VectorWeight={weight}");
        }
        Assert.AreEqual("owned", (await fixture.Store.SearchAsync(new[] { 1f, 0f }, filter: filter)).Single().Record.Id);
        Assert.AreEqual(1L, await fixture.Store.CountAsync(filter));
    }

    private static VectorRecord Record(string id, string content, string tenant = "mine")
        => new VectorRecord(id, new[] { 1f, 0f }, content) { Metadata = { ["tenant"] = tenant, ["source"] = "policy.md" } };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _table = "retrieval_contract_" + Guid.NewGuid().ToString("N");
        public PostgresStore Store { get; }

        public Fixture(string configuration = "simple", TextSearchMode mode = TextSearchMode.TsVector)
        {
            _connectionString = Environment.GetEnvironmentVariable("MYTHOSIA_PG_CONN") ?? "";
            if (string.IsNullOrWhiteSpace(_connectionString)) Assert.Inconclusive("Set MYTHOSIA_PG_CONN to run PostgreSQL integration checks.");
            Store = new PostgresStore(new PostgresOptions
            {
                ConnectionString = _connectionString, Dimension = 2, TableName = _table, EnsureSchema = true,
                TextSearchConfig = configuration, TextSearchMode = mode
            });
        }

        public async ValueTask DisposeAsync()
        {
            Store.Dispose();
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE IF EXISTS public.\"{_table}\"";
            await command.ExecuteNonQueryAsync();
        }
    }
}
