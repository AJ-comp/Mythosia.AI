using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
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
public class AnthropicErrorBodyCancellationTests
{
    private const string CurrentModel = "claude-sonnet-5-5";
    private const string OlderModel = "claude-sonnet-4-20250514";
    private const string ErrorBody = """{"error":{"type":"overloaded_error","message":"응답 café unavailable"}}""";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [TestMethod]
    [DataRow("text", CurrentModel)]
    [DataRow("advanced", CurrentModel)]
    [DataRow("text", OlderModel)]
    [DataRow("advanced", OlderModel)]
    public async Task StreamCallerCancellation_AbortsStalledErrorBodyAndPreservesCallerToken(string entry, string model)
    {
        using var body = new StalledBody();
        using var fixture = new Fixture(body, HttpStatusCode.ServiceUnavailable, model);
        using var caller = new CancellationTokenSource();
        var observed = new List<StreamingContent>();
        var operation = ObserveAsync(fixture.Service, entry, observed, caller.Token);
        try
        {
            await body.ReadStarted.Task.WaitAsync(Deadline);
            caller.Cancel();
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(Deadline));

            Assert.AreEqual(caller.Token, exception.CancellationToken);
            AssertCancelledResponse(fixture, body);
            Assert.AreEqual(0, observed.Count, "An interrupted error body must not become an error, completion, or text event.");
        }
        finally
        {
            caller.Cancel();
            body.Release();
            await DrainAsync(operation);
        }
    }

    [TestMethod]
    [DataRow("caller", CurrentModel)]
    [DataRow("cancel", CurrentModel)]
    [DataRow("dispose", CurrentModel)]
    [DataRow("caller", OlderModel)]
    public async Task RunCancellation_DrainsStalledErrorBodyAndAllowsNextRun(string cancellation, string model)
    {
        // The disposal case also models a transport whose pending read ignores cancellation tokens.
        using var body = new StalledBody(honorCancellation: cancellation != "dispose");
        using var fixture = new Fixture(body, HttpStatusCode.ServiceUnavailable, model);
        using var caller = new CancellationTokenSource();
        var text = new List<string>();
        var run = await fixture.Service.StartRunAsync("question", text.Add, cancellationToken: caller.Token).WaitAsync(Deadline);
        Task? disposing = null;
        try
        {
            await body.ReadStarted.Task.WaitAsync(Deadline);
            if (cancellation == "caller") caller.Cancel();
            else if (cancellation == "cancel") run.Cancel();
            else disposing = run.DisposeAsync().AsTask();

            await Assert.ThrowsAsync<OperationCanceledException>(() => run.Result.WaitAsync(Deadline));
            if (disposing != null) await disposing.WaitAsync(Deadline);
            Assert.IsTrue(run.Result.IsCanceled);
            AssertCancelledResponse(fixture, body);
            Assert.AreEqual(0, text.Count);

            // Result is settled only after the provider has released this service's active-run slot.
            await AssertNextRunAsync(fixture);
        }
        finally
        {
            caller.Cancel();
            body.Release();
            await DrainAsync(run.Result);
            await (disposing ?? run.DisposeAsync().AsTask()).WaitAsync(Deadline);
        }
    }

    [TestMethod]
    [DataRow("text")]
    [DataRow("advanced")]
    [DataRow("callback")]
    [DataRow("run")]
    public async Task PolicyTimeout_AbortsStalledErrorBodyWithoutBecomingCallerCancellation(string entry)
    {
        using var body = new StalledBody();
        using var fixture = new Fixture(body, HttpStatusCode.ServiceUnavailable);
        fixture.Service.DefaultPolicy.TimeoutSeconds = 1;
        using var caller = new CancellationTokenSource();
        var observed = new List<StreamingContent>();
        AIRun? run = null;
        Task operation;
        if (entry == "run")
        {
            run = await fixture.Service.StartRunAsync("question", cancellationToken: caller.Token).WaitAsync(Deadline);
            operation = run.Result;
        }
        else operation = ObserveAsync(fixture.Service, entry, observed, caller.Token);

        try
        {
            await body.ReadStarted.Task.WaitAsync(Deadline);
            var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => operation.WaitAsync(Deadline));

            StringAssert.Contains(exception.Message, "Request timeout after 1 seconds");
            Assert.IsInstanceOfType<OperationCanceledException>(exception.InnerException);
            Assert.IsFalse(caller.IsCancellationRequested);
            Assert.IsFalse(operation.IsCanceled, "Policy expiry is a timeout failure, not caller cancellation.");
            AssertCancelledResponse(fixture, body);
            Assert.AreEqual(0, observed.Count);
            if (run != null)
            {
                fixture.Service.DefaultPolicy.TimeoutSeconds = null;
                await AssertNextRunAsync(fixture);
            }
        }
        finally
        {
            caller.Cancel();
            body.Release();
            await DrainAsync(operation);
            if (run != null) await run.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
    }

    [TestMethod]
    [DataRow("advanced", "charset")]
    [DataRow("advanced", "bom")]
    [DataRow("text", "charset")]
    [DataRow("callback", "charset")]
    [DataRow("run", "charset")]
    public async Task CompleteHttpError_PreservesStatusDecodedBodyAndFailureContract(string entry, string encodingKind)
    {
        var encoding = encodingKind == "bom" ? Encoding.UTF8 : Encoding.Unicode;
        var bytes = encodingKind == "bom"
            ? encoding.GetPreamble().Concat(encoding.GetBytes(ErrorBody)).ToArray()
            : encoding.GetBytes(ErrorBody);
        using var body = new MemoryStream(bytes);
        using var fixture = new Fixture(body, HttpStatusCode.ServiceUnavailable);
        fixture.Response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
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
            var exception = await Assert.ThrowsAsync<AIServiceException>(() =>
                ObserveAsync(fixture.Service, entry, observed).WaitAsync(Deadline));
            Assert.AreEqual($"API error (503): {ErrorBody}", exception.Message);
            Assert.AreEqual("Anthropic", exception.ServiceName);
            using var details = JsonDocument.Parse(exception.ErrorDetails!);
            Assert.AreEqual(503, details.RootElement.GetProperty("status_code").GetInt32());
            Assert.AreEqual(ErrorBody, details.RootElement.GetProperty("error").GetString());
            Assert.AreEqual(0, observed.Count);
        }
        AssertResponseDisposed(fixture);
        AssertNoGeneratedMessages(fixture.Service);
        Assert.AreEqual(1, fixture.Handler.Requests);
    }

    [TestMethod]
    [DataRow("text")]
    [DataRow("advanced")]
    [DataRow("callback")]
    [DataRow("run")]
    public async Task SuccessfulResponse_StillCompletesAndSavesAssistantMessage(string entry)
    {
        using var body = new MemoryStream(Encoding.UTF8.GetBytes(SuccessBody));
        using var fixture = new Fixture(body, HttpStatusCode.OK);
        var observed = new List<StreamingContent>();

        await ObserveAsync(fixture.Service, entry, observed).WaitAsync(Deadline);

        Assert.AreEqual("answer", string.Concat(observed.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        Assert.AreEqual("answer", fixture.Service.ActivateChat.Messages.Single(message => message.Role == ActorRole.Assistant).Content);
        Assert.IsFalse(observed.Any(item => item.Type == StreamingContentType.Error));
        Assert.AreEqual(1, fixture.Handler.Requests);
        AssertResponseDisposed(fixture);
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
                await foreach (var content in service.StreamAsync("question", StreamOptions.WithFunctions, cancellationToken)) observed.Add(content);
                break;
            case "callback":
                await service.StreamCompletionAsync("question", OnText);
                break;
            case "run":
                var run = await service.StartRunAsync("question", OnText, cancellationToken: cancellationToken).WaitAsync(Deadline);
                try { Assert.AreEqual("answer", (await run.Result.WaitAsync(Deadline)).Text); }
                finally { await run.DisposeAsync().AsTask().WaitAsync(Deadline); }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(entry));
        }
    }

    private static async Task AssertNextRunAsync(Fixture fixture)
    {
        var next = await fixture.Service.StartRunAsync("retry after cleanup").WaitAsync(Deadline);
        try
        {
            Assert.AreEqual("answer", (await next.Result.WaitAsync(Deadline)).Text);
            Assert.AreEqual(2, fixture.Handler.Requests);
        }
        finally { await next.DisposeAsync().AsTask().WaitAsync(Deadline); }
    }

    private static void AssertCancelledResponse(Fixture fixture, StalledBody body)
    {
        AssertResponseDisposed(fixture);
        Assert.IsTrue(body.IsDisposed, "Cancellation must dispose the pending response stream.");
        Assert.IsTrue(body.ReadToken.IsCancellationRequested, "The pending copy must receive the effective cancellation token.");
        Assert.AreEqual(1, fixture.Handler.Requests);
        AssertNoGeneratedMessages(fixture.Service);
    }

    private static void AssertResponseDisposed(Fixture fixture)
    {
        Assert.IsTrue(fixture.Response.IsDisposed, "The HTTP response must be disposed before the operation settles.");
        Assert.IsTrue(fixture.Content.IsDisposed, "The response content must be released.");
    }

    private static void AssertNoGeneratedMessages(AnthropicService service)
    {
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant ||
            message.FunctionCallBatch != null || message.FunctionCallResultBatch != null),
            "An unsuccessful response must not save assistant output, tool calls, or tool results.");
    }

    private static async Task DrainAsync(Task operation)
    {
        try { await operation.WaitAsync(Deadline); }
        catch (OperationCanceledException) { }
        catch (AIServiceException) { }
    }

    private const string SuccessBody = """
        data: {"type":"message_start","message":{"id":"msg-test","model":"claude-sonnet-5-5","type":"message","role":"assistant","content":[]}}

        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"answer"}}

        data: {"type":"content_block_stop","index":0}

        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"}}

        data: {"type":"message_stop"}


        """;

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _client;
        public TrackedResponse Response { get; }
        public TrackedContent Content { get; }
        public Handler Handler { get; }
        public AnthropicService Service { get; }

        public Fixture(Stream body, HttpStatusCode status, string model = CurrentModel)
        {
            Content = new TrackedContent(body);
            Response = new TrackedResponse(status) { Content = Content };
            Handler = new Handler(Response);
            _client = new HttpClient(Handler);
            Service = new AnthropicService("offline-key", model, _client)
            {
                DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = null, MaxRounds = 3, EnableLogging = false }
            };
        }

        public void Dispose()
        {
            Response.Dispose();
            _client.Dispose();
        }
    }

    private sealed class Handler(HttpResponseMessage firstResponse) : HttpMessageHandler
    {
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Interlocked.Increment(ref _requests) == 1 ? firstResponse : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessBody, Encoding.UTF8, "text/event-stream")
            });
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

    /// <summary>Returns a partial error document, then waits for cancellation, disposal, or explicit test cleanup.</summary>
    private sealed class StalledBody(bool honorCancellation = true) : Stream
    {
        private readonly byte[] _prefix = Encoding.UTF8.GetBytes("{\"error\":");
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _offset;
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ReadToken { get; private set; }
        public bool IsDisposed { get; private set; }
        public void Release() => _release.TrySetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset < _prefix.Length)
            {
                var count = Math.Min(buffer.Length, _prefix.Length - _offset);
                _prefix.AsMemory(_offset, count).CopyTo(buffer);
                _offset += count;
                return count;
            }
            ReadToken = cancellationToken;
            ReadStarted.TrySetResult();
            await _release.Task.WaitAsync(honorCancellation ? cancellationToken : CancellationToken.None);
            return 0;
        }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            Release();
            base.Dispose(disposing);
        }
    }
}
