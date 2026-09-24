using System.Reflection;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.VectorDb.Tests.InMemory;

[TestClass]
public class InMemoryCancellationTests
{
    private static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CancellationTimeout = TimeSpan.FromSeconds(2);

    [TestMethod]
    [DataRow("get")]
    [DataRow("get-batch")]
    [DataRow("count")]
    [DataRow("vector")]
    [DataRow("text")]
    [DataRow("hybrid")]
    [DataRow("configured-hybrid")]
    [DataRow("list")]
    [DataRow("scored")]
    [DataRow("upsert")]
    [DataRow("batch-upsert")]
    [DataRow("delete")]
    [DataRow("filtered-delete")]
    [DataRow("delete-by-filter")]
    [DataRow("replace")]
    public async Task Operation_CancelledWhileWaiting_ExitsBeforeCurrentCommitFinishes(string operation)
    {
        using var store = new InMemoryVectorStore();
        using var cancellation = new CancellationTokenSource();
        using var liveCancellation = new CancellationTokenSource();
        await store.UpsertAsync(Record("alpha"));

        var schedule = CancelWhileCommitIsPaused(store, operation, cancellation, liveCancellation.Token);

        // Release the index barrier and settle every worker before assertions, so
        // a regression cannot leave another test blocked behind the paused owner.
        await Task.WhenAll(schedule.Owner.Completion, schedule.LiveReader.Completion).WaitAsync(WorkerTimeout);
        Exception? cancellationError = null;
        try { await schedule.CancelledWaiter.Completion.WaitAsync(WorkerTimeout); }
        catch (Exception error) { cancellationError = error; }

        Assert.IsTrue(schedule.OwnerReachedBarrier, "The owner did not reach its index commit barrier.");
        Assert.IsTrue(schedule.WaiterReachedBarrier, "The request did not wait behind the owner.");
        Assert.IsTrue(schedule.OwnerWasStillPending, "The original commit must remain paused while cancellation is observed.");
        Assert.IsTrue(schedule.LiveReaderWasStillPending, "Cancelling one waiter must not release another operation's gate.");
        Assert.IsTrue(schedule.CancelledBeforeOwnerWasReleased,
            $"{operation} kept waiting for the unrelated commit after its own token was cancelled.");
        Assert.IsNotNull(cancellationError);
        Assert.AreEqual(typeof(OperationCanceledException), cancellationError.GetType());
        Assert.AreEqual(cancellation.Token, ((OperationCanceledException)cancellationError).CancellationToken);

        // The uncancelled owner must commit normally. Cancelled writes/deletes
        // must leave neither body nor keyword changes behind.
        Assert.AreEqual("beta", (await store.GetAsync("same"))!.Content);
        Assert.AreEqual(1L, await store.CountAsync());
        Assert.HasCount(0, await store.TextSearchAsync("alpha"));
        Assert.HasCount(0, await store.TextSearchAsync("gamma"));
        Assert.HasCount(1, await store.TextSearchAsync("beta"));

        // A new writer on a different thread proves the cancelled acquisition
        // did not leak ownership or corrupt the lock's recursion count.
        var subsequentWriter = StartWorker(() => store.UpsertAsync(Record("delta")));
        await subsequentWriter.Completion.WaitAsync(WorkerTimeout);
        Assert.AreEqual("delta", (await store.GetAsync("same"))!.Content);
        Assert.HasCount(0, await store.TextSearchAsync("beta"));
        Assert.HasCount(1, await store.TextSearchAsync("delta"));
    }

