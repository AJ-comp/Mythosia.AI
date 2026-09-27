using System.Net;
using Mythosia.AI.Rag.Embeddings;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class GeminiEmbeddingCancellationTests
{
    [TestMethod]
    [DataRow("single")]
    [DataRow("batch")]
    [DataRow("query")]
    [DataRow("document")]
    public async Task CallerCancellation_InterruptsHttpAndPreservesOriginalToken(string mode)
    {
        using var handler = new StalledRequestHandler();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var provider = new GeminiEmbeddingProvider("key", http, dimensions: 128, maxConcurrency: 1);
        var pending = Invoke(provider, mode, cancellation.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.IsTrue(handler.HttpWasCanceled);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.IsNull(exception.InnerException);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request!.Content!.ReadAsStringAsync());
    }

    [TestMethod]
    [DataRow("single")]
    [DataRow("batch")]
    [DataRow("query")]
    [DataRow("document")]
    public async Task PreCanceledCall_DoesNotSendRequests(string mode)
    {
        using var handler = new StalledRequestHandler();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new GeminiEmbeddingProvider("key", http);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => Invoke(provider, mode, cancellation.Token));

        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    [DataRow(200)]
    [DataRow(400)]
    public async Task CallerCancellation_InterruptsResponseBodyReadAndDisposesResponse(int status)
    {
        using var handler = new StalledBodyHandler((HttpStatusCode)status);
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var provider = new GeminiEmbeddingProvider("key", http, dimensions: 128);
        var pending = provider.GetEmbeddingAsync("text", cancellation.Token);
        await handler.Content.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.IsTrue(handler.Content.WasCanceled);
        Assert.IsTrue(handler.Content.WasDisposed);
    }

    [TestMethod]
    public async Task ProviderTimeout_CancelsHttpAndQueuedInputsAcrossTheWholeOperation()
    {
        using var handler = new StalledRequestHandler();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var provider = new GeminiEmbeddingProvider("key", http, dimensions: 128, timeout: TimeSpan.FromMilliseconds(200), maxConcurrency: 1);
        var pending = provider.GetEmbeddingsAsync(new[] { "active", "queued-one", "queued-two" });
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.IsTrue(handler.HttpWasCanceled);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.IsNull(exception.InnerException);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, http.Timeout);
    }

    [TestMethod]
    public async Task CallerHttpClientTimeout_IsSanitizedAndRemainsAConfigurationOfTheCaller()
    {
        using var handler = new StalledRequestHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(150) };
        var provider = new GeminiEmbeddingProvider("key", http, dimensions: 128);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => provider.GetEmbeddingAsync("text").WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.IsTrue(handler.HttpWasCanceled);
        Assert.IsNull(exception.InnerException);
        Assert.AreEqual(TimeSpan.FromMilliseconds(150), http.Timeout);
    }

    [TestMethod]
    public async Task WaitingForAnotherOperation_IsCancelableWithoutCancelingItsActiveRequest()
    {
        using var handler = new StalledRequestHandler();
        using var http = new HttpClient(handler);
        using var activeCancellation = new CancellationTokenSource();
        using var waitingCancellation = new CancellationTokenSource();
        var provider = new GeminiEmbeddingProvider("key", http, dimensions: 128, maxConcurrency: 1);
        var active = provider.GetEmbeddingAsync("active", activeCancellation.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var waiting = provider.GetQueryEmbeddingAsync("waiting", waitingCancellation.Token);

        waitingCancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(waitingCancellation.Token, exception.CancellationToken);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.IsFalse(handler.HttpWasCanceled);
        activeCancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => active.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public async Task Failure_CancelsSiblingRequestsAndNeverReturnsPartialVectors()
    {
        using var handler = new FailingSiblingHandler();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var provider = new GeminiEmbeddingProvider("key", http, dimensions: 128, maxConcurrency: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetEmbeddingsAsync(new[] { "pending", "invalid" }).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.IsTrue(handler.SiblingWasCanceled);
    }

    private static Task Invoke(GeminiEmbeddingProvider provider, string mode, CancellationToken cancellationToken)
        => mode switch
        {
            "single" => provider.GetEmbeddingAsync("text", cancellationToken),
            "batch" => provider.GetEmbeddingsAsync(new[] { "first", "second" }, cancellationToken),
            "query" => provider.GetQueryEmbeddingAsync("query", cancellationToken),
            _ => provider.GetDocumentEmbeddingsAsync(new EmbeddingDocument("doc", new[] { "first", "second" }), cancellationToken)
        };

    private sealed class StalledRequestHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool HttpWasCanceled { get; private set; }
        internal HttpRequestMessage? Request { get; private set; }
        internal int RequestCount { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestCount++;
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { HttpWasCanceled = true; throw; }
            throw new InvalidOperationException("The request unexpectedly resumed.");
        }
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
        internal bool WasCanceled { get; private set; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { WasCanceled = true; throw; }
        }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }

    private sealed class FailingSiblingHandler : HttpMessageHandler
    {
        private int _requests;
        internal bool SiblingWasCanceled { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requests) == 2)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"embedding\":{\"values\":[]}}") };
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { SiblingWasCanceled = true; throw; }
            throw new InvalidOperationException("The request unexpectedly resumed.");
        }
    }
}
