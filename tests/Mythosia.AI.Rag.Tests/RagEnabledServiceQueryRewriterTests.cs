using System.Collections.Concurrent;
using System.Reflection;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RagEnabledServiceQueryRewriterTests
{
    [TestMethod]
    public async Task RewriterAddedAfterAttachment_IsUsedByExistingWrappersAndDirectStore()
    {
        var store = await RagStore.BuildAsync(builder => builder
            .AddText("Cancel at least 48 hours before arrival for a full refund.", "hotel-policy")
            .UseLocalEmbedding(32));
        using var vectorStore = (IDisposable)store.VectorStore;
        var firstWrapper = new MockAIService().WithRag(store);
        var secondWrapper = new MockAIService().WithRag(store);
        var rewriter = new RecordingRewriter((query, _, _) => Task.FromResult(QueryRewriteResult.Pass(query)));
        store.SetQueryRewriter(rewriter);

        var first = await firstWrapper.RetrieveAsync("What is the cancellation policy?");
        var second = await secondWrapper.RetrieveAsync("Does the cancellation policy apply to me?");
        var direct = await store.QueryAsync("What about late cancellations?", Array.Empty<ConversationTurn>());

        Assert.AreEqual(3, rewriter.Calls.Count);
        Assert.IsTrue(first.SearchSkipped);
        Assert.IsTrue(second.SearchSkipped);
        Assert.IsTrue(direct.SearchSkipped);
        Assert.AreEqual(0, first.References.Count);
    }

    [TestMethod]
    public async Task RewriterReplacedAfterAttachment_NextRequestUsesReplacementAndItsKeywords()
    {
        using var vectorStore = new InMemoryVectorStore();
        var pipeline = new RecordingPipeline();
        var original = new RecordingRewriter((query, _, _) => Task.FromResult(QueryRewriteResult.Pass(query)));
        var replacementResult = QueryRewriteResult.Search("replacement query", ["replacement-keyword"]);
        var replacement = new RecordingRewriter((_, _, _) => Task.FromResult(replacementResult));
        var store = new RagStore(pipeline, vectorStore, original);
        var wrapper = new MockAIService().WithRag(store);
        store.SetQueryRewriter(replacement);

        var result = await wrapper.RetrieveAsync("original question");

        Assert.AreEqual(0, original.Calls.Count);
        Assert.AreEqual(1, replacement.Calls.Count);
        Assert.AreSame(replacementResult, result.RewriteResult);
        Assert.AreEqual("original question", result.OriginalQuery);
        Assert.AreEqual("replacement query", result.RewrittenQuery);
        CollectionAssert.AreEqual(new[] { "replacement-keyword" }, result.SearchKeywords!.ToArray());
        Assert.AreEqual("replacement query", pipeline.Queries.Single());
        Assert.IsFalse(result.SearchSkipped);
    }

    [TestMethod]
    public async Task RewriterClearedAfterAttachment_NextRequestUsesOriginalQueryWithoutRewriteMetadata()
    {
        using var vectorStore = new InMemoryVectorStore();
        var pipeline = new RecordingPipeline();
        var rewriter = new RecordingRewriter((_, _, _) =>
            Task.FromResult(QueryRewriteResult.Search("old query", ["old-keyword"])));
        var store = new RagStore(pipeline, vectorStore, rewriter);
        var wrapper = new MockAIService().WithRag(store);
        store.SetQueryRewriter(null);

        var result = await wrapper.RetrieveAsync("original question");

        Assert.AreEqual(0, rewriter.Calls.Count);
        Assert.AreEqual("original question", pipeline.Queries.Single());
        Assert.IsNull(result.RewriteResult);
        Assert.IsNull(result.RewrittenQuery);
        Assert.IsNull(result.SearchKeywords);
        Assert.IsFalse(result.SearchSkipped);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ChangeDuringRewrite_CurrentRequestKeepsSnapshot_NextRequestUsesNewSetting(bool replace, bool skipSearch)
    {
        using var vectorStore = new InMemoryVectorStore();
        using var cancellation = new CancellationTokenSource();
        var pipeline = new RecordingPipeline();
        var barrier = new AsyncBarrier();
        var oldResult = skipSearch
            ? QueryRewriteResult.Pass("first question")
            : QueryRewriteResult.Search("old semantic query", ["old-keyword"]);
        var original = new RecordingRewriter(async (query, _, _) =>
        {
            // Only the old request pauses, so an incorrect second call fails assertions instead of hanging.
            if (query == "first question") await barrier.PauseAsync();
            return oldResult;
        });
        var replacement = new RecordingRewriter((_, _, _) =>
            Task.FromResult(QueryRewriteResult.Search("new semantic query", ["new-keyword"])));
        var store = new RagStore(pipeline, vectorStore, original);
        var service = new MockAIService();
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "Tell me about refunds."));
        var wrapper = service.WithRag(store);

        var first = wrapper.RetrieveAsync("first question", cancellation.Token);
        try
        {
            await barrier.WaitUntilEnteredAsync();
            store.SetQueryRewriter(replace ? replacement : null);

            var next = await wrapper.RetrieveAsync("next question");

            Assert.AreEqual(replace ? "new semantic query" : "next question", pipeline.Queries.Single());
            Assert.AreEqual(replace ? "new semantic query" : null, next.RewrittenQuery);
            Assert.AreEqual(replace ? 1 : 0, replacement.Calls.Count);
            Assert.AreEqual(1, original.Calls.Count);
            Assert.IsFalse(next.SearchSkipped);
        }
        finally { barrier.Resume(); }

        var result = await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreSame(oldResult, result.RewriteResult);
        Assert.AreEqual(skipSearch, result.SearchSkipped);
        Assert.AreEqual(skipSearch ? 1 : 2, pipeline.Queries.Count);
        if (!skipSearch)
        {
            Assert.AreEqual("old semantic query", pipeline.Queries.Last());
            CollectionAssert.AreEqual(new[] { "old-keyword" }, result.SearchKeywords!.ToArray());
        }
        var call = original.Calls.Single();
        Assert.AreEqual(cancellation.Token, call.Token);
        Assert.AreEqual("Tell me about refunds.", call.History!.Single().Content);
        Assert.AreEqual(1, original.Calls.Count);
    }

    [TestMethod]
    public async Task RewriterConfiguredBeforeAttachment_RemainsActive()
    {
        using var vectorStore = new InMemoryVectorStore();
        var pipeline = new RecordingPipeline();
        var rewriter = new RecordingRewriter((query, _, _) => Task.FromResult(QueryRewriteResult.Pass(query)));
        var wrapper = new MockAIService().WithRag(new RagStore(pipeline, vectorStore, rewriter));

        var result = await wrapper.RetrieveAsync("question");

        Assert.IsTrue(result.SearchSkipped);
        Assert.AreEqual(1, rewriter.Calls.Count);
        Assert.AreEqual(0, pipeline.Queries.Count);
    }

    [TestMethod]
    public async Task LazyCustomRewriter_IsPreservedWithoutCreatingAutomaticRewriter()
    {
        var service = new CountingAIService();
        var rewriter = new RecordingRewriter((query, _, _) => Task.FromResult(QueryRewriteResult.Pass(query)));
        var wrapper = service.WithRag(builder => builder
            .AddText("Refunds are available within 48 hours.", "policy")
            .UseLocalEmbedding(32)
            .WithQueryRewriter(rewriter));

        var result = await wrapper.RetrieveAsync("question");
        var store = GetInitializedStore(wrapper);
        using var vectorStore = (IDisposable)store.VectorStore;

        Assert.AreSame(rewriter, store.QueryRewriter);
        Assert.AreEqual(1, rewriter.Calls.Count);
        Assert.AreEqual(0, service.CompletionCalls);
        Assert.IsTrue(result.SearchSkipped);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LazyAutomaticRewriter_IsCreatedOnce_AndRuntimeUpdatesAreNotOverwritten(bool replace)
    {
        var service = new CountingAIService();
        var wrapper = service.WithRag(builder => builder
            .AddText("Refunds are available within 48 hours.", "policy")
            .UseLocalEmbedding(32)
            .WithQueryRewriter());

        var first = await wrapper.RetrieveAsync("first question");
        var store = GetInitializedStore(wrapper);
        using var vectorStore = (IDisposable)store.VectorStore;
        var automatic = store.QueryRewriter;
        var second = await wrapper.RetrieveAsync("second question");
        Assert.IsInstanceOfType<LlmQueryRewriter>(automatic);
        Assert.AreSame(automatic, store.QueryRewriter);
        Assert.IsTrue(first.SearchSkipped);
        Assert.IsTrue(second.SearchSkipped);
        Assert.AreEqual(2, service.CompletionCalls);

        var replacement = new RecordingRewriter((query, _, _) => Task.FromResult(QueryRewriteResult.Pass(query)));
        store.SetQueryRewriter(replace ? replacement : null);
        var next = await wrapper.RetrieveAsync("next question");
        var last = await wrapper.RetrieveAsync("last question");

        Assert.AreEqual(2, service.CompletionCalls, "The old automatic rewriter must not run or be recreated after the update.");
        Assert.AreSame(replace ? replacement : null, store.QueryRewriter);
        Assert.AreEqual(replace ? 2 : 0, replacement.Calls.Count);
        Assert.AreEqual(replace, next.SearchSkipped);
        Assert.AreEqual(replace, last.SearchSkipped);
        if (!replace)
        {
            Assert.IsNull(next.RewriteResult);
            Assert.IsNull(last.RewriteResult);
        }
    }

    // The lazy wrapper currently exposes no public store accessor. Reflection is limited to this
    // test helper; selection/update behavior is exercised through the normal public methods.
    private static RagStore GetInitializedStore(RagEnabledService wrapper) =>
        (RagStore)typeof(RagEnabledService).GetField("_ragStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wrapper)!;

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
        internal ConcurrentQueue<(string Query, IReadOnlyList<ConversationTurn>? History, CancellationToken Token)> Calls { get; } = new();
        public Task<QueryRewriteResult> RewriteAsync(string query, IReadOnlyList<ConversationTurn>? conversationHistory,
            CancellationToken cancellationToken = default)
        {
            Calls.Enqueue((query, conversationHistory, cancellationToken));
            return rewrite(query, conversationHistory, cancellationToken);
        }
    }

    private sealed class RecordingPipeline : IRagPipeline
    {
        internal ConcurrentQueue<string> Queries { get; } = new();
        public Task<RagProcessedQuery> ProcessAsync(string query, CancellationToken cancellationToken = default)
            => ProcessAsync(query, null, cancellationToken);
        public Task<RagProcessedQuery> ProcessAsync(string query, RagQueryOptions? options, CancellationToken cancellationToken = default)
        {
            Queries.Enqueue(query);
            return Task.FromResult(new RagProcessedQuery(query, query, Array.Empty<VectorSearchResult>(),
                Array.Empty<VectorSearchResult>(), new RagQueryDiagnostics()));
        }
    }

    private sealed class CountingAIService : MockAIService
    {
        internal int CompletionCalls { get; private set; }
        internal CountingAIService() => CompletionResponse = "[PASS]";
        public override Task<string> GetCompletionAsync(Message message)
        {
            CompletionCalls++;
            return base.GetCompletionAsync(message);
        }
    }
}
