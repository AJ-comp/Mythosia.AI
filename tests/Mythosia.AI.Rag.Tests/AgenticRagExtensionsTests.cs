using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Splitters;
using Mythosia.AI.Models.Functions;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public class AgenticRagExtensionsTests
{
    [TestMethod]
    public async Task WithAgenticRag_QueryOptions_AppliesPerCallStoreFilter()
    {
        var store = await CreateTaggedStoreAsync();
        var service = new MockAIService();

        service.WithAgenticRag(
            store,
            queryOptions: _ => new RagQueryOptions
            {
                StoreFilter = new VectorFilter().Where("tenant", "alpha")
            });

        var tool = service.Functions.Single(f => f.Name == "search_documents");
        Assert.IsNotNull(tool.Handler);
        var response = await tool.Handler!(new Dictionary<string, object>
        {
            ["query"] = "refund policy"
        });

        Assert.IsTrue(response.Contains("alpha-source"), "Filtered result should include the allowed tenant source.");
        Assert.IsFalse(response.Contains("beta-source"), "Filtered result should exclude other tenant sources.");
    }

    [TestMethod]
    public async Task WithAgenticRagTracing_ReceivesStructuredRagResult()
    {
        var store = await CreateTaggedStoreAsync();
        var service = new MockAIService();
        AgenticRagSearchTrace? captured = null;

        service
            .WithAgenticRag(
                store,
                queryOptions: _ => new RagQueryOptions
                {
                    StoreFilter = new VectorFilter().Where("tenant", "alpha")
                })
            .WithAgenticRagTracing(trace =>
            {
                captured = trace;
            });

        var tool = service.Functions.Single(f => f.Name == "search_documents");
        Assert.IsNotNull(tool.Handler);
        _ = await tool.Handler!(new Dictionary<string, object>
        {
            ["query"] = "refund policy"
        });

        Assert.IsNotNull(captured, "Trace callback should be invoked.");
        Assert.AreEqual("search_documents", captured.ToolName);
        Assert.AreEqual("refund policy", captured.Query);
        Assert.IsTrue(captured.Succeeded);
        Assert.IsNotNull(captured.QueryOptions);
        Assert.IsNotNull(captured.Result);
        Assert.IsTrue(captured.HasReferences);
        Assert.IsTrue(captured.Result!.References.Count > 0);
        Assert.IsTrue(captured.Result.Diagnostics.FinalTopK > 0);
        Assert.IsTrue(captured.Result.Diagnostics.ElapsedMs >= 0);
        Assert.AreEqual("alpha", captured.Result.References[0].Record.Metadata["tenant"]);
    }

    [TestMethod]
    public async Task WithAgenticRagTracing_ReceivesFailuresFromQueryOptions()
    {
        var store = await CreateTaggedStoreAsync();
        var service = new MockAIService();
        AgenticRagSearchTrace? captured = null;

        service
            .WithAgenticRag(
                store,
                queryOptions: _ => throw new InvalidOperationException("permission lookup failed"))
            .WithAgenticRagTracing(trace =>
            {
                captured = trace;
            });

        var tool = service.Functions.Single(f => f.Name == "search_documents");
        Assert.IsNotNull(tool.Handler);
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            tool.Handler!(new Dictionary<string, object> { ["query"] = "refund policy" }));

        Assert.AreEqual("permission lookup failed", exception.Message);
        Assert.IsNotNull(captured, "Trace callback should run even when the search fails.");
        Assert.IsFalse(captured!.Succeeded);
        Assert.IsNull(captured.Result);
        Assert.IsNotNull(captured.Exception);
        Assert.AreEqual("permission lookup failed", captured.Exception!.Message);
        Assert.AreSame(exception, captured.Exception);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task WithAgenticRag_ForwardsTheExecutionTokenWithOrWithoutQueryOptions(bool withQueryOptions)
    {
        var pipeline = new TokenProbePipeline();
        var store = new RagStore(pipeline, new InMemoryVectorStore());
        var service = new MockAIService();
        var options = new RagQueryOptions();
        if (withQueryOptions) service.WithAgenticRag(store, queryOptions: _ => options);
        else service.WithAgenticRag(store);
        var tool = service.Functions.Single();
        using var cancellation = new CancellationTokenSource();

        _ = await tool.HandlerWithCancellation!(
            new Dictionary<string, object> { ["query"] = "refund policy" }, cancellation.Token);

        Assert.AreEqual(cancellation.Token, pipeline.ObservedToken);
        Assert.AreEqual("refund policy", pipeline.ObservedQuery);
        Assert.AreSame(withQueryOptions ? options : null, pipeline.ObservedOptions);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task WithAgenticRag_CancelsRunningRetrievalAndPreservesTheFailureTrace(bool withQueryOptions)
    {
        var pipeline = new TokenProbePipeline { WaitForCancellation = true };
        var store = new RagStore(pipeline, new InMemoryVectorStore());
        var service = new MockAIService();
        AgenticRagSearchTrace? captured = null;
        if (withQueryOptions) service.WithAgenticRag(store, queryOptions: _ => new RagQueryOptions());
        else service.WithAgenticRag(store);
        service.WithAgenticRagTracing(trace => captured = trace);
        using var cancellation = new CancellationTokenSource();
        var execution = service.Functions.Single().HandlerWithCancellation!(
            new Dictionary<string, object> { ["query"] = "refund policy" }, cancellation.Token);
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            execution.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.IsNotNull(captured);
        Assert.IsFalse(captured.Succeeded);
        Assert.AreSame(exception, captured.Exception);
        Assert.AreEqual("refund policy", captured.Query);
        Assert.AreEqual(withQueryOptions, captured.QueryOptions != null);
        Assert.IsNull(captured.Result);
    }

    [TestMethod]
    public async Task WithAgenticRag_PipelineFailureBecomesAnErrorResultAndKeepsItsTrace()
    {
        var failure = new InvalidOperationException("retrieval failed");
        var pipeline = new TokenProbePipeline { Failure = failure };
        var service = new ToolProbeService();
        AgenticRagSearchTrace? captured = null;
        service.WithAgenticRag(new RagStore(pipeline, new InMemoryVectorStore()))
            .WithAgenticRagTracing(trace => captured = trace);

        var result = await service.ExecuteSearchAsync();

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.Content, failure.Message);
        Assert.IsNotNull(captured);
        Assert.IsFalse(captured.Succeeded);
        Assert.AreSame(failure, captured.Exception);
    }

    private sealed class ToolProbeService : MockAIService
    {
        public Task<FunctionCallResult> ExecuteSearchAsync()
            => ProcessFunctionCallAsync(new FunctionCall
            {
                Id = "rag-call", Name = "search_documents",
                Arguments = new Dictionary<string, object> { ["query"] = "refund policy" }
            });
    }

    private sealed class TokenProbePipeline : IRagPipeline
    {
        public CancellationToken ObservedToken { get; private set; }
        public string? ObservedQuery { get; private set; }
        public RagQueryOptions? ObservedOptions { get; private set; }
        public bool WaitForCancellation { get; init; }
        public Exception? Failure { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<RagProcessedQuery> ProcessAsync(string query, CancellationToken cancellationToken = default)
            => ProcessAsync(query, null, cancellationToken);

        public async Task<RagProcessedQuery> ProcessAsync(string query, RagQueryOptions? options,
            CancellationToken cancellationToken = default)
        {
            ObservedToken = cancellationToken;
            ObservedQuery = query;
            ObservedOptions = options;
            Started.TrySetResult();
            if (Failure != null) throw Failure;
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new RagProcessedQuery(query, query, Array.Empty<VectorSearchResult>(),
                Array.Empty<VectorSearchResult>(), new RagQueryDiagnostics());
        }
    }

    private static async Task<RagStore> CreateTaggedStoreAsync()
    {
        var vectorStore = new InMemoryVectorStore();
        var pipeline = new RagPipeline(
            new LocalEmbeddingProvider(256),
            vectorStore,
            new CharacterTextSplitter(400, 0, separator: null),
            new DefaultContextBuilder(),
            options: new RagPipelineOptions
            {
                DefaultQuery = new RagQueryOptions
                {
                    FinalFilter = new RagFilter
                    {
                        TopK = 5
                    }
                }
            });

        await pipeline.IndexDocumentsAsync(new[]
        {
            new RagDocument
            {
                Id = "alpha-refund",
                Content = "Refund policy for tenant alpha. Alpha refunds are allowed within 14 days.",
                Source = "alpha-source",
                Metadata = new Dictionary<string, string>
                {
                    ["tenant"] = "alpha"
                }
            },
            new RagDocument
            {
                Id = "beta-refund",
                Content = "Refund policy for tenant beta. Beta refunds are allowed within 30 days.",
                Source = "beta-source",
                Metadata = new Dictionary<string, string>
                {
                    ["tenant"] = "beta"
                }
            }
        });

        return new RagStore(pipeline, vectorStore);
    }
}
