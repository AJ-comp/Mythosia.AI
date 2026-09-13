using Mythosia.AI.Mcp.Transports;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;

namespace Mythosia.AI.Mcp.Tests;

[TestClass]
public class AdversarialThirdMcpLifecycleTests
{
    [TestMethod]
    public async Task MalformedResponseMustNotForgetTheMatchingPendingRequest()
    {
        var transport = new ScriptedTransport { MalformedResponseThenValid = true };
        await using var connection = new McpConnection(transport);
        using var cancellation = new CancellationTokenSource();
        await transport.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var call = connection.CallToolAsync("lookup", cancellationToken: cancellation.Token);
        try
        {
            Assert.AreEqual("recovered", await call.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            cancellation.Cancel();
            try { await call; } catch (OperationCanceledException) { }
        }
    }

    [TestMethod]
    public async Task DisposalStillSettlesACallAfterAMalformedMatchingResponse()
    {
        var transport = new ScriptedTransport { MalformedResponseOnly = true };
        var connection = new McpConnection(transport);
        using var cancellation = new CancellationTokenSource();
        await transport.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var call = connection.CallToolAsync("lookup", cancellationToken: cancellation.Token);
        try
        {
            // The second read starts only after the first response was fully processed/skipped.
            await transport.SecondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await connection.DisposeAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            cancellation.Cancel();
            try { await call; } catch (OperationCanceledException) { }
            await connection.DisposeAsync();
        }
    }

    [TestMethod]
    [DataRow(false, "call")]
    [DataRow(false, "refresh")]
    [DataRow(false, "initialize")]
    [DataRow(true, "call")]
    [DataRow(true, "refresh")]
    [DataRow(true, "initialize")]
    public async Task OnceDisposalStartsNewOperationsCannotCreateUnsettledRequests(bool cleanupStillRunning, string operation)
    {
        var transport = new ScriptedTransport { HoldDisposal = cleanupStillRunning };
        var connection = new McpConnection(transport);
        using var cancellation = new CancellationTokenSource();
        await transport.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposal = connection.DisposeAsync().AsTask();
        await transport.DisposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!cleanupStillRunning) await disposal;
        Task? request = null;
        try
        {
            request = operation switch
            {
                "initialize" => connection.InitializeAsync(cancellation.Token),
                "refresh" => connection.RefreshToolsAsync(cancellation.Token),
                _ => connection.CallToolAsync("lookup", cancellationToken: cancellation.Token)
            };
            await Assert.ThrowsAsync<ObjectDisposedException>(() => request.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.AreEqual(0, transport.Sends, "The connection must reject new work before reaching a disposed transport.");
        }
        finally
        {
            cancellation.Cancel();
            if (request != null)
            {
                try { await request; }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            }
            transport.AllowDisposal.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OnceReadLoopEndsNewCallsFailEvenWhenTransportStillReportsConnected(bool receiveFails)
    {
        var transport = new ScriptedTransport { CloseReadImmediately = true, ThrowOnRead = receiveFails };
        await using var connection = new McpConnection(transport);
        using var cancellation = new CancellationTokenSource();
        // Observe completed read-loop cleanup deterministically, without timing sleeps or
        // assuming that the transport's stale IsConnected value reflects EOF.
        var readLoop = (Task)typeof(McpConnection).GetField("_readLoop", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(connection)!;
        await readLoop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(transport.IsConnected);
        var call = connection.CallToolAsync("lookup", cancellationToken: cancellation.Token);
        try
        {
            await Assert.ThrowsAsync<McpException>(() => call.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.AreEqual(0, transport.Sends);
        }
        finally
        {
            cancellation.Cancel();
            try { await call; }
            catch (OperationCanceledException) { }
            catch (McpException) { }
        }
    }

    private sealed class ScriptedTransport : IMcpTransport
    {
        private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();
        private int _receives;
        private bool _disposed;
        public bool MalformedResponseThenValid { get; init; }
        public bool MalformedResponseOnly { get; init; }
        public bool HoldDisposal { get; init; }
        public bool CloseReadImmediately { get; init; }
        public bool ThrowOnRead { get; init; }
        public TaskCompletionSource ReadStarted { get; } = Signal();
        public TaskCompletionSource SecondReadStarted { get; } = Signal();
        public TaskCompletionSource DisposalStarted { get; } = Signal();
        public TaskCompletionSource AllowDisposal { get; } = Signal();
        public int Sends { get; private set; }
        public bool IsConnected => !_disposed;

        public Task SendAsync(string json, CancellationToken cancellationToken = default)
        {
            // Keep sends observable even when closed: connection lifetime cannot depend on a
            // particular custom transport throwing for every late send.
            Sends++;
            if (MalformedResponseThenValid || MalformedResponseOnly)
            {
                using var document = JsonDocument.Parse(json);
                var id = document.RootElement.GetProperty("id").GetInt32();
                _incoming.Writer.TryWrite(JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0", id,
                    result = new { content = new[] { new { type = "text", text = new { invalid = "object" } } } }
                }));
                if (MalformedResponseThenValid)
                    _incoming.Writer.TryWrite(JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0", id,
                        result = new { content = new[] { new { type = "text", text = "recovered" } } }
                    }));
            }
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            var receive = Interlocked.Increment(ref _receives);
            if (receive == 1) ReadStarted.TrySetResult();
            if (receive == 2) SecondReadStarted.TrySetResult();
            if (CloseReadImmediately)
            {
                if (ThrowOnRead) throw new IOException("read transport failed");
                return null;
            }
            try { return await _incoming.Reader.ReadAsync(cancellationToken); }
            catch (ChannelClosedException) { return null; }
        }

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            _incoming.Writer.TryComplete();
            DisposalStarted.TrySetResult();
            if (HoldDisposal) await AllowDisposal.Task;
        }

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
