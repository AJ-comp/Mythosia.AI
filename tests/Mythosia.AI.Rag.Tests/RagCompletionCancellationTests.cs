using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Reranking;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public class RagCompletionCancellationTests
{
    [TestMethod]
    [DataRow("rewrite")]
    [DataRow("rerank")]
    [DataRow("rag-string")]
    [DataRow("rag-message")]
    [DataRow("rag-string-options")]
    [DataRow("rag-message-options")]
    [DataRow("pipeline-top-k")]
    [DataRow("pipeline-options")]
    public async Task Cancellation_ReachesInnerCompletion_AndWaitsForItsCleanup(string entryPoint)
    {
        var service = new CancellableCompletionService();
        using var cancellation = new CancellationTokenSource();
        var request = InvokeAsync(service, entryPoint, cancellation.Token);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.AreEqual(1, service.Calls);
        Assert.AreEqual(cancellation.Token, service.ReceivedToken);
        Assert.IsTrue(service.CleanedUp, "The wrapper must await cancellation of the inner operation, not abandon its task.");
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow("rewrite")]
    [DataRow("rerank")]
    [DataRow("rag-string")]
    [DataRow("rag-message")]
    [DataRow("rag-string-options")]
    [DataRow("rag-message-options")]
    [DataRow("pipeline-top-k")]
    [DataRow("pipeline-options")]
    public async Task PreCancelled_DoesNotStartInnerCompletion(string entryPoint)
    {
        var service = new CancellableCompletionService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => InvokeAsync(service, entryPoint, cancellation.Token));

        Assert.AreEqual(0, service.Calls);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task RagQueryRewriteCancellation_StopsBeforeFinalAnswer()
    {
        var service = new CancellableCompletionService();
        var rag = service.WithRag(builder => builder
            .AddText("Shipping takes three days.", id: "shipping")
            .UseLocalEmbedding(32)
            .WithQueryRewriter());
        using var cancellation = new CancellationTokenSource();
        var request = rag.GetCompletionAsync("Shipping policy?", cancellationToken: cancellation.Token);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.AreEqual(1, service.Calls, "The cancelled rewriting call must not be followed by an answer call.");
        Assert.AreEqual(cancellation.Token, service.ReceivedToken);
        Assert.IsTrue(service.StatelessDuringCall, "Internal query rewriting retains its stateless request profile.");
        Assert.IsFalse(service.StatelessMode, "The profile must not modify service defaults.");
        Assert.IsTrue(service.CleanedUp);
    }

    private static Task InvokeAsync(CancellableCompletionService service, string entryPoint, CancellationToken token)
    {
        var rag = service.WithRag(builder => builder
            .AddText("Shipping takes three days.", id: "shipping")
            .UseLocalEmbedding(32));
        var pipeline = new RagPipeline(new LocalEmbeddingProvider(32), new InMemoryVectorStore(),
            new CharacterTextSplitter(chunkSize: 200, chunkOverlap: 20), new DefaultContextBuilder());
        var message = new Message(ActorRole.User, "Shipping policy?");
        return entryPoint switch
        {
            "rewrite" => new LlmQueryRewriter(service).RewriteAsync("Shipping policy?", null, token),
            "rerank" => new LlmReranker(service).RerankAsync("Shipping policy?",
                new[] { new VectorSearchResult(new VectorRecord { Id = "shipping", Content = "Three days." }, 0.8) }, token),
            "rag-string" => rag.GetCompletionAsync("Shipping policy?", cancellationToken: token),
            "rag-message" => rag.GetCompletionAsync(message, cancellationToken: token),
            "rag-string-options" => rag.GetCompletionAsync("Shipping policy?", new RagQueryOptions(), token),
            "rag-message-options" => rag.GetCompletionAsync(message, new RagQueryOptions(), token),
            "pipeline-top-k" => pipeline.QueryAndGenerateAsync(service, "Shipping policy?", topK: 2, cancellationToken: token),
            "pipeline-options" => pipeline.QueryAndGenerateAsync(service, "Shipping policy?", new RagQueryOptions(), cancellationToken: token),
            _ => throw new AssertFailedException($"Unknown entry point: {entryPoint}")
        };
    }

    private sealed class CancellableCompletionService : MockAIService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }
        public bool CleanedUp { get; private set; }
        public bool StatelessDuringCall { get; private set; }

        public override async Task<string> GetCompletionAsync(Message message)
        {
            Calls++;
            ReceivedToken = RequestCancellationToken;
            StatelessDuringCall = RequestStatelessMode;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ReceivedToken);
                throw new AssertFailedException("The cancellation barrier must not complete normally.");
            }
            finally { CleanedUp = true; }
        }
    }
}
