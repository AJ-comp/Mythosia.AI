using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Test.Streaming;

[TestClass]
[TestCategory("Unit")]
public class SseHelperDiagnosticsTests
{
    [TestMethod]
    public async Task ReadSseLines_HappyPath_ReportsLinesReadAndCallsCallback()
    {
        var body = "data: line1\ndata: line2\ndata: [DONE]\n";
        var response = MakeSseResponse(body);

        StreamDiagnostics? captured = null;
        var service = MakeService().WithStreamDiagnostics(d => d.OnComplete(diag => captured = diag));
        var diagnostics = new StreamDiagnostics();

        var lines = new List<string>();
        await foreach (var line in service.ReadSseLinesAsync(response, diagnostics, default))
            lines.Add(line);

        Assert.AreEqual(3, lines.Count);
        Assert.AreEqual(3, diagnostics.LinesRead);
        Assert.AreEqual("data: [DONE]", diagnostics.LastRawLine);
        Assert.IsNotNull(captured);
        Assert.AreSame(diagnostics, captured);
    }

    [TestMethod]
    public async Task WithStreamDiagnostics_OnRawLine_FiresPerLine()
    {
        var body = "data: a\ndata: b\ndata: c\n";
        var response = MakeSseResponse(body);

        var traced = new List<string>();
        var service = MakeService().WithStreamDiagnostics(d => d.OnRawLine(traced.Add));

        var diagnostics = new StreamDiagnostics();
        await foreach (var _ in service.ReadSseLinesAsync(response, diagnostics, default)) { }

        CollectionAssert.AreEqual(new[] { "data: a", "data: b", "data: c" }, traced);
    }

    [TestMethod]
    public async Task WithStreamDiagnostics_OnRawLineOnly_DoesNotRequireOnComplete()
    {
        var body = "data: x\n";
        var response = MakeSseResponse(body);

        int rawCalls = 0;
        var service = MakeService().WithStreamDiagnostics(d => d.OnRawLine(_ => rawCalls++));

        var diagnostics = new StreamDiagnostics();
        await foreach (var _ in service.ReadSseLinesAsync(response, diagnostics, default)) { }

        Assert.AreEqual(1, rawCalls);
        // OnComplete was not registered → no error, just nothing fires.
    }

    [TestMethod]
    public async Task WithStreamDiagnostics_OnCompleteOnly_DoesNotRequireOnRawLine()
    {
        var body = "data: x\ndata: y\n";
        var response = MakeSseResponse(body);

        int completeCalls = 0;
        var service = MakeService().WithStreamDiagnostics(d => d.OnComplete(_ => completeCalls++));

        var diagnostics = new StreamDiagnostics();
        await foreach (var _ in service.ReadSseLinesAsync(response, diagnostics, default)) { }

        Assert.AreEqual(1, completeCalls);
    }

    [TestMethod]
    public async Task WithStreamDiagnostics_RawLineCallbackThrows_DoesNotBreakStream()
    {
        var body = "data: a\ndata: b\ndata: c\n";
        var response = MakeSseResponse(body);

        var service = MakeService().WithStreamDiagnostics(d =>
            d.OnRawLine(_ => throw new InvalidOperationException("boom")));

        var diagnostics = new StreamDiagnostics();
        var lines = new List<string>();
        await foreach (var line in service.ReadSseLinesAsync(response, diagnostics, default))
            lines.Add(line);

        Assert.AreEqual(3, lines.Count);
    }

    [TestMethod]
    public async Task WithStreamDiagnostics_OnComplete_FiresOnceOnNormalCompletion()
    {
        var body = "data: x\n";
        var response = MakeSseResponse(body);

        int callCount = 0;
        var service = MakeService().WithStreamDiagnostics(d => d.OnComplete(_ => callCount++));

        var diagnostics = new StreamDiagnostics();
        await foreach (var _ in service.ReadSseLinesAsync(response, diagnostics, default)) { }

        Assert.AreEqual(1, callCount);
    }

    [TestMethod]
    public async Task ReadSseLines_NonIOExceptionDuringRead_AlsoWrappedInStreamReadException()
    {
        // The catch is broad (Exception), not just IOException — any read-time failure
        // surfaces as StreamReadException.
        var content = new StreamContent(new ThrowingStream(throwAfterBytes: 5, ex: () =>
            new NotSupportedException("Specified method is not supported.")));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        var service = MakeService();
        var diagnostics = new StreamDiagnostics();

        var ex = await Assert.ThrowsExactlyAsync<StreamReadException>(async () =>
        {
            await foreach (var _ in service.ReadSseLinesAsync(response, diagnostics, default)) { }
        });

        Assert.IsInstanceOfType<NotSupportedException>(ex.InnerException);
        StringAssert.Contains(ex.Message, "NotSupportedException");
    }

