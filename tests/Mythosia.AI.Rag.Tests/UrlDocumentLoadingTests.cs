using System.Net;
using System.Net.Sockets;
using System.Text;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class UrlDocumentLoadingTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task CancelDuringDownload_CompletesBeforeServerResponseAndPreservesIndex(bool sendHeaders, bool callback)
    {
        await using var server = new LocalDocumentServer("NEW policy", holdResponse: true, sendHeadersBeforeHold: sendHeaders);
        using var vectors = new InMemoryVectorStore();
        await Seed(vectors, server.Url);
        using var cancellation = new CancellationTokenSource();
        var embedding = new CountingEmbedding();
        int callbackCalls = 0;
        var build = RagStore.BuildAsync(builder => builder.AddUrl(server.Url).UseStore(vectors).UseEmbedding(embedding),
            onDocumentEmbedded: callback ? _ => { callbackCalls++; return Task.CompletedTask; } : null,
            cancellationToken: cancellation.Token);
        await server.RequestReady.WaitAsync(TimeSpan.FromSeconds(10));

        cancellation.Cancel();

        // The server stays blocked until disposal; a missing HTTP token times out here.
        await Assert.ThrowsAsync<OperationCanceledException>(() => build.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, callbackCalls);
        Assert.AreEqual(0, embedding.Calls);
        await AssertOriginalIndex(vectors);
    }

    [TestMethod]
    public async Task AlreadyCanceled_DoesNotStartHttpRequestOrEmbedding()
    {
        await using var server = new LocalDocumentServer("NEW policy", holdResponse: true);
        using var vectors = new InMemoryVectorStore();
        await Seed(vectors, server.Url);
        var embedding = new CountingEmbedding();
        await Assert.ThrowsAsync<OperationCanceledException>(() => RagStore.BuildAsync(
            builder => builder.AddUrl(server.Url).UseStore(vectors).UseEmbedding(embedding),
            cancellationToken: new CancellationToken(true)));
        Assert.IsFalse(server.RequestReady.IsCompleted);
        Assert.AreEqual(0, embedding.Calls);
        await AssertOriginalIndex(vectors);
    }

    [TestMethod]
    [DataRow("utf-8")]
    [DataRow("utf-16")]
    public async Task SuccessfulDownload_PreservesCharsetAndUrlIdentity(string charset)
    {
        const string text = "서울의 새 정책 🙂";
        await using var server = new LocalDocumentServer(text, charset: charset);
        using var vectors = new InMemoryVectorStore();
        await Seed(vectors, server.Url);

        await RagStore.BuildAsync(builder => builder.AddUrl(server.Url).UseStore(vectors).UseLocalEmbedding(8));

        var records = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(2, records.Count);
        var updated = records.Single(r => r.Metadata["document_id"] == server.Url);
        Assert.AreEqual(text, updated.Content);
        Assert.AreEqual(server.Url, updated.Metadata["source"]);
        Assert.AreEqual(server.Url, updated.Metadata["url"]);
        Assert.AreEqual("url", updated.Metadata["type"]);
        Assert.AreEqual("KEEP", records.Single(r => r.Metadata["document_id"] == "other").Content);
    }

    [TestMethod]
    [DataRow(404)]
    [DataRow(500)]
    public async Task FailedDownload_DoesNotDeletePreviousDocument(int status)
    {
        await using var server = new LocalDocumentServer("error page", status: status);
        using var vectors = new InMemoryVectorStore();
        await Seed(vectors, server.Url);
        var embedding = new CountingEmbedding();

        await Assert.ThrowsAsync<HttpRequestException>(() => RagStore.BuildAsync(
            builder => builder.AddUrl(server.Url).UseStore(vectors).UseEmbedding(embedding)));

        Assert.AreEqual(0, embedding.Calls);
        await AssertOriginalIndex(vectors);
    }

    [TestMethod]
    public async Task SuccessfulEmptyDownload_RemovesOnlyItsPreviousDocument()
    {
        await using var server = new LocalDocumentServer("");
        using var vectors = new InMemoryVectorStore();
        await Seed(vectors, server.Url);
        var embedding = new CountingEmbedding();

        await RagStore.BuildAsync(builder => builder.AddUrl(server.Url).UseStore(vectors).UseEmbedding(embedding));

        var records = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("KEEP", records[0].Content);
        Assert.AreEqual(0, embedding.Calls);
    }

    private static async Task Seed(InMemoryVectorStore vectors, string url)
    {
        await vectors.UpsertAsync(new VectorRecord(url + "_chunk_0", new float[8], "OLD")
            { Metadata = { ["document_id"] = url } });
        await vectors.UpsertAsync(new VectorRecord("other_chunk_0", new float[8], "KEEP")
            { Metadata = { ["document_id"] = "other" } });
    }

    private static async Task AssertOriginalIndex(InMemoryVectorStore vectors) =>
        CollectionAssert.AreEquivalent(new[] { "OLD", "KEEP" }, (await vectors.ListAllRecordsAsync()).Select(r => r.Content).ToArray());

    private sealed class CountingEmbedding : IEmbeddingProvider
    {
        private readonly LocalEmbeddingProvider inner = new(8);
        public int Dimensions => 8;
        public int Calls { get; private set; }
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        { Calls++; return inner.GetEmbeddingAsync(text, cancellationToken); }
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        { Calls++; return inner.GetEmbeddingsAsync(texts, cancellationToken); }
    }

    private sealed class LocalDocumentServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stopping = new();
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task serving;
        public string Url { get; }
        public Task RequestReady => ready.Task;

        public LocalDocumentServer(string content, bool holdResponse = false, bool sendHeadersBeforeHold = false,
            string charset = "utf-8", int status = 200)
        {
            listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/document";
            serving = Serve(content, holdResponse, sendHeadersBeforeHold, charset, status);
        }

        private async Task Serve(string content, bool holdResponse, bool sendHeadersBeforeHold, string charset, int status)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(stopping.Token);
                using var stream = client.GetStream();
                var request = new StringBuilder();
                var buffer = new byte[1024];
                while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, stopping.Token);
                    if (read == 0) throw new IOException("Request ended before headers.");
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    if (request.Length > 16_384) throw new IOException("Unexpectedly large test request.");
                }
                var body = Encoding.GetEncoding(charset).GetBytes(content);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Type: text/plain; charset={charset}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                if (sendHeadersBeforeHold) await stream.WriteAsync(headers, stopping.Token);
                ready.TrySetResult();
                if (holdResponse) await release.Task.WaitAsync(stopping.Token);
                if (!sendHeadersBeforeHold) await stream.WriteAsync(headers, stopping.Token);
                await stream.WriteAsync(body, stopping.Token);
            }
            catch (Exception ex) when (stopping.IsCancellationRequested &&
                ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            { }
            catch (Exception ex) { ready.TrySetException(ex); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            await stopping.CancelAsync();
            release.TrySetResult();
            listener.Stop();
            try { await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { stopping.Dispose(); }
        }
    }
}
