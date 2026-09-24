using System.Reflection;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.VectorDb.Tests.InMemory;

[TestClass]
public class InMemoryConsistencyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow("get")]
    [DataRow("batch")]
    [DataRow("list")]
    [DataRow("count")]
    [DataRow("total")]
    [DataRow("vector")]
    [DataRow("scored")]
    public async Task Upsert_DoesNotPublishRecordBeforeKeywordIndexIsReady(string readKind)
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("same", "alpha"));

        var schedule = PauseIndexCommit(store,
            () => store.UpsertAsync(Record("same", "beta")),
            () => ObserveAsync(store, readKind));

        await Task.WhenAll(schedule.First, schedule.Second).WaitAsync(Timeout);
        Assert.IsFalse(schedule.SecondCompletedWhileIndexPaused,
            $"{readKind} exposed a record while its keyword index had not committed.");
        await AssertCoherentAsync(store);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Delete_DoesNotPublishRecordRemovalBeforeKeywordIndexIsReady(bool byFilter)
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("same", "alpha"));
        var schedule = PauseIndexCommit(store,
            () => byFilter
                ? store.DeleteByFilterAsync(new VectorFilter().Where("tenant", "mine"))
                : store.DeleteAsync("same"),
            () => ObserveAsync(store, "count"));

        await Task.WhenAll(schedule.First, schedule.Second).WaitAsync(Timeout);
        Assert.IsFalse(schedule.SecondCompletedWhileIndexPaused,
            "Deletion became visible before its keyword-index removal committed.");
        await AssertCoherentAsync(store);
        Assert.HasCount(0, await store.ListAllRecordsAsync());
    }

    [TestMethod]
    [DataRow("upsert")]
    [DataRow("delete")]
    [DataRow("filtered-delete")]
    [DataRow("delete-by-filter")]
    public async Task ConcurrentWrites_KeepRecordAndKeywordIndexAtTheSameRevision(string firstKind)
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("same", "alpha"));
        var schedule = PauseIndexCommit(store,
            () => firstKind switch
            {
                "upsert" => store.UpsertAsync(Record("same", "gamma")),
                "delete" => store.DeleteAsync("same"),
                "filtered-delete" => store.DeleteAsync("same", new VectorFilter().Where("tenant", "mine")),
                _ => store.DeleteByFilterAsync(new VectorFilter().Where("tenant", "mine"))
            },
            () => store.UpsertAsync(Record("same", "beta", "other")));

        await Task.WhenAll(schedule.First, schedule.Second).WaitAsync(Timeout);
        await AssertCoherentAsync(store);
        // The second update entered after the first operation had reached its commit.
        var stored = await store.GetAsync("same");
        Assert.IsNotNull(stored);
        Assert.AreEqual("beta", stored.Content);
        Assert.AreEqual("other", stored.Metadata["tenant"]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KeywordAndHybridReads_WaitForRecordAndIndexToAgree(bool hybrid)
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("same", "alpha"));
        IReadOnlyList<VectorSearchResult>? result = null;
        var schedule = PauseIndexCommit(store,
            () => store.UpsertAsync(Record("same", "beta")),
            async () => result = hybrid
                ? await store.HybridSearchAsync(new[] { 1f, 0f }, "beta")
                : await store.TextSearchAsync("beta"));

        await Task.WhenAll(schedule.First, schedule.Second).WaitAsync(Timeout);
        Assert.IsFalse(schedule.SecondCompletedWhileIndexPaused);
        Assert.IsNotNull(result);
        Assert.HasCount(1, result);
        Assert.AreEqual("beta", result[0].Record.Content);
        if (hybrid)
            Assert.AreEqual(1d, result[0].Score, 1e-10,
                "Both hybrid legs must observe the committed revision.");
    }

    [TestMethod]
    public async Task Hybrid_DoesNotMixDenseAndKeywordResultsFromDifferentRevisions()
    {
        using var store = new InMemoryVectorStore();
        using var comparer = new PausingMetadataComparer();
        var alpha = Record("same", "alpha");
        alpha.Metadata = new Dictionary<string, string>(comparer) { ["tenant"] = "mine" };
        await store.UpsertAsync(alpha);

        // Pause the first metadata lookup after the dense leg has captured its
        // candidate records. The comparer changes scheduling, never key equality.
        comparer.PauseNextLookup();
        var search = Task.Run(() => store.HybridSearchAsync(new[] { 1f, 0f }, "beta",
            filter: new VectorFilter().Where("tenant", "mine")));
        Task? update = null;
        try
        {
            Assert.IsTrue(comparer.Entered.Wait(Timeout), "Dense search did not reach its candidate filter.");
            update = Task.Run(() => store.UpsertAsync(Record("same", "beta")));
            // Without a whole-search gate the update commits here, so the later
            // keyword leg sees beta while dense ranking still contains alpha.
            // With the gate it waits until the search's coherent snapshot ends.
            update.Wait(TimeSpan.FromSeconds(2));
        }
        finally { comparer.Release.Set(); }

        var results = await search.WaitAsync(Timeout);
        if (update != null) await update.WaitAsync(Timeout);
        Assert.HasCount(1, results);
        Assert.AreEqual("alpha", results[0].Record.Content);
        Assert.AreEqual(.5d, results[0].Score, 1e-10,
            "A keyword match from the new beta revision must not raise the old alpha revision's score.");
        await AssertCoherentAsync(store);
    }

    [TestMethod]
    public async Task ConcurrentIndependentDocuments_KeepSearchAndFilteredDeletionConsistent()
    {
        using var store = new InMemoryVectorStore();
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 12).Select(i => Task.Run(async () =>
        {
            if (!start.Wait(Timeout)) throw new TimeoutException();
            var id = "doc-" + i;
            var tenant = i % 2 == 0 ? "remove" : "keep";
            await store.UpsertAsync(Record(id, "alpha", tenant));
            await store.UpsertAsync(Record(id, "beta", tenant));
            if (i % 2 == 0)
                await store.DeleteAsync(id, new VectorFilter().Where("tenant", "remove"));
        })).ToArray();
        start.Set();
        await Task.WhenAll(tasks).WaitAsync(Timeout);

        var records = await store.ListAllRecordsAsync();
        Assert.HasCount(6, records);
        Assert.IsTrue(records.All(r => r.Content == "beta" && r.Metadata["tenant"] == "keep"));
        Assert.HasCount(0, await store.TextSearchAsync("alpha", 20));
        var beta = await store.TextSearchAsync("beta", 20);
        CollectionAssert.AreEquivalent(records.Select(r => r.Id).ToArray(), beta.Select(r => r.Record.Id).ToArray());
        Assert.HasCount(0, await store.TextSearchAsync("beta", 20, new VectorFilter().Where("tenant", "remove")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Upsert_SnapshotsTheRecordVectorAndMetadata(bool batch)
    {
        using var store = new InMemoryVectorStore();
        var input = Record("same", "alpha");
        if (batch) await store.UpsertBatchAsync(new[] { input });
        else await store.UpsertAsync(input);

        Corrupt(input);

        await AssertOriginalSnapshotAsync(store);
    }

    [TestMethod]
    [DataRow("get")]
    [DataRow("batch")]
    [DataRow("list")]
    [DataRow("vector")]
    [DataRow("scored")]
    [DataRow("text")]
    [DataRow("hybrid")]
    public async Task Retrieval_ReturnsIndependentRecordVectorAndMetadata(string readKind)
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("same", "alpha"));
        var returned = readKind switch
        {
            "get" => (await store.GetAsync("same"))!,
            "batch" => (await store.GetBatchAsync(new[] { "same" }))[0],
            "list" => (await store.ListAllRecordsAsync())[0],
            "vector" => (await store.SearchAsync(new[] { 1f, 0f }))[0].Record,
            "scored" => (await store.ScoredListAsync(new[] { 1f, 0f }))[0].Record,
            "text" => (await store.TextSearchAsync("alpha"))[0].Record,
            _ => (await store.HybridSearchAsync(new[] { 1f, 0f }, "alpha"))[0].Record
        };

        Corrupt(returned);

        await AssertOriginalSnapshotAsync(store);
    }

    [TestMethod]
    public async Task BatchGet_DuplicateIdsReturnIndependentSnapshots()
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("same", "alpha"));
        var returned = await store.GetBatchAsync(new[] { "same", "same" });
        Assert.HasCount(2, returned);
        Corrupt(returned[0]);
        Assert.AreEqual("alpha", returned[1].Content);
        Assert.AreEqual("mine", returned[1].Metadata["tenant"]);
        CollectionAssert.AreEqual(new[] { 1f, 0f }, returned[1].Vector);
        await AssertOriginalSnapshotAsync(store);
    }

    [TestMethod]
    [DataRow("upsert")]
    [DataRow("batch-upsert")]
    [DataRow("delete")]
    [DataRow("delete-by-filter")]
    [DataRow("get")]
    [DataRow("batch")]
    [DataRow("list")]
    [DataRow("count")]
    [DataRow("vector")]
    [DataRow("scored")]
    [DataRow("text")]
    [DataRow("hybrid")]
    public async Task Operations_ObservePreCancellationWithoutChangingState(string operation)
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("same", "alpha"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            switch (operation)
            {
                case "upsert": await store.UpsertAsync(Record("same", "beta"), cts.Token); break;
                case "batch-upsert": await store.UpsertBatchAsync(new[] { Record("same", "beta") }, cts.Token); break;
                case "delete": await store.DeleteAsync("same", cancellationToken: cts.Token); break;
                case "delete-by-filter": await store.DeleteByFilterAsync(new VectorFilter(), cts.Token); break;
                default: await ObserveAsync(store, operation, cts.Token); break;
            }
        });
        await AssertOriginalSnapshotAsync(store);
    }

    [TestMethod]
    public async Task Upsert_CancelledWhileWaitingForAnotherCommitDoesNotChangeState()
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("same", "alpha"));
        using var cts = new CancellationTokenSource();
        var schedule = PauseIndexCommit(store,
            () => store.UpsertAsync(Record("same", "beta")),
            () => store.UpsertAsync(Record("same", "gamma"), cts.Token),
            cts.Cancel);

        await schedule.First.WaitAsync(Timeout);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => schedule.Second.WaitAsync(Timeout));
        Assert.AreEqual("beta", (await store.GetAsync("same"))!.Content);
        await AssertCoherentAsync(store);
    }

    [TestMethod]
    public async Task IndexFailure_DoesNotPublishAnUnindexedRecord()
    {
        using var store = new InMemoryVectorStore();
        // A Lucene term cannot exceed 32,766 UTF-8 bytes. Failure occurs in the real
        // keyword index, after ordinary record construction and argument validation.
        var invalid = Record(new string('x', 33_000), "alpha");
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync(invalid));
        Assert.IsNull(await store.GetAsync(invalid.Id));
        Assert.AreEqual(0L, await store.CountAsync());
        Assert.HasCount(0, await store.TextSearchAsync("alpha"));

        await store.UpsertAsync(Record("same", "beta"));
        await AssertCoherentAsync(store);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InterruptedBatch_PreservesCompletedRecordPairs(bool cancellation)
    {
        using var store = new InMemoryVectorStore();
        await store.UpsertAsync(Record("same", "alpha"));
        using var cts = new CancellationTokenSource();
        IEnumerable<VectorRecord> Incoming()
        {
            yield return Record("same", "beta");
            if (cancellation) cts.Cancel();
            else throw new InvalidOperationException("Controlled enumeration failure.");
            yield return Record("same", "gamma");
        }

        if (cancellation)
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.UpsertBatchAsync(Incoming(), cts.Token));
        else
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.UpsertBatchAsync(Incoming()));

        Assert.AreEqual("beta", (await store.GetAsync("same"))!.Content);
        await AssertCoherentAsync(store);
    }

    [TestMethod]
    public async Task MetadataSnapshots_PreserveTheCallersKeyComparer()
    {
        using var store = new InMemoryVectorStore();
        var record = Record("same", "alpha");
        record.Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TENANT"] = "mine"
        };
        await store.UpsertAsync(record);

        var returned = await store.GetAsync("same", new VectorFilter().Where("tenant", "mine"));
        Assert.IsNotNull(returned);
        Assert.AreEqual("mine", returned.Metadata["tenant"]);
        Assert.HasCount(1, await store.TextSearchAsync("alpha", filter: new VectorFilter().Where("tenant", "mine")));
        returned.Metadata["tenant"] = "other";
        Assert.AreEqual("mine", (await store.GetAsync("same"))!.Metadata["tenant"]);
    }

    [TestMethod]
    public async Task Dispose_WaitsForAnInFlightCommitAndIsIdempotent()
    {
        var store = new InMemoryVectorStore();
        try
        {
            var schedule = PauseIndexCommit(store,
                () => store.UpsertAsync(Record("same", "alpha")),
                () => { store.Dispose(); return Task.CompletedTask; });
            await Task.WhenAll(schedule.First, schedule.Second).WaitAsync(Timeout);
            Assert.IsFalse(schedule.SecondCompletedWhileIndexPaused);
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => store.GetAsync("same"));
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => store.UpsertAsync(Record("same", "beta")));
        }
        finally { store.Dispose(); }
    }

    private static async Task ObserveAsync(InMemoryVectorStore store, string kind, CancellationToken ct = default)
    {
        switch (kind)
        {
            case "get": await store.GetAsync("same", cancellationToken: ct); break;
            case "batch": await store.GetBatchAsync(new[] { "same" }, cancellationToken: ct); break;
            case "list": await store.ListAllRecordsAsync(ct); break;
            case "count": await store.CountAsync(cancellationToken: ct); break;
            case "total": store.GetTotalRecordCount(); break;
            case "vector": await store.SearchAsync(new[] { 1f, 0f }, cancellationToken: ct); break;
            case "scored": await store.ScoredListAsync(new[] { 1f, 0f }, ct); break;
            case "text": await store.TextSearchAsync("alpha", cancellationToken: ct); break;
            case "hybrid": await store.HybridSearchAsync(new[] { 1f, 0f }, "alpha", cancellationToken: ct); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static async Task AssertOriginalSnapshotAsync(InMemoryVectorStore store)
    {
        var stored = await store.GetAsync("same");
        Assert.IsNotNull(stored);
        Assert.AreEqual("same", stored.Id);
        Assert.AreEqual("alpha", stored.Content);
        CollectionAssert.AreEqual(new[] { 1f, 0f }, stored.Vector);
        Assert.AreEqual("mine", stored.Metadata["tenant"]);
        Assert.AreEqual(1, stored.Metadata.Count);
        Assert.HasCount(0, await store.TextSearchAsync("beta"));
        var alpha = await store.TextSearchAsync("alpha", filter: new VectorFilter().Where("tenant", "mine"));
        Assert.HasCount(1, alpha);
        Assert.AreEqual("alpha", alpha[0].Record.Content);
        Assert.AreEqual(1d, (await store.SearchAsync(new[] { 1f, 0f }))[0].Score, 1e-10);
    }

    private static async Task AssertCoherentAsync(InMemoryVectorStore store)
    {
        var stored = await store.GetAsync("same");
        foreach (var word in new[] { "alpha", "beta", "gamma" })
        {
            var text = await store.TextSearchAsync(word);
            var textOnlyHybrid = await store.HybridSearchAsync(Array.Empty<float>(), word,
                new HybridSearchOptions { VectorWeight = 0 });
            var expected = stored?.Content == word ? 1 : 0;
            Assert.HasCount(expected, text, $"Keyword '{word}' disagrees with stored content '{stored?.Content}'.");
            Assert.HasCount(expected, textOnlyHybrid, $"Hybrid keyword '{word}' disagrees with stored content.");
            if (expected == 1)
            {
                Assert.AreEqual(stored!.Content, text[0].Record.Content);
                Assert.AreEqual(stored.Metadata["tenant"], text[0].Record.Metadata["tenant"]);
                Assert.IsTrue(double.IsFinite(text[0].Score));
            }
        }
        Assert.AreEqual(stored == null ? 0L : 1L, await store.CountAsync());
    }

    private static void Corrupt(VectorRecord record)
    {
        record.Id = "different";
        record.Content = "beta";
        record.Vector[0] = 0;
        record.Vector[1] = 1;
        record.Metadata["tenant"] = "other";
        record.Metadata["injected"] = "value";
    }

    private static VectorRecord Record(string id, string text, string tenant = "mine")
        => new(id, new[] { 1f, 0f }, text) { Metadata = { ["tenant"] = tenant } };

    private sealed record Schedule(Task First, Task Second, bool SecondCompletedWhileIndexPaused);

    private sealed class PausingMetadataComparer : IEqualityComparer<string>, IDisposable
    {
        private int _pauseNext;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public void PauseNextLookup() => Interlocked.Exchange(ref _pauseNext, 1);
        public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y);
        public int GetHashCode(string value)
        {
            if (Interlocked.Exchange(ref _pauseNext, 0) == 1)
            {
                Entered.Set();
                if (!Release.Wait(Timeout)) throw new TimeoutException("Paused metadata lookup was not released.");
            }
            return StringComparer.Ordinal.GetHashCode(value);
        }

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }

    private static Schedule PauseIndexCommit(InMemoryVectorStore store, Func<Task> first,
        Func<Task> second, Action? beforeRelease = null)
    {
        // Reflection controls only thread scheduling; it never alters store state.
        // Holding the existing Lucene lock pauses the first mutation exactly before
        // its keyword-index commit, allowing readers/writers to enter concurrently.
        var index = typeof(InMemoryVectorStore).GetField("_bm25Index", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        var indexGate = index.GetType().GetField("_indexLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(index)!;
        Monitor.Enter(indexGate);
        try
        {
            var firstWorker = StartThread(first);
            WaitUntilBlockedOrFinished(firstWorker.Thread, firstWorker.Completion);
            Assert.IsFalse(firstWorker.Completion.IsCompleted, "The mutation did not reach the index commit barrier.");
            var secondWorker = StartThread(second);
            WaitUntilBlockedOrFinished(secondWorker.Thread, secondWorker.Completion);
            var completedWhilePaused = secondWorker.Completion.IsCompleted;
            beforeRelease?.Invoke();
            return new Schedule(firstWorker.Completion, secondWorker.Completion, completedWhilePaused);
        }
        finally { Monitor.Exit(indexGate); }
    }

    private static (Thread Thread, Task Completion) StartThread(Func<Task> work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                work().GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "InMemory consistency regression" };
        thread.Start();
        return (thread, completion.Task);
    }

    private static void WaitUntilBlockedOrFinished(Thread thread, Task completion)
    {
        Assert.IsTrue(SpinWait.SpinUntil(
            () => completion.IsCompleted || (thread.ThreadState & ThreadState.WaitSleepJoin) != 0,
            Timeout), "Concurrent operation did not reach the scheduling barrier.");
    }
}