    [TestMethod]
    public async Task ReadSseLines_ReadException_StillFiresOnCompleteEvenIfDisposalFails()
    {
        // Both read and Dispose fail. OnComplete must still fire and the original
        // read exception must reach the caller (not be masked by disposal failure).
        var content = new StreamContent(new DisposeFailingStream(throwAfterBytes: 5));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        int completeCalls = 0;
        var service = MakeService().WithStreamDiagnostics(d => d.OnComplete(_ => completeCalls++));
        var diagnostics = new StreamDiagnostics();

        var ex = await Assert.ThrowsExactlyAsync<StreamReadException>(async () =>
        {
            await foreach (var _ in service.ReadSseLinesAsync(response, diagnostics, default)) { }
        });

        Assert.AreEqual(1, completeCalls, "OnComplete must fire even when disposal also fails");
        Assert.IsInstanceOfType<IOException>(ex.InnerException);
    }

    [TestMethod]
    public async Task ReadSseLines_ReadException_WrapsInStreamReadExceptionWithDiagnostics()
    {
        var content = new StreamContent(new ThrowingStream(throwAfterBytes: 20));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        var service = MakeService();
        var diagnostics = new StreamDiagnostics();

        var ex = await Assert.ThrowsExactlyAsync<StreamReadException>(async () =>
        {
            await foreach (var _ in service.ReadSseLinesAsync(response, diagnostics, default)) { }
        });

        Assert.IsNotNull(ex.Diagnostics);
        Assert.AreSame(diagnostics, ex.Diagnostics);
        Assert.IsNotNull(ex.InnerException);
        Assert.IsInstanceOfType<IOException>(ex.InnerException);
    }

    [TestMethod]
    public async Task WithStreamDiagnostics_OnComplete_FiresOnceOnException()
    {
        var content = new StreamContent(new ThrowingStream(throwAfterBytes: 10));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        int callCount = 0;
        var service = MakeService().WithStreamDiagnostics(d => d.OnComplete(_ => callCount++));
        var diagnostics = new StreamDiagnostics();

        try
        {
            await foreach (var _ in service.ReadSseLinesAsync(response, diagnostics, default)) { }
        }
        catch (StreamReadException) { /* expected */ }

        Assert.AreEqual(1, callCount);
    }