    private static Schedule CancelWhileCommitIsPaused(InMemoryVectorStore store, string operation,
        CancellationTokenSource cancellation, CancellationToken liveToken)
    {
        // The real index lock controls scheduling only. An actual upsert owns
        // the store gate while it waits to complete its keyword-index commit.
        var index = typeof(InMemoryVectorStore).GetField("_bm25Index", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        var indexGate = index.GetType().GetField("_indexLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(index)!;
        Monitor.Enter(indexGate);
        try
        {
            var owner = StartWorker(() => store.UpsertAsync(Record("beta")));
            var ownerReachedBarrier = WaitUntilBlocked(owner);
            var cancelledWaiter = StartWorker(() => InvokeAsync(store, operation, cancellation.Token));
            var waiterReachedBarrier = WaitUntilBlocked(cancelledWaiter);
            cancellation.Cancel();

            // The generous deadline measures cancellation while the owner is
            // still blocked, rather than depending on the polling interval.
            var cancelledBeforeRelease = SpinWait.SpinUntil(
                () => cancelledWaiter.Completion.IsCompleted, CancellationTimeout);
            var liveReader = StartWorker(async () =>
            {
                var result = await store.TextSearchAsync("beta", cancellationToken: liveToken);
                if (result.Count != 1 || result[0].Record.Content != "beta")
                    throw new InvalidOperationException("The live reader did not observe the completed owner.");
            });
            WaitUntilBlocked(liveReader);

            return new Schedule(owner, cancelledWaiter, liveReader, ownerReachedBarrier, waiterReachedBarrier,
                cancelledBeforeRelease, !owner.Completion.IsCompleted, !liveReader.Completion.IsCompleted);
        }
        finally { Monitor.Exit(indexGate); }
    }

    private static async Task InvokeAsync(InMemoryVectorStore store, string operation, CancellationToken token)
    {
        switch (operation)
        {
            case "get": await store.GetAsync("same", cancellationToken: token); break;
            case "get-batch": await store.GetBatchAsync(new[] { "same" }, cancellationToken: token); break;
            case "count": await store.CountAsync(cancellationToken: token); break;
            case "vector": await store.SearchAsync(new[] { 1f, 0f }, cancellationToken: token); break;
            case "text": await store.TextSearchAsync("beta", cancellationToken: token); break;
            case "hybrid": await store.HybridSearchAsync(new[] { 1f, 0f }, "beta", cancellationToken: token); break;
            case "configured-hybrid": await store.HybridSearchAsync(new[] { 1f, 0f }, "beta", new HybridSearchOptions(), cancellationToken: token); break;
            case "list": await store.ListAllRecordsAsync(token); break;
            case "scored": await store.ScoredListAsync(new[] { 1f, 0f }, token); break;
            case "upsert": await store.UpsertAsync(Record("gamma"), token); break;
            case "batch-upsert": await store.UpsertBatchAsync(new[] { Record("gamma") }, token); break;
            case "delete": await store.DeleteAsync("same", cancellationToken: token); break;
            case "filtered-delete": await store.DeleteAsync("same", new VectorFilter().Where("tenant", "mine"), token); break;
            case "delete-by-filter": await store.DeleteByFilterAsync(new VectorFilter().Where("tenant", "mine"), token); break;
            case "replace": await ((IVectorStore)store).ReplaceByFilterAsync(new VectorFilter().Where("tenant", "mine"), new[] { Record("gamma") }, token); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static VectorRecord Record(string content)
        => new("same", new[] { 1f, 0f }, content) { Metadata = { ["tenant"] = "mine" } };

    private static Worker StartWorker(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action().GetAwaiter().GetResult(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "InMemory cancelled waiter regression" };
        thread.Start();
        return new Worker(thread, completion.Task);
    }

    private static bool WaitUntilBlocked(Worker worker)
        => SpinWait.SpinUntil(() => worker.Completion.IsCompleted ||
            (worker.Thread.ThreadState & ThreadState.WaitSleepJoin) != 0, WorkerTimeout)
            && !worker.Completion.IsCompleted;

    private sealed record Worker(Thread Thread, Task Completion);

    private sealed record Schedule(Worker Owner, Worker CancelledWaiter, Worker LiveReader,
        bool OwnerReachedBarrier, bool WaiterReachedBarrier, bool CancelledBeforeOwnerWasReleased,
        bool OwnerWasStillPending, bool LiveReaderWasStillPending);
}
