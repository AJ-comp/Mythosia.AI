using System.Net;
using System.Text;

namespace Mythosia.AI.Serving.LlamaCpp.Tests;

internal sealed class TestHttp : HttpMessageHandler
{
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _replies = new();
    internal List<(string Method, string Path, string? Authorization, string? Body)> Calls { get; } = [];

    internal void Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        Reply(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    internal void Text(string body, string type = "text/event-stream") =>
        Reply(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) });
    internal void Reply(HttpResponseMessage response) => _replies.Enqueue(_ => Task.FromResult(response));
    internal void Reply(Func<CancellationToken, Task<HttpResponseMessage>> reply) => _replies.Enqueue(reply);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(),
            request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        if (_replies.Count == 0) throw new InvalidOperationException("No fake reply is available.");
        return await _replies.Dequeue()(cancellationToken);
    }
}

internal sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}

internal sealed class BlockingStream : Stream
{
    private readonly CancellationTokenSource _disposed = new();
    internal bool WasDisposed { get; private set; }
    internal TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ReadStarted.TrySetResult();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposed.Token);
        await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
        return 0;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        if (disposing) _disposed.Cancel();
        base.Dispose(disposing);
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
