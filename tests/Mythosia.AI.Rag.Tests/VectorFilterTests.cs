using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

/// <summary>
/// Unit tests for VectorFilter fluent API and all FilterOperator cases.
/// Uses InMemoryVectorStore so no external dependencies are required.
/// </summary>
[TestClass]
public class VectorFilterTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static readonly float[] AnyVector = new float[] { 1, 0, 0 };

    private static VectorRecord Record(string id, Dictionary<string, string> metadata, string? scope = null) =>
        new VectorRecord
        {
            Id = id,
            Vector = AnyVector,
            Content = id,
            Namespace = "test",
            Scope = scope,
            Metadata = metadata
        };

    private static async Task<InMemoryVectorStore> StoreWith(params VectorRecord[] records)
    {
        var store = new InMemoryVectorStore();
        await store.UpsertBatchAsync(records);
        return store;
    }

    private static async Task<List<string>> SearchIds(InMemoryVectorStore store, VectorFilter filter)
    {
        var results = await store.SearchAsync(AnyVector, topK: 100, filter: filter);
        return results.Select(r => r.Record.Id).ToList();
    }

    // ── Eq ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Eq_MatchesExactValue()
    {
        var store = await StoreWith(
            Record("a", new() { ["status"] = "active" }),
            Record("b", new() { ["status"] = "draft" }),
            Record("c", new() { ["status"] = "active" }));

        var filter = new VectorFilter().WithNamespace("test").Where("status", "active");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "c" }, ids);
    }

    [TestMethod]
    public async Task Eq_MissingKey_NotMatched()
    {
        var store = await StoreWith(
            Record("a", new() { ["status"] = "active" }),
            Record("b", new())); // no status key

        var filter = new VectorFilter().WithNamespace("test").Where("status", "active");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEqual(new[] { "a" }, ids);
    }

    // ── Ne ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Ne_ExcludesMatchingValue()
    {
        var store = await StoreWith(
            Record("a", new() { ["status"] = "active" }),
            Record("b", new() { ["status"] = "draft" }),
            Record("c", new() { ["status"] = "archived" }));

        var filter = new VectorFilter().WithNamespace("test").WhereNot("status", "draft");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "c" }, ids);
    }

    [TestMethod]
    public async Task Ne_MissingKey_NotMatched()
    {
        var store = await StoreWith(
            Record("a", new() { ["status"] = "active" }),
            Record("b", new())); // no status key — should not match Ne

        var filter = new VectorFilter().WithNamespace("test").WhereNot("status", "draft");
        var ids = await SearchIds(store, filter);

        // Only "a" has the key with a different value
        CollectionAssert.AreEqual(new[] { "a" }, ids);
    }

    // ── In ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task In_MatchesAnyValueInSet()
    {
        var store = await StoreWith(
            Record("a", new() { ["type"] = "doc" }),
            Record("b", new() { ["type"] = "faq" }),
            Record("c", new() { ["type"] = "news" }),
            Record("d", new() { ["type"] = "doc" }));

        var filter = new VectorFilter().WithNamespace("test").WhereIn("type", "doc", "faq");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "b", "d" }, ids);
    }

    [TestMethod]
    public async Task In_MissingKey_NotMatched()
    {
        var store = await StoreWith(
            Record("a", new() { ["type"] = "doc" }),
            Record("b", new())); // no type key

        var filter = new VectorFilter().WithNamespace("test").WhereIn("type", "doc", "faq");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEqual(new[] { "a" }, ids);
    }

    // ── NotIn ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task NotIn_ExcludesValuesInSet()
    {
        var store = await StoreWith(
            Record("a", new() { ["type"] = "doc" }),
            Record("b", new() { ["type"] = "faq" }),
            Record("c", new() { ["type"] = "news" }));

        var filter = new VectorFilter().WithNamespace("test").WhereNotIn("type", "doc", "faq");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEqual(new[] { "c" }, ids);
    }

    // ── Gt / Gte / Lt / Lte ────────────────────────────────────────────────

    [TestMethod]
    public async Task Gt_MatchesGreaterValues()
    {
        var store = await StoreWith(
            Record("a", new() { ["score"] = "50" }),
            Record("b", new() { ["score"] = "80" }),
            Record("c", new() { ["score"] = "30" }));

        // String ordinal comparison: "80" > "50" > "30"
        var filter = new VectorFilter().WithNamespace("test").WhereGreaterThan("score", "50");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEqual(new[] { "b" }, ids);
    }

    [TestMethod]
    public async Task Gte_MatchesGreaterOrEqualValues()
    {
        var store = await StoreWith(
            Record("a", new() { ["score"] = "50" }),
            Record("b", new() { ["score"] = "80" }),
            Record("c", new() { ["score"] = "30" }));

        var filter = new VectorFilter().WithNamespace("test").WhereGreaterThanOrEqual("score", "50");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "b" }, ids);
    }

    [TestMethod]
    public async Task Lt_MatchesLesserValues()
    {
        var store = await StoreWith(
            Record("a", new() { ["score"] = "50" }),
            Record("b", new() { ["score"] = "80" }),
            Record("c", new() { ["score"] = "30" }));

        var filter = new VectorFilter().WithNamespace("test").WhereLessThan("score", "50");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEqual(new[] { "c" }, ids);
    }

    [TestMethod]
    public async Task Lte_MatchesLesserOrEqualValues()
    {
        var store = await StoreWith(
            Record("a", new() { ["score"] = "50" }),
            Record("b", new() { ["score"] = "80" }),
            Record("c", new() { ["score"] = "30" }));

        var filter = new VectorFilter().WithNamespace("test").WhereLessThanOrEqual("score", "50");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "c" }, ids);
    }

    // ── Like ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Like_LeadingWildcard_MatchesSuffix()
    {
        var store = await StoreWith(
            Record("a", new() { ["path"] = "/sales/report.pdf" }),
            Record("b", new() { ["path"] = "/hr/report.docx" }),
            Record("c", new() { ["path"] = "/sales/summary.xlsx" }));

        var filter = new VectorFilter().WithNamespace("test").WhereLike("path", "/sales/%");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "c" }, ids);
    }

    [TestMethod]
    public async Task Like_TrailingWildcard_MatchesPrefix()
    {
        var store = await StoreWith(
            Record("a", new() { ["name"] = "report_2024" }),
            Record("b", new() { ["name"] = "summary_2024" }),
            Record("c", new() { ["name"] = "report_2023" }));

        var filter = new VectorFilter().WithNamespace("test").WhereLike("name", "report%");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "c" }, ids);
    }

    [TestMethod]
    public async Task Like_Underscore_MatchesSingleChar()
    {
        var store = await StoreWith(
            Record("a", new() { ["code"] = "A1" }),
            Record("b", new() { ["code"] = "A22" }),
            Record("c", new() { ["code"] = "B1" }));

        var filter = new VectorFilter().WithNamespace("test").WhereLike("code", "A_");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEqual(new[] { "a" }, ids);
    }

    [TestMethod]
    public async Task Like_MiddleWildcard_MatchesSubstring()
    {
        var store = await StoreWith(
            Record("a", new() { ["path"] = "/folder/2024/file.txt" }),
            Record("b", new() { ["path"] = "/folder/2023/file.txt" }),
            Record("c", new() { ["path"] = "/other/2024/doc.txt" }));

        var filter = new VectorFilter().WithNamespace("test").WhereLike("path", "/folder/%/file.txt");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "b" }, ids);
    }

    // ── Exists / NotExists ─────────────────────────────────────────────────

    [TestMethod]
    public async Task Exists_OnlyMatchesRecordsWithKey()
    {
        var store = await StoreWith(
            Record("a", new() { ["tag"] = "important" }),
            Record("b", new()),
            Record("c", new() { ["tag"] = "" }));

        var filter = new VectorFilter().WithNamespace("test").WhereExists("tag");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "c" }, ids);
    }

    [TestMethod]
    public async Task NotExists_OnlyMatchesRecordsWithoutKey()
    {
        var store = await StoreWith(
            Record("a", new() { ["tag"] = "important" }),
            Record("b", new()),
            Record("c", new() { ["other"] = "x" }));

        var filter = new VectorFilter().WithNamespace("test").WhereNotExists("tag");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "b", "c" }, ids);
    }

    // ── And grouping ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task TopLevel_MultipleConditions_AreAndCombined()
    {
        var store = await StoreWith(
            Record("a", new() { ["status"] = "active", ["type"] = "doc" }),
            Record("b", new() { ["status"] = "active", ["type"] = "faq" }),
            Record("c", new() { ["status"] = "draft",  ["type"] = "doc" }));

        var filter = new VectorFilter().WithNamespace("test")
            .Where("status", "active")
            .Where("type", "doc");

        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEqual(new[] { "a" }, ids);
    }

    [TestMethod]
    public async Task And_GroupInsideOr_ComposesCorrectly()
    {
        // Match: (storage=A AND folder=/sales/) OR storage=B
        var store = await StoreWith(
            Record("a", new() { ["storage"] = "A", ["folder"] = "/sales/" }),
            Record("b", new() { ["storage"] = "A", ["folder"] = "/hr/" }),
            Record("c", new() { ["storage"] = "B", ["folder"] = "/hr/" }),
            Record("d", new() { ["storage"] = "C", ["folder"] = "/sales/" }));

        var filter = new VectorFilter().WithNamespace("test")
            .Or(g => g
                .And(g2 => g2
                    .Where("storage", "A")
                    .Where("folder", "/sales/"))
                .Where("storage", "B"));

        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "c" }, ids);
    }

    // ── Or grouping ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Or_MatchesAnyCondition()
    {
        var store = await StoreWith(
            Record("a", new() { ["type"] = "doc" }),
            Record("b", new() { ["type"] = "faq" }),
            Record("c", new() { ["type"] = "news" }));

        var filter = new VectorFilter().WithNamespace("test")
            .Or(g => g.Where("type", "doc").Where("type", "news"));

        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "c" }, ids);
    }

    [TestMethod]
    public async Task Or_AllConditionsFail_NotMatched()
    {
        var store = await StoreWith(
            Record("a", new() { ["type"] = "other" }));

        var filter = new VectorFilter().WithNamespace("test")
            .Or(g => g.Where("type", "doc").Where("type", "faq"));

        var ids = await SearchIds(store, filter);

        Assert.AreEqual(0, ids.Count);
    }

    // ── Multi-tenant access pattern ────────────────────────────────────────

    [TestMethod]
    public async Task WhereIn_StorageIds_PermissionBasedSearch()
    {
        // Typical pattern: user has access to storage A and B, not C
        var store = await StoreWith(
            Record("a", new() { ["storage_id"] = "A", ["title"] = "Sales report" }),
            Record("b", new() { ["storage_id"] = "B", ["title"] = "HR policy" }),
            Record("c", new() { ["storage_id"] = "C", ["title"] = "Finance data" }),
            Record("d", new() { ["storage_id"] = "A", ["title"] = "Sales forecast" }));

        var filter = new VectorFilter().WithNamespace("test")
            .WhereIn("storage_id", "A", "B");

        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "b", "d" }, ids);
        CollectionAssert.DoesNotContain(ids, "c");
    }

    [TestMethod]
    public async Task PermissionWithFolderFilter_ComposedCorrectly()
    {
        // storage=A allowed, but only /sales/ folder; storage=B full access
        var store = await StoreWith(
            Record("a", new() { ["storage_id"] = "A", ["folder"] = "/sales/" }),
            Record("b", new() { ["storage_id"] = "A", ["folder"] = "/hr/" }),
            Record("c", new() { ["storage_id"] = "B", ["folder"] = "/hr/" }),
            Record("d", new() { ["storage_id"] = "C", ["folder"] = "/sales/" }));

        var filter = new VectorFilter().WithNamespace("test")
            .Or(g => g
                .And(inner => inner.Where("storage_id", "A").WhereLike("folder", "/sales/%"))
                .Where("storage_id", "B"));

        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "c" }, ids);
    }

    // ── Scope filtering ────────────────────────────────────────────────────

    [TestMethod]
    public async Task Scope_FiltersRecordsByScope()
    {
        var store = await StoreWith(
            Record("a", new() { ["x"] = "1" }, scope: "published"),
            Record("b", new() { ["x"] = "1" }, scope: "draft"),
            Record("c", new() { ["x"] = "1" }, scope: "published"));

        var filter = new VectorFilter { Namespace = "test", Scope = "published" };
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEquivalent(new[] { "a", "c" }, ids);
    }

    [TestMethod]
    public async Task Scope_CombinedWithCondition_BothApply()
    {
        var store = await StoreWith(
            Record("a", new() { ["type"] = "doc" }, scope: "published"),
            Record("b", new() { ["type"] = "doc" }, scope: "draft"),
            Record("c", new() { ["type"] = "faq" }, scope: "published"));

        var filter = new VectorFilter { Namespace = "test", Scope = "published" };
        filter.Where("type", "doc");
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEqual(new[] { "a" }, ids);
    }

    // ── Empty filter ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task EmptyConditions_MatchesAllRecords()
    {
        var store = await StoreWith(
            Record("a", new() { ["x"] = "1" }),
            Record("b", new() { ["x"] = "2" }),
            Record("c", new()));

        var filter = new VectorFilter { Namespace = "test" };
        var ids = await SearchIds(store, filter);

        Assert.AreEqual(3, ids.Count);
    }

    // ── AppendConditionsFrom (MergeStoreFilter simulation) ─────────────────

    [TestMethod]
    public async Task AppendConditionsFrom_AndCombinesTwoFilters()
    {
        // Simulate RagPipeline.MergeStoreFilter: storeFilter AND queryFilter
        var store = await StoreWith(
            Record("a", new() { ["storage_id"] = "A", ["status"] = "active" }),
            Record("b", new() { ["storage_id"] = "A", ["status"] = "draft" }),
            Record("c", new() { ["storage_id"] = "B", ["status"] = "active" }));

        // storeFilter: user can only access storage A
        var storeFilter = new VectorFilter().WhereIn("storage_id", "A");
        // queryFilter: only active status
        var queryFilter = new VectorFilter().Where("status", "active");

        var merged = new VectorFilter { Namespace = "test" };
        merged.AppendConditionsFrom(storeFilter);
        merged.AppendConditionsFrom(queryFilter);

        var ids = await SearchIds(store, merged);

        // Only record with storage=A AND status=active
        CollectionAssert.AreEqual(new[] { "a" }, ids);
    }

    [TestMethod]
    public async Task AppendConditionsFrom_ConflictingConditions_ResultIsEmpty()
    {
        // storeFilter says storage=A, queryFilter says storage=C — no results
        var store = await StoreWith(
            Record("a", new() { ["storage_id"] = "A" }),
            Record("b", new() { ["storage_id"] = "C" }));

        var merged = new VectorFilter { Namespace = "test" };
        merged.AppendConditionsFrom(new VectorFilter().WhereIn("storage_id", "A", "B"));
        merged.AppendConditionsFrom(new VectorFilter().Where("storage_id", "C"));

        var ids = await SearchIds(store, merged);

        Assert.AreEqual(0, ids.Count);
    }

    // ── WithoutMinScore (used in HybridSearch) ─────────────────────────────

    [TestMethod]
    public async Task MinScore_FiltersLowScoreResults()
    {
        // CosineSimilarity of [1,0,0] vs [1,0,0] = 1.0, vs [0,1,0] = 0.0
        var store = new InMemoryVectorStore();
        await store.UpsertBatchAsync(new[]
        {
            new VectorRecord { Id = "perfect",    Vector = new float[] { 1, 0, 0 }, Content = "perfect",    Namespace = "test" },
            new VectorRecord { Id = "orthogonal", Vector = new float[] { 0, 1, 0 }, Content = "orthogonal", Namespace = "test" },
        });

        var filter = new VectorFilter { Namespace = "test" };
        filter.WithMinScore(0.5);
        var results = await store.SearchAsync(AnyVector, topK: 10, filter: filter);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("perfect", results[0].Record.Id);
    }

    // ── Fluent chaining correctness ────────────────────────────────────────

    [TestMethod]
    public void FluentChaining_ReturnsThisInstance()
    {
        var filter = new VectorFilter();
        var result = filter
            .Where("a", "1")
            .WhereNot("b", "2")
            .WhereIn("c", "x", "y")
            .WhereNotIn("d", "z")
            .WhereGreaterThan("e", "5")
            .WhereLessThan("f", "9")
            .WhereExists("g")
            .WhereNotExists("h")
            .WhereLike("i", "%pattern%")
            .WithNamespace("ns")
            .WithMinScore(0.7);

        Assert.AreSame(filter, result);
        Assert.AreEqual(9, filter.Conditions.Count);
        Assert.AreEqual("ns", filter.Namespace);
        Assert.AreEqual(0.7, filter.MinScore);
    }

    [TestMethod]
    public void Or_EmptyGroup_NotAddedToConditions()
    {
        var filter = new VectorFilter().Or(g => { /* nothing */ });
        Assert.AreEqual(0, filter.Conditions.Count);
    }

    [TestMethod]
    public void And_EmptyGroup_NotAddedToConditions()
    {
        var filter = new VectorFilter().And(g => { /* nothing */ });
        Assert.AreEqual(0, filter.Conditions.Count);
    }

    // ── DeleteByFilter integration ─────────────────────────────────────────

    [TestMethod]
    public async Task DeleteByFilter_RemovesOnlyMatchingRecords()
    {
        var store = await StoreWith(
            Record("a", new() { ["type"] = "doc" }),
            Record("b", new() { ["type"] = "faq" }),
            Record("c", new() { ["type"] = "doc" }));

        var deleteFilter = new VectorFilter { Namespace = "test" };
        deleteFilter.Where("type", "doc");
        await store.DeleteByFilterAsync(deleteFilter);

        var filter = new VectorFilter { Namespace = "test" };
        var ids = await SearchIds(store, filter);

        CollectionAssert.AreEqual(new[] { "b" }, ids);
    }

    // ── CountAsync integration ─────────────────────────────────────────────

    [TestMethod]
    public async Task CountAsync_WithFilter_CountsOnlyMatching()
    {
        var store = await StoreWith(
            Record("a", new() { ["status"] = "active" }),
            Record("b", new() { ["status"] = "draft" }),
            Record("c", new() { ["status"] = "active" }));

        var filter = new VectorFilter { Namespace = "test" };
        filter.Where("status", "active");
        var count = await store.CountAsync(filter);

        Assert.AreEqual(2L, count);
    }
}
