using System.Net;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Services.Perplexity;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class PerplexityHttpCancellationTests
{
    [TestMethod]
    [DataRow("search", 200)]
    [DataRow("search", 400)]
    [DataRow("standard", 200)]
    [DataRow("standard", 400)]
    [DataRow("context", 200)]
    [DataRow("context", 400)]
    public async Task Cancellation_InterruptsBodyBufferingAndDisposesResponse(string api, int status)
    {
        using var handler = new StalledBodyHandler((HttpStatusCode)status);
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        Task pending = api switch
        {
            "search" => new PerplexitySearchClient("key", http).SearchAsync("query", cancellationToken: cancellation.Token),
            "standard" => new PerplexityEmbeddingProvider("key", http).GetEmbeddingAsync("query", cancellation.Token),
            _ => new PerplexityContextualizedEmbeddingProvider("key", http).GetQueryEmbeddingAsync("query", cancellation.Token)
        };
        await handler.Content.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(handler.Content.WasDisposed);
    }

    private sealed class StalledBodyHandler(HttpStatusCode status) : HttpMessageHandler
    {
        internal StalledContent Content { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = Content });
    }

    private sealed class StalledContent : HttpContent
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool WasDisposed { get; private set; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
