using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("Streaming")]
public class AnthropicResponseCleanupTests
{
    private const string CurrentModel = "claude-sonnet-5-5";
    private const string OlderModel = "claude-sonnet-4-20250514";
    private const string ErrorBody = """{"error":{"type":"overloaded_error","message":"응답 café unavailable"}}""";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow("text", CurrentModel)]
    [DataRow("advanced", CurrentModel)]
    [DataRow("callback", CurrentModel)]
    [DataRow("run", CurrentModel)]
    [DataRow("text", OlderModel)]
    public async Task AsyncOnlyResponse_CompleteSuccessKeepsAnswerAndDisposesOwners(string entry, string model)
    {
        var body = new AsyncOnlyBody(SuccessBody(model));
        using var fixture = new Fixture(body, model: model);
        var observed = new List<StreamingContent>();

        await ObserveAsync(fixture.Service, entry, observed).WaitAsync(Deadline);

        Assert.AreEqual("answer", Text(observed));
        Assert.AreEqual("answer", fixture.Service.ActivateChat.Messages.Single(m => m.Role == ActorRole.Assistant).Content);
        Assert.IsFalse(observed.Any(item => item.Type == StreamingContentType.Error));
        AssertCleanup(fixture, body);
        Assert.AreEqual(1, fixture.CompletedDiagnostics);
        Assert.AreEqual(1, fixture.Handler.Requests);
    }

    [TestMethod]
    [DataRow("text", CurrentModel, false)]
    [DataRow("advanced", CurrentModel, false)]
    [DataRow("callback", CurrentModel, false)]
    [DataRow("run", CurrentModel, false)]
    [DataRow("advanced", OlderModel, false)]
    [DataRow("text", CurrentModel, true)]
    [DataRow("run", CurrentModel, true)]
    public async Task AsyncOnlyResponse_PartialReadFailureSurvivesBothCleanupFailures(
        string entry, string model, bool throwAsyncDispose)
    {
        var cause = new IOException("Original response read failure");
        var body = new AsyncOnlyBody(TextPrefix(model), cause) { ThrowAsyncDispose = throwAsyncDispose };
        using var fixture = new Fixture(body, model: model);
        var observed = new List<StreamingContent>();

        var exception = await Assert.ThrowsExactlyAsync<StreamReadException>(() =>
            ObserveAsync(fixture.Service, entry, observed).WaitAsync(Deadline));

        Assert.AreSame(cause, exception.InnerException);
        Assert.IsTrue(exception.Diagnostics.LinesRead > 0);
        Assert.AreEqual("answer", Text(observed), "The failure must happen after a delivered text prefix.");
        AssertNoAssistant(fixture.Service);
        AssertCleanup(fixture, body);
        Assert.AreEqual(1, fixture.CompletedDiagnostics);
    }

    [TestMethod]
    [DataRow("text", false, CurrentModel)]
    [DataRow("advanced", false, OlderModel)]
    [DataRow("run", false, CurrentModel)]
    [DataRow("advanced", true, CurrentModel)]
    [DataRow("run", true, CurrentModel)]
    public async Task AsyncOnlyResponse_CallerCancellationDoesNotEscapeFromDisposalCallbacks(
        string entry, bool httpError, string model)
    {
        var body = new AsyncOnlyBody(httpError ? "{\"error\":" : TextPrefix(model), stall: true);
        using var fixture = new Fixture(body, httpError ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, model);
        using var caller = new CancellationTokenSource();
        var observed = new List<StreamingContent>();
        var operation = ObserveAsync(fixture.Service, entry, observed, caller.Token);
        try
        {
            await body.PendingRead.Task.WaitAsync(Deadline);
            // In particular this must not throw AggregateException from a registered response.Dispose.
            caller.Cancel();
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(Deadline));

            if (entry != "run") Assert.AreEqual(caller.Token, exception.CancellationToken);
            Assert.IsTrue(operation.IsCanceled);
            Assert.AreEqual(httpError ? "" : "answer", Text(observed));
            AssertNoAssistant(fixture.Service);
            AssertCleanup(fixture, body);
        }
        finally
        {
            body.Release();
            await DrainAsync(operation);
        }
    }

    [TestMethod]
    public async Task AsyncOnlyHttpError_CancellationAfterHeadersStillCleansUpAvailableBody()
    {
        var body = new AsyncOnlyBody(ErrorBody);
        using var fixture = new Fixture(body, HttpStatusCode.ServiceUnavailable);
        using var caller = new CancellationTokenSource();
        fixture.Handler.BeforeFirstResponse = caller.Cancel;
        var observed = new List<StreamingContent>();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ObserveAsync(fixture.Service, "advanced", observed, caller.Token).WaitAsync(Deadline));

        Assert.AreEqual(caller.Token, exception.CancellationToken);
        Assert.AreEqual(0, observed.Count);
        AssertCleanup(fixture, body);
        AssertNoAssistant(fixture.Service);
    }

    [TestMethod]
    [DataRow("callback", false)]
    [DataRow("run", false)]
    [DataRow("advanced", true)]
    [DataRow("run", true)]
    public async Task AsyncOnlyResponse_PolicyTimeoutRetainsTimeoutClassification(string entry, bool httpError)
    {
        var body = new AsyncOnlyBody(httpError ? "{\"error\":" : TextPrefix(CurrentModel), stall: true);
        using var fixture = new Fixture(body, httpError ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        fixture.Service.DefaultPolicy.TimeoutSeconds = 1;
        using var caller = new CancellationTokenSource();
        var operation = ObserveAsync(fixture.Service, entry, new List<StreamingContent>(), caller.Token);
        try
        {
            await body.PendingRead.Task.WaitAsync(Deadline);
            var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => operation.WaitAsync(Deadline));

            Assert.AreEqual("Request timeout after 1 seconds", exception.Message);
            Assert.IsInstanceOfType<OperationCanceledException>(exception.InnerException);
            Assert.IsFalse(caller.IsCancellationRequested);
            Assert.IsTrue(operation.IsFaulted);
            AssertNoAssistant(fixture.Service);
            AssertCleanup(fixture, body);
        }
        finally
        {
            body.Release();
            await DrainAsync(operation);
        }
    }

    [TestMethod]
    [DataRow("advanced", "charset")]
    [DataRow("advanced", "bom")]
    [DataRow("text", "charset")]
    [DataRow("callback", "charset")]
    [DataRow("run", "charset")]
    public async Task AsyncOnlyHttpError_PreservesStatusAndDecodedBody(string entry, string encodingKind)
    {
        var encoding = encodingKind == "bom" ? Encoding.UTF8 : Encoding.Unicode;
        var bytes = encodingKind == "bom"
            ? encoding.GetPreamble().Concat(encoding.GetBytes(ErrorBody)).ToArray()
            : encoding.GetBytes(ErrorBody);
        var body = new AsyncOnlyBody(bytes);
        using var fixture = new Fixture(body, HttpStatusCode.ServiceUnavailable);
        fixture.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = encodingKind == "charset" ? encoding.WebName : null
        };
        var observed = new List<StreamingContent>();

        if (entry == "advanced")
        {
            await ObserveAsync(fixture.Service, entry, observed).WaitAsync(Deadline);
            var error = observed.Single();
            Assert.AreEqual(StreamingContentType.Error, error.Type);
            Assert.AreEqual($"API error (503): {ErrorBody}", error.Content);
            Assert.IsNotNull(error.Metadata);
            Assert.AreEqual(503, error.Metadata["status_code"]);
            Assert.AreEqual(ErrorBody, error.Metadata["error"]);
        }
        else
        {
            var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
                ObserveAsync(fixture.Service, entry, observed).WaitAsync(Deadline));
            Assert.AreEqual($"API error (503): {ErrorBody}", exception.Message);
            Assert.AreEqual("Anthropic", exception.ServiceName);
            using var details = JsonDocument.Parse(exception.ErrorDetails!);
            Assert.AreEqual(503, details.RootElement.GetProperty("status_code").GetInt32());
            Assert.AreEqual(ErrorBody, details.RootElement.GetProperty("error").GetString());
            Assert.AreEqual(0, observed.Count);
        }

        AssertCleanup(fixture, body);
        AssertNoAssistant(fixture.Service);
    }

    [TestMethod]
    [DataRow("text")]
    [DataRow("advanced")]
    public async Task AsyncOnlyResponse_EarlyIteratorBreakAwaitsCleanup(string entry)
    {
        var body = new AsyncOnlyBody(TextPrefix(CurrentModel), stall: true, gateCleanup: true);
        using var fixture = new Fixture(body);
        async Task ReadOneAsync()
        {
            if (entry == "text")
            {
                await foreach (var text in fixture.Service.StreamAsync("question"))
                {
                    Assert.AreEqual("answer", text);
                    break;
                }
            }
            else
            {
                await foreach (var content in fixture.Service.StreamAsync("question", StreamOptions.TextOnlyOptions))
                {
                    if (content.Type != StreamingContentType.Text) continue;
                    Assert.AreEqual("answer", content.Content);
                    break;
                }
            }
        }
        var operation = ReadOneAsync();
        try
        {
            await body.CleanupStarted.Task.WaitAsync(Deadline);
            Assert.IsFalse(body.CleanupFinished.Task.IsCompleted);
            Assert.IsFalse(operation.IsCompleted, "Iterator disposal must await asynchronous response cleanup.");
        }
        finally { body.Release(); }
        await operation.WaitAsync(Deadline);

        AssertCleanup(fixture, body);
        AssertNoAssistant(fixture.Service);
        Assert.AreEqual(1, fixture.CompletedDiagnostics);
    }

    [TestMethod]
    [DataRow("success")]
    [DataRow("read-failure")]
    [DataRow("caller-cancel")]
    public async Task RunResult_AwaitsCleanupThenAllowsReuseBeforeOldHandleDisposal(string outcome)
    {
        var cause = outcome == "read-failure" ? new IOException("Original run read failure") : null;
        var body = new AsyncOnlyBody(outcome == "success" ? SuccessBody(CurrentModel) : TextPrefix(CurrentModel),
            cause, stall: outcome == "caller-cancel", gateCleanup: true);
        using var fixture = new Fixture(body);
        using var caller = new CancellationTokenSource();
        var run = await fixture.Service.StartRunAsync("question", cancellationToken: caller.Token).WaitAsync(Deadline);
        try
        {
            if (outcome == "caller-cancel")
            {
                await body.PendingRead.Task.WaitAsync(Deadline);
                caller.Cancel();
            }
            await body.CleanupStarted.Task.WaitAsync(Deadline);
            Assert.IsFalse(run.Result.IsCompleted, "Result must remain pending until async disposal finishes.");
            Assert.Throws<InvalidOperationException>(() => fixture.Service.StartRunAsync("overlap"));
            body.Release();

            if (outcome == "success")
                Assert.AreEqual("answer", (await run.Result.WaitAsync(Deadline)).Text);
            else if (outcome == "read-failure")
            {
                var failure = await Assert.ThrowsExactlyAsync<StreamReadException>(() => run.Result.WaitAsync(Deadline));
                Assert.AreSame(cause, failure.InnerException);
                var outputFailure = await Assert.ThrowsExactlyAsync<StreamReadException>(() => ReadOutputAsync(run).WaitAsync(Deadline));
                Assert.AreSame(failure, outputFailure);
            }
            else
            {
                await Assert.ThrowsAsync<OperationCanceledException>(() => run.Result.WaitAsync(Deadline));
                Assert.IsTrue(run.Result.IsCanceled);
            }

            AssertCleanup(fixture, body);
            var assistants = outcome == "success" ? 1 : 0;
            Assert.AreEqual(assistants, fixture.Service.ActivateChat.Messages.Count(m => m.Role == ActorRole.Assistant));
            await using var next = await fixture.Service.StartRunAsync("retry after cleanup").WaitAsync(Deadline);
            Assert.AreEqual("answer", (await next.Result.WaitAsync(Deadline)).Text);
            Assert.AreEqual(2, fixture.Handler.Requests);
            Assert.AreEqual(assistants + 1, fixture.Service.ActivateChat.Messages.Count(m => m.Role == ActorRole.Assistant));
        }
        finally
        {
            body.Release();
            await run.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
    }

    [TestMethod]
    [DataRow("text", false)]
    [DataRow("run", false)]
    [DataRow("advanced", true)]
    public async Task ResponseStreamAcquisitionFailure_PreservesCauseAndAttemptsOwnerCleanup(string entry, bool httpError)
    {
        var cause = new IOException("Original stream acquisition failure");
        var content = new FailingContent(cause);
        using var fixture = new Fixture(content, httpError ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        var observed = new List<StreamingContent>();

        var exception = await Assert.ThrowsExactlyAsync<IOException>(() =>
            ObserveAsync(fixture.Service, entry, observed).WaitAsync(Deadline));

        Assert.AreSame(cause, exception);
        Assert.IsTrue(content.IsDisposed);
        Assert.IsTrue(fixture.Response.IsDisposed);
        Assert.AreEqual(1, content.AcquisitionAttempts);
        Assert.AreEqual(0, observed.Count);
        AssertNoAssistant(fixture.Service);
    }

    private static async Task ObserveAsync(AnthropicService service, string entry,
        List<StreamingContent> observed, CancellationToken cancellationToken = default)
    {
        void OnText(string text) => observed.Add(new StreamingContent { Type = StreamingContentType.Text, Content = text });
        switch (entry)
        {
            case "text":
                await foreach (var text in service.StreamAsync("question", cancellationToken)) OnText(text);
                break;
            case "advanced":
                await foreach (var item in service.StreamAsync("question", StreamOptions.TextOnlyOptions, cancellationToken)) observed.Add(item);
                break;
            case "callback":
                await service.StreamCompletionAsync("question", OnText);
                break;
            case "run":
                await using (var run = await service.StartRunAsync("question", OnText, cancellationToken: cancellationToken).WaitAsync(Deadline))
                    Assert.AreEqual("answer", (await run.Result.WaitAsync(Deadline)).Text);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(entry));
        }
    }

    private static async Task ReadOutputAsync(AIRun run)
    {
        await foreach (var _ in run.StreamAsync()) { }
    }

    private static async Task DrainAsync(Task operation)
    {
        try { await operation.WaitAsync(Deadline); }
        catch (Exception) when (operation.IsCompleted) { }
    }

    private static string Text(IEnumerable<StreamingContent> observed) =>
        string.Concat(observed.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content));

    private static void AssertNoAssistant(AnthropicService service) =>
        Assert.IsFalse(service.ActivateChat.Messages.Any(m => m.Role == ActorRole.Assistant ||
            m.FunctionCallBatch != null || m.FunctionCallResultBatch != null));

    private static void AssertCleanup(Fixture fixture, AsyncOnlyBody body)
    {
        Assert.AreEqual(1, body.AsyncDisposeCalls, "One response stream must have one async cleanup owner.");
        Assert.IsTrue(body.CleanupFinished.Task.IsCompleted, "Cleanup must finish before the operation settles.");
        Assert.IsTrue(fixture.Response.IsDisposed, "The HTTP response owner must still be disposed.");
        Assert.IsTrue(((TrackedContent)fixture.Content).IsDisposed, "The HTTP content owner must still be disposed.");
        // HttpContent may synchronously dispose its cached stream even after DisposeAsync.
        // Its unsupported-disposal failure must be guarded, not assumed impossible.
    }

    private static string TextPrefix(string model) => string.Join("\n\n", new[]
    {
        "data: " + JsonSerializer.Serialize(new { type = "message_start", message = new { id = "msg-cleanup", role = "assistant", model, content = Array.Empty<object>() } }),
        """data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
        """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"answer"}}"""
    }) + "\n\n";

    private static string SuccessBody(string model) => TextPrefix(model) +
        """data: {"type":"content_block_stop","index":0}""" + "\n\n" +
        """data: {"type":"message_delta","delta":{"stop_reason":"end_turn"}}""" + "\n\n" +
        """data: {"type":"message_stop"}""" + "\n\n";

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _client;
        private int _completedDiagnostics;
        public TrackedResponse Response { get; }
        public HttpContent Content { get; }
        public Handler Handler { get; }
        public AnthropicService Service { get; }
        public int CompletedDiagnostics => Volatile.Read(ref _completedDiagnostics);

        public Fixture(Stream body, HttpStatusCode status = HttpStatusCode.OK, string model = CurrentModel)
            : this(new TrackedContent(body), status, model) { }

        public Fixture(HttpContent content, HttpStatusCode status, string model = CurrentModel)
        {
            Content = content;
            Response = new TrackedResponse(status) { Content = content };
            Handler = new Handler(Response, model);
            _client = new HttpClient(Handler);
            Service = new AnthropicService("offline-key", model, _client)
            {
                DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = null, MaxRounds = 3, EnableLogging = false }
            };
            Service.WithStreamDiagnostics(d => d.OnComplete(_ =>
            {
                Interlocked.Increment(ref _completedDiagnostics);
                throw new InvalidOperationException("A diagnostic callback must not break cleanup");
            }));
        }

        public void Dispose()
        {
            try { Response.Dispose(); }
            catch (NotSupportedException) { /* The fixture deliberately rejects synchronous disposal. */ }
            _client.Dispose();
        }
    }

    private sealed class Handler(HttpResponseMessage firstResponse, string model) : HttpMessageHandler
    {
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        public Action? BeforeFirstResponse { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requests) == 1)
            {
                BeforeFirstResponse?.Invoke();
                return Task.FromResult(firstResponse);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessBody(model), Encoding.UTF8, "text/event-stream")
            });
        }
    }

    private sealed class TrackedResponse(HttpStatusCode status) : HttpResponseMessage(status)
    {
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackedContent(Stream body) : StreamContent(body)
    {
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class FailingContent(IOException cause) : HttpContent
    {
        public int AcquisitionAttempts { get; private set; }
        public bool IsDisposed { get; private set; }
        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            AcquisitionAttempts++;
            return Task.FromException<Stream>(cause);
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.FromException(cause);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
            throw new NotSupportedException("Content cleanup also fails");
        }
    }

    /// <summary>A supported custom transport shape: async cleanup works, synchronous cleanup is unsupported.</summary>
    private sealed class AsyncOnlyBody : Stream
    {
        private readonly byte[] _bytes;
        private readonly IOException? _readFailure;
        private readonly bool _stall;
        private readonly TaskCompletionSource _releaseRead = Signal();
        private readonly TaskCompletionSource _releaseCleanup = Signal();
        private int _offset;
        private int _asyncDisposeCalls;
        public TaskCompletionSource PendingRead { get; } = Signal();
        public TaskCompletionSource CleanupStarted { get; } = Signal();
        public TaskCompletionSource CleanupFinished { get; } = Signal();
        public int AsyncDisposeCalls => Volatile.Read(ref _asyncDisposeCalls);
        public bool ThrowAsyncDispose { get; init; }

        public AsyncOnlyBody(string text, IOException? readFailure = null, bool stall = false, bool gateCleanup = false)
            : this(Encoding.UTF8.GetBytes(text), readFailure, stall, gateCleanup) { }

        public AsyncOnlyBody(byte[] bytes, IOException? readFailure = null, bool stall = false, bool gateCleanup = false)
        {
            _bytes = bytes;
            _readFailure = readFailure;
            _stall = stall;
            if (!gateCleanup) _releaseCleanup.TrySetResult();
        }

        public void Release()
        {
            _releaseRead.TrySetResult();
            _releaseCleanup.TrySetResult();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset < _bytes.Length)
            {
                var count = Math.Min(buffer.Length, _bytes.Length - _offset);
                _bytes.AsMemory(_offset, count).CopyTo(buffer);
                _offset += count;
                return count;
            }
            if (_readFailure != null) throw _readFailure;
            if (_stall)
            {
                PendingRead.TrySetResult();
                await _releaseRead.Task.WaitAsync(cancellationToken);
            }
            return 0;
        }

        public override async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _asyncDisposeCalls);
            CleanupStarted.TrySetResult();
            _releaseRead.TrySetResult();
            await _releaseCleanup.Task;
            CleanupFinished.TrySetResult();
            if (ThrowAsyncDispose) throw new NotSupportedException("Asynchronous cleanup failed too");
        }

        protected override void Dispose(bool disposing) => throw new NotSupportedException("Synchronous cleanup is unsupported");
        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
