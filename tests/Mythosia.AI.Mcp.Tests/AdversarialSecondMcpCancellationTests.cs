using Mythosia.AI.Mcp.Transports;

namespace Mythosia.AI.Mcp.Tests;

[TestClass]
public class AdversarialSecondMcpCancellationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConcurrentOrReentrantDisposalWaitsForTransportCleanup(bool reenterFromCancellation)
    {
        var transport = new GatedDisposalTransport();
        var connection = new McpConnection(transport);
        Task? reentrantDisposal = null;
        if (reenterFromCancellation)
            transport.OnCancellation = () => reentrantDisposal = connection.DisposeAsync().AsTask();

        await transport.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var firstDisposal = connection.DisposeAsync().AsTask();
        Task? secondDisposal = null;
        try
        {
            await transport.DisposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            secondDisposal = reenterFromCancellation
                ? reentrantDisposal
                : connection.DisposeAsync().AsTask();
            Assert.IsNotNull(secondDisposal);
            Assert.IsFalse(secondDisposal.IsCompleted,
                "Every pending DisposeAsync call must wait for the same transport cleanup.");
            Assert.AreEqual(1, transport.Disposals);
            transport.AllowDisposal.TrySetResult();
            await Task.WhenAll(firstDisposal, secondDisposal).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, transport.Disposals);
        }
        finally
        {
            transport.AllowDisposal.TrySetResult();
            await firstDisposal.WaitAsync(TimeSpan.FromSeconds(5));
            if (secondDisposal != null) await secondDisposal.WaitAsync(TimeSpan.FromSeconds(5));
            await connection.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task DisposalClosesTransportBeforeWaitingForAReadThatNeedsClosure()
    {
        var transport = new ClosureDrivenReadTransport();
        var connection = new McpConnection(transport);
        await transport.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposal = connection.DisposeAsync().AsTask();
        try
        {
            await disposal.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(1, transport.Disposals);
            Assert.IsTrue(transport.ReadExited.Task.IsCompleted);
        }
        finally
        {
            // Ensure the intentionally blocked baseline implementation also terminates.
            transport.CloseRead.TrySetResult(null);
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            await connection.DisposeAsync();
        }
    }

    private sealed class GatedDisposalTransport : IMcpTransport
    {
        public TaskCompletionSource ReadStarted { get; } = NewSignal();
        public TaskCompletionSource DisposalStarted { get; } = NewSignal();
        public TaskCompletionSource AllowDisposal { get; } = NewSignal();
        public Action? OnCancellation { get; set; }
        public int Disposals { get; private set; }
        public bool IsConnected => Disposals == 0;
        public Task SendAsync(string json, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => OnCancellation?.Invoke());
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }

        public async ValueTask DisposeAsync()
        {
            Disposals++;
            DisposalStarted.TrySetResult();
            await AllowDisposal.Task;
        }
    }

    private sealed class ClosureDrivenReadTransport : IMcpTransport
    {
        public TaskCompletionSource ReadStarted { get; } = NewSignal();
        public TaskCompletionSource<string?> CloseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadExited { get; } = NewSignal();
        public int Disposals { get; private set; }
        public bool IsConnected => Disposals == 0;
        public Task SendAsync(string json, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            // Some underlying readers can only unblock when their transport is closed.
            ReadStarted.TrySetResult();
            try { return await CloseRead.Task; }
            finally { ReadExited.TrySetResult(); }
        }

        public async ValueTask DisposeAsync()
        {
            Disposals++;
            CloseRead.TrySetResult(null);
            await ReadExited.Task;
        }
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
