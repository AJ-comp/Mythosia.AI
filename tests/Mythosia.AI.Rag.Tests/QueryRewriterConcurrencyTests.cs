using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class QueryRewriterConcurrencyTests
{
    private static readonly IReadOnlyList<ConversationTurn> History =
        [new ConversationTurn("user", "Tell me about the refund policy.")];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeDuringProgress_CurrentRequestKeepsSelectedRewriter_NextRequestUsesNewSetting(bool replace)
    {
        using var vectorStore = new InMemoryVectorStore();
        using var cancellation = new CancellationTokenSource();
        var pipeline = new RecordingPipeline();
        var oldResult = QueryRewriteResult.Search("original rewritten question", ["original-keyword"]);
        var oldRewriter = new RecordingRewriter((_, _, _) => Task.FromResult(oldResult));
        var nextRewriter = new RecordingRewriter((_, _, _) =>
            Task.FromResult(QueryRewriteResult.Search("replacement rewritten question", ["replacement-keyword"])));
        var store = new RagStore(pipeline, vectorStore, oldRewriter);
        var barrier = new AsyncBarrier();
        var options = new RagQueryOptions
        {
            ProgressAsync = stage => stage == RagProgressStage.QueryRewrite ? barrier.PauseAsync() : Task.CompletedTask
        };

        var first = store.QueryAsync("first question", History, options, cancellation.Token);
        try
        {
            await barrier.WaitUntilEnteredAsync();
            Assert.AreEqual(0, oldRewriter.Calls.Count);
            store.SetQueryRewriter(replace ? nextRewriter : null);

            // A new request must observe the update even while the previous one is paused.
            var next = await store.QueryAsync("next question", History);
            Assert.AreEqual(replace ? "replacement rewritten question" : "next question", pipeline.Calls.Single().Query);
            Assert.AreEqual(replace ? 1 : 0, nextRewriter.Calls.Count);
            Assert.AreEqual(replace ? "replacement rewritten question" : null, next.RewrittenQuery);
        }
        finally { barrier.Resume(); }

        var result = await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, oldRewriter.Calls.Count);
        Assert.AreEqual(replace ? 1 : 0, nextRewriter.Calls.Count);
        Assert.AreEqual("first question", oldRewriter.Calls.Single().Query);
        Assert.AreSame(History, oldRewriter.Calls.Single().History);
        Assert.AreEqual(cancellation.Token, oldRewriter.Calls.Single().Token);
        Assert.AreSame(oldResult, result.RewriteResult);
        CollectionAssert.AreEqual(new[] { "original-keyword" }, result.SearchKeywords!.ToArray());
        Assert.AreEqual("first question", result.OriginalQuery);
        Assert.AreEqual("original rewritten question", result.RewrittenQuery);
        Assert.AreEqual("original rewritten question", pipeline.Calls.Last().Query);
        Assert.AreSame(options, pipeline.Calls.Last().Options);
        Assert.AreEqual(cancellation.Token, pipeline.Calls.Last().Token);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ChangeDuringRewrite_KeepsInflightResultAndSearchDecision(bool replace, bool skipSearch)
    {
        using var vectorStore = new InMemoryVectorStore();
        var pipeline = new RecordingPipeline();
        var barrier = new AsyncBarrier();
        var oldResult = skipSearch
            ? QueryRewriteResult.Pass("first question")
            : QueryRewriteResult.Search("old semantic query", ["old-keyword"]);
        var oldRewriter = new RecordingRewriter(async (_, _, _) =>
        {
            await barrier.PauseAsync();
            return oldResult;
        });
        var nextRewriter = new RecordingRewriter((_, _, _) => Task.FromResult(QueryRewriteResult.Search("new semantic query")));
        var store = new RagStore(pipeline, vectorStore, oldRewriter);

        var first = store.QueryAsync("first question", History);
        try
        {
            await barrier.WaitUntilEnteredAsync();
            store.SetQueryRewriter(replace ? nextRewriter : null);
            await store.QueryAsync("next question", History);
            Assert.AreEqual(replace ? "new semantic query" : "next question", pipeline.Calls.Single().Query);
        }
        finally { barrier.Resume(); }

        var result = await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreSame(oldResult, result.RewriteResult);
        Assert.AreEqual(skipSearch, result.SearchSkipped);
        Assert.AreEqual(skipSearch ? 1 : 2, pipeline.Calls.Count);
        if (!skipSearch)
        {
            Assert.AreEqual("old semantic query", pipeline.Calls.Last().Query);
            CollectionAssert.AreEqual(new[] { "old-keyword" }, result.SearchKeywords!.ToArray());
        }
        Assert.AreEqual(1, oldRewriter.Calls.Count);
        Assert.AreEqual(replace ? 1 : 0, nextRewriter.Calls.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AlreadyCanceled_DoesNotDispatchProgressRewriterOrPipeline(bool configured)
    {
        using var cancellation = new CancellationTokenSource();
        using var vectorStore = new InMemoryVectorStore();
        var pipeline = new RecordingPipeline();
        var rewriter = new RecordingRewriter((query, _, _) => Task.FromResult(QueryRewriteResult.Search(query)));
        var store = new RagStore(pipeline, vectorStore, configured ? rewriter : null);
        var progressCalls = 0;
        var options = new RagQueryOptions
        {
            ProgressAsync = _ => { progressCalls++; return Task.CompletedTask; }
        };
        cancellation.Cancel();

        var exception = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            store.QueryAsync("question", History, options, cancellation.Token));

        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.AreEqual(0, progressCalls);
        Assert.AreEqual(0, rewriter.Calls.Count);
        Assert.AreEqual(0, pipeline.Calls.Count);
    }

    [TestMethod]
    public async Task CanceledDuringProgress_DoesNotDispatchSelectedRewriter_AndLaterQueryStillWorks()
    {
        using var cancellation = new CancellationTokenSource();
        using var vectorStore = new InMemoryVectorStore();
        var pipeline = new RecordingPipeline();
        var rewriter = new RecordingRewriter((query, _, _) => Task.FromResult(QueryRewriteResult.Search(query)));
        var store = new RagStore(pipeline, vectorStore, rewriter);
        var barrier = new AsyncBarrier();
        var first = store.QueryAsync("canceled question", History,
            new RagQueryOptions { ProgressAsync = _ => barrier.PauseAsync() }, cancellation.Token);
        try
        {
            await barrier.WaitUntilEnteredAsync();
            cancellation.Cancel();
        }
        finally { barrier.Resume(); }

        var exception = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => first);
        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.AreEqual(0, rewriter.Calls.Count);
        Assert.AreEqual(0, pipeline.Calls.Count);

        await store.QueryAsync("next question", History);
        Assert.AreEqual("next question", rewriter.Calls.Single().Query);
        Assert.AreEqual("next question", pipeline.Calls.Single().Query);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CanceledDuringRewrite_ResultCannotDispatchSearchOrCompleteAsSkipped(bool skipSearch)
    {
        using var cancellation = new CancellationTokenSource();
        using var vectorStore = new InMemoryVectorStore();
        var pipeline = new RecordingPipeline();
        var barrier = new AsyncBarrier();
        var rewriter = new RecordingRewriter(async (query, _, _) =>
        {
            // Deliberately ignore cancellation to check the library's continuation boundary.
            await barrier.PauseAsync();
            return skipSearch ? QueryRewriteResult.Pass(query) : QueryRewriteResult.Search("rewritten question");
        });
        var store = new RagStore(pipeline, vectorStore, rewriter);
        var first = store.QueryAsync("canceled question", History, cancellation.Token);
        try
        {
            await barrier.WaitUntilEnteredAsync();
            cancellation.Cancel();
            store.SetQueryRewriter(null);
        }
        finally { barrier.Resume(); }

        var exception = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => first);
        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.AreEqual(cancellation.Token, rewriter.Calls.Single().Token);
        Assert.AreEqual(0, pipeline.Calls.Count);

        await store.QueryAsync("next question", History);
        Assert.AreEqual("next question", pipeline.Calls.Single().Query);
        Assert.AreEqual(1, rewriter.Calls.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProgressOrRewriteFailure_DoesNotDispatchSearchOrPoisonLaterRequests(bool failInProgress)
    {
        using var vectorStore = new InMemoryVectorStore();
        var pipeline = new RecordingPipeline();
        var expected = new InvalidOperationException("Expected callback failure.");
        var rewriter = new RecordingRewriter((_, _, _) => Task.FromException<QueryRewriteResult>(expected));
        var store = new RagStore(pipeline, vectorStore, rewriter);
        var options = new RagQueryOptions
        {
            ProgressAsync = _ => failInProgress ? Task.FromException(expected) : Task.CompletedTask
        };

        var actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            store.QueryAsync("failed question", History, options));

        Assert.AreSame(expected, actual);
        Assert.AreEqual(failInProgress ? 0 : 1, rewriter.Calls.Count);
        Assert.AreEqual(0, pipeline.Calls.Count);

        store.SetQueryRewriter(null);
        await store.QueryAsync("next question", History);
        Assert.AreEqual("next question", pipeline.Calls.Single().Query);
    }

    private sealed class AsyncBarrier
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal async Task PauseAsync()
        {
            _entered.TrySetResult();
            await _resume.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        internal Task WaitUntilEnteredAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        internal void Resume() => _resume.TrySetResult();
    }

    private sealed class RecordingRewriter(
        Func<string, IReadOnlyList<ConversationTurn>?, CancellationToken, Task<QueryRewriteResult>> rewrite) : IQueryRewriter
    {
        internal List<(string Query, IReadOnlyList<ConversationTurn>? History, CancellationToken Token)> Calls { get; } = [];
        public Task<QueryRewriteResult> RewriteAsync(string query, IReadOnlyList<ConversationTurn>? conversationHistory,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((query, conversationHistory, cancellationToken));
            return rewrite(query, conversationHistory, cancellationToken);
        }
    }

    private sealed class RecordingPipeline : IRagPipeline
    {
        internal List<(string Query, RagQueryOptions? Options, CancellationToken Token)> Calls { get; } = [];
        public Task<RagProcessedQuery> ProcessAsync(string query, CancellationToken cancellationToken = default)
            => ProcessAsync(query, null, cancellationToken);
        public Task<RagProcessedQuery> ProcessAsync(string query, RagQueryOptions? options, CancellationToken cancellationToken = default)
        {
            // Intentionally do not enforce cancellation here: the caller must not dispatch a canceled request.
            Calls.Add((query, options, cancellationToken));
            return Task.FromResult(new RagProcessedQuery(query, query, Array.Empty<VectorSearchResult>(),
                Array.Empty<VectorSearchResult>(), new RagQueryDiagnostics()));
        }
    }
}