    [TestMethod]
    public async Task ReadSseLines_CancellationToken_HonoredBetweenReads()
    {
        var body = string.Concat(System.Linq.Enumerable.Repeat("data: x\n", 100));
        var response = MakeSseResponse(body);

        using var cts = new CancellationTokenSource();
        var service = MakeService();
        var diagnostics = new StreamDiagnostics();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in service.ReadSseLinesAsync(response, diagnostics, cts.Token))
            {
                cts.Cancel();
            }
        });
    }

    [TestMethod]
    public async Task ReadSseLines_ElapsedAndLastRawLine_PopulatedOnException()
    {
        var content = new StreamContent(new PartialThenThrowStream("data: alpha\ndata: beta\n"));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        var service = MakeService();
        var diagnostics = new StreamDiagnostics();

        try
        {
            await foreach (var _ in service.ReadSseLinesAsync(response, diagnostics, default)) { }
            Assert.Fail("expected StreamReadException");
        }
        catch (StreamReadException)
        {
            Assert.IsTrue(diagnostics.LinesRead >= 2, $"expected ≥2 lines read, got {diagnostics.LinesRead}");
            Assert.IsNotNull(diagnostics.LastRawLine);
            StringAssert.StartsWith(diagnostics.LastRawLine, "data:");
            Assert.IsTrue(diagnostics.Elapsed > TimeSpan.Zero);
        }
    }

    [TestMethod]
    public void WithStreamDiagnostics_PassedEmptyConfigure_ClearsCallbacks()
    {
        var service = MakeService()
            .WithStreamDiagnostics(d => d.OnRawLine(_ => { }).OnComplete(_ => { }));
        Assert.IsNotNull(service.StreamRawLineCallback);
        Assert.IsNotNull(service.StreamCompleteCallback);

        // Re-applying with empty config clears both.
        service.WithStreamDiagnostics(_ => { });
        Assert.IsNull(service.StreamRawLineCallback);
        Assert.IsNull(service.StreamCompleteCallback);
    }

    [TestMethod]
    public void CopyFrom_PropagatesStreamDiagnosticsCallbacks_ByReference()
    {
        Action<string> rawCallback = _ => { };
        Action<StreamDiagnostics> completeCallback = _ => { };

        var source = MakeService()
            .WithStreamDiagnostics(d => d.OnRawLine(rawCallback).OnComplete(completeCallback));

        var target = MakeService();
        Assert.IsNull(target.StreamRawLineCallback);
        Assert.IsNull(target.StreamCompleteCallback);

        target.CopyFrom(source);

        // Callbacks must be propagated as the same delegate references.
        Assert.AreSame(source.StreamRawLineCallback, target.StreamRawLineCallback);
        Assert.AreSame(source.StreamCompleteCallback, target.StreamCompleteCallback);
    }

    [TestMethod]
    public async Task CopyFrom_PropagatedCallback_FiresOnTargetServiceStream()
    {
        var rawLines = new List<string>();
        var source = MakeService().WithStreamDiagnostics(d => d.OnRawLine(rawLines.Add));

        var target = MakeService();
        target.CopyFrom(source);

        // Target streams independently — callback registered via source must fire here too.
        var response = MakeSseResponse("data: a\ndata: b\n");
        var diagnostics = new StreamDiagnostics();
        await foreach (var _ in target.ReadSseLinesAsync(response, diagnostics, default)) { }

        CollectionAssert.AreEqual(new[] { "data: a", "data: b" }, rawLines);
    }

    [TestMethod]
    public void CopyFrom_NoDiagnosticsOnSource_LeavesTargetUnchanged()
    {
        var source = MakeService();              // no diagnostics
        var target = MakeService().WithStreamDiagnostics(d => d.OnRawLine(_ => { }));

        target.CopyFrom(source);

        // Source had nulls → target's previously-registered callbacks are now also null.
        // (CopyFrom is replacement semantics, consistent with WithStreamDiagnostics(_ => {}).)
        Assert.IsNull(target.StreamRawLineCallback);
        Assert.IsNull(target.StreamCompleteCallback);
    }

    // ---------- helpers ----------

    private static FakeService MakeService() =>
        new FakeService("test-key", "https://example.invalid/", new HttpClient());

    private static HttpResponseMessage MakeSseResponse(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var ms = new MemoryStream(bytes);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(ms),
        };
    }

    /// <summary>
    /// Minimal AIService subclass for testing the SSE helper in isolation.
    /// All abstract members are stubbed — none are invoked by the helper tests.
    /// </summary>
    private sealed class FakeService : AIService
    {
        public FakeService(string apiKey, string baseUrl, HttpClient httpClient)
            : base(apiKey, baseUrl, httpClient) { }

        public override string Provider => "Fake";

        public override Task<string> GetCompletionAsync(Message message)
            => throw new NotImplementedException();

        public override Task<uint> GetInputTokenCountAsync()
            => throw new NotImplementedException();

        public override Task<uint> GetInputTokenCountAsync(string prompt)
            => throw new NotImplementedException();

        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => throw new NotImplementedException();

        protected override HttpRequestMessage CreateMessageRequest()
            => throw new NotImplementedException();

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => throw new NotImplementedException();

        protected override string StreamParseJson(string jsonData)
            => throw new NotImplementedException();

        protected override string ExtractResponseContent(string responseContent)
            => throw new NotImplementedException();

        protected override (string content, Mythosia.AI.Models.Functions.FunctionCallBatch functionCalls) ExtractFunctionCalls(string responseContent)
            => throw new NotImplementedException();
    }

    private sealed class ThrowingStream : Stream
    {
        private int _readBytes;
        private readonly int _throwAfter;
        private readonly Func<Exception> _exceptionFactory;

        public ThrowingStream(int throwAfterBytes, Func<Exception>? ex = null)
        {
            _throwAfter = throwAfterBytes;
            _exceptionFactory = ex ?? (() => new IOException("The response ended prematurely."));
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _readBytes;
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_readBytes >= _throwAfter)
                throw _exceptionFactory();

            int toWrite = Math.Min(count, _throwAfter - _readBytes);
            for (int i = 0; i < toWrite; i++) buffer[offset + i] = (byte)'x';
            _readBytes += toWrite;
            return toWrite;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer, offset, count));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Stream that throws on Read and ALSO throws on Dispose / DisposeAsync.
    /// Reproduces the worst case: a transport that ends prematurely AND has a
    /// broken sync Dispose path. The library must surface the read exception,
    /// not the disposal exception, and must still fire OnComplete.
    /// </summary>
    private sealed class DisposeFailingStream : Stream
    {
        private int _readBytes;
        private readonly int _throwAfter;

        public DisposeFailingStream(int throwAfterBytes) { _throwAfter = throwAfterBytes; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _readBytes;
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_readBytes >= _throwAfter)
                throw new IOException("The response ended prematurely.");
            int toWrite = Math.Min(count, _throwAfter - _readBytes);
            for (int i = 0; i < toWrite; i++) buffer[offset + i] = (byte)'x';
            _readBytes += toWrite;
            return toWrite;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer, offset, count));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            throw new NotSupportedException("Specified method is not supported.");
        }
        public override ValueTask DisposeAsync()
        {
            throw new NotSupportedException("Specified method is not supported.");
        }
    }

    private sealed class PartialThenThrowStream : Stream
    {
        private readonly byte[] _prefix;
        private int _pos;

        public PartialThenThrowStream(string prefix)
        {
            _prefix = Encoding.UTF8.GetBytes(prefix);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _pos;
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _prefix.Length)
                throw new IOException("The response ended prematurely.");

            int toWrite = Math.Min(count, _prefix.Length - _pos);
            Array.Copy(_prefix, _pos, buffer, offset, toWrite);
            _pos += toWrite;
            return toWrite;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer, offset, count));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
