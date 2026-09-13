using Mythosia.AI.Mcp.Transports;

namespace Mythosia.AI.Mcp.Tests;

[TestClass]
public class AdversarialMcpCancellationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ThrowingReadCancellationCallbackDoesNotSkipTransportCleanup(bool disposalThrows)
    {
        var transport = new ThrowingCallbackTransport { DisposalThrows = disposalThrows };
        var connection = new McpConnection(transport);
        try
        {
            await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var exception = await Assert.ThrowsAsync<AggregateException>(() => connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(1, transport.Disposals, "The connection owns transport cleanup even when cancellation callbacks throw.");
            Assert.IsTrue(exception.Flatten().InnerExceptions.Any(error => error.Message == "transport cancellation callback failed"));
            Assert.AreEqual(disposalThrows, exception.Flatten().InnerExceptions.Any(error => error.Message == "transport disposal failed"));
            await connection.DisposeAsync();
            Assert.AreEqual(1, transport.Disposals);
        }
        finally { transport.Release.TrySetResult(null); }
    }

    private sealed class ThrowingCallbackTransport : IMcpTransport
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string?> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsConnected => Disposals == 0;
        public int Disposals { get; private set; }
        public bool DisposalThrows { get; set; }
        public Task SendAsync(string json, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() =>
            {
                Release.TrySetCanceled(cancellationToken);
                throw new InvalidOperationException("transport cancellation callback failed");
            });
            Started.TrySetResult();
            return await Release.Task;
        }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            Release.TrySetResult(null);
            if (DisposalThrows) throw new InvalidOperationException("transport disposal failed");
            return default;
        }
    }
}
