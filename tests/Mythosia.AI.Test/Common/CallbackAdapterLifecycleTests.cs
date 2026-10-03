using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class CallbackAdapterLifecycleTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestMethod]
    [DataRow("cancel")]
    [DataRow("caller-cancel")]
    [DataRow("dispose")]
    [DataRow("observer-failure")]
    [DataRow("timeout")]
    public async Task RunTermination_DrainsCallbackCleanupBeforeSettlingAndReleasingService(string trigger)
    {
        using var http = new HttpClient();
        using var callerCancellation = new CancellationTokenSource();
        var service = new GatedCallbackProvider(http);
        var observerFailure = new ApplicationException("observer failed");
        var request = service.CreateRequest("input");
        if (trigger == "timeout") request = request.WithTimeout(1);
        var run = await request.StartRunAsync(
            onText: trigger == "observer-failure" ? _ => throw observerFailure : null,
            cancellationToken: callerCancellation.Token);
        Task? disposing = null;
        try
        {
            await service.Started.Task.WaitAsync(Deadline);
            if (trigger == "cancel") run.Cancel();
            if (trigger == "caller-cancel") callerCancellation.Cancel();
            if (trigger == "dispose") disposing = run.DisposeAsync().AsTask();
            await service.CleanupStarted.Task.WaitAsync(Deadline);
            Assert.IsTrue(service.ProviderToken.IsCancellationRequested);
            Assert.IsFalse(service.CleanupFinished.Task.IsCompleted);
            Assert.IsFalse(run.Result.IsCompleted, "Result must wait for provider cleanup.");
            if (disposing != null) Assert.IsFalse(disposing.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => service.StartRunAsync("overlap"));
        }
        finally
        {
            service.ReleaseWork.TrySetResult();
            service.ReleaseCleanup.TrySetResult();
            // Let timeout/observer failures settle under their original cause before disposal
            // supplies an additional caller cancellation signal.
            try { await run.Result.WaitAsync(Deadline); } catch { }
            await run.DisposeAsync().AsTask().WaitAsync(Deadline);
        }

        await service.CleanupFinished.Task.WaitAsync(Deadline);
        if (disposing != null) await disposing.WaitAsync(Deadline);
        if (trigger == "observer-failure")
            Assert.AreSame(observerFailure, await Assert.ThrowsAsync<ApplicationException>(async () => await run.Result));
        else if (trigger == "timeout")
            StringAssert.Contains((await Assert.ThrowsAsync<AIServiceException>(async () => await run.Result)).Message, "timeout");
        else
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await run.Result);

        service.CompleteImmediately = true;
        await using var next = await service.StartRunAsync("next");
        Assert.AreEqual("answer", (await next.Result.WaitAsync(Deadline)).Text);
    }

    [TestMethod]
    public async Task RunCancellation_WaitsForCallbackThatTemporarilyIgnoresCancellation()
    {
        using var http = new HttpClient();
        var service = new GatedCallbackProvider(http) { IgnoreCancellation = true };
        var run = await service.StartRunAsync("input");
        try
        {
            await service.Started.Task.WaitAsync(Deadline);
            run.Cancel();
            await service.CancellationObserved.Task.WaitAsync(Deadline);
            Assert.IsFalse(run.Result.IsCompleted);
            Assert.IsFalse(service.CleanupStarted.Task.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => service.StartRunAsync("overlap"));
            service.ReleaseWork.TrySetResult();
            await service.CleanupStarted.Task.WaitAsync(Deadline);
            Assert.IsFalse(run.Result.IsCompleted);
        }
        finally
        {
            service.ReleaseWork.TrySetResult();
            service.ReleaseCleanup.TrySetResult();
            await run.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await run.Result);
        Assert.IsTrue(service.CleanupFinished.Task.IsCompleted);
    }

    [TestMethod]
    public async Task LegacyIteratorBreak_CancelsAndAwaitsCallbackCleanup()
    {
        using var http = new HttpClient();
        var service = new GatedCallbackProvider(http);
        async Task ReadOne()
        {
            await foreach (var text in service.StreamAsync("input"))
            {
                Assert.AreEqual("answer", text);
                break;
            }
        }
        var reading = ReadOne();
        try
        {
            await service.CleanupStarted.Task.WaitAsync(Deadline);
            Assert.IsTrue(service.ProviderToken.IsCancellationRequested);
            Assert.IsFalse(reading.IsCompleted);
        }
        finally
        {
            service.ReleaseWork.TrySetResult();
            service.ReleaseCleanup.TrySetResult();
        }
        await reading.WaitAsync(Deadline);
        Assert.IsTrue(service.CleanupFinished.Task.IsCompleted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IteratorDisposalFailure_StillWaitsForCallbackCleanup(bool cancellationCallbackThrows)
    {
        using var http = new HttpClient();
        var service = new GatedCallbackProvider(http)
        {
            ThrowCancellationCallback = cancellationCallbackThrows,
            CleanupFailure = cancellationCallbackThrows ? null : new ApplicationException("cleanup failed")
        };
        var iterator = service.StreamAsync("input").GetAsyncEnumerator();
        Assert.IsTrue(await iterator.MoveNextAsync());
        var disposing = iterator.DisposeAsync().AsTask();
        try
        {
            await service.CleanupStarted.Task.WaitAsync(Deadline);
            Assert.IsFalse(disposing.IsCompleted);
        }
        finally
        {
            service.ReleaseWork.TrySetResult();
            service.ReleaseCleanup.TrySetResult();
        }
        if (cancellationCallbackThrows)
        {
            var failure = await Assert.ThrowsAsync<AggregateException>(() => disposing.WaitAsync(Deadline));
            Assert.IsTrue(failure.Flatten().InnerExceptions.Any(ex => ex.Message == "cancellation callback failed"));
        }
        else
            Assert.AreSame(service.CleanupFailure, await Assert.ThrowsAsync<ApplicationException>(() => disposing.WaitAsync(Deadline)));
        Assert.IsTrue(service.CleanupFinished.Task.IsCompleted);
    }

    [TestMethod]
    public async Task ProviderFailure_PreservesErrorEventAndWaitsForCleanup()
    {
        using var http = new HttpClient();
        var service = new GatedCallbackProvider(http) { WorkFailure = new ApplicationException("provider failed") };
        await using var run = await service.StartRunAsync("input");
        try
        {
            await service.Started.Task.WaitAsync(Deadline);
            service.ReleaseWork.TrySetResult();
            await service.CleanupStarted.Task.WaitAsync(Deadline);
            Assert.IsFalse(run.Result.IsCompleted);
        }
        finally { service.ReleaseCleanup.TrySetResult(); }
        var failure = await Assert.ThrowsAsync<AIServiceException>(() => run.Result.WaitAsync(Deadline));
        StringAssert.Contains(failure.Message, "provider failed");
        Assert.IsTrue(service.CleanupFinished.Task.IsCompleted);
    }

    [TestMethod]
    public async Task ObserverAndCleanupFailures_AreBothRetainedAfterCleanupCompletes()
    {
        using var http = new HttpClient();
        var observerFailure = new ApplicationException("observer failed");
        var cleanupFailure = new InvalidOperationException("cleanup failed");
        var service = new GatedCallbackProvider(http) { CleanupFailure = cleanupFailure };
        var run = await service.StartRunAsync("input", _ => throw observerFailure);
        try
        {
            await service.CleanupStarted.Task.WaitAsync(Deadline);
            Assert.IsTrue(service.ProviderToken.IsCancellationRequested);
            Assert.IsFalse(run.Result.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => service.StartRunAsync("overlap"));
            service.ReleaseCleanup.TrySetResult();
            var failure = await Assert.ThrowsAsync<AggregateException>(() => run.Result.WaitAsync(Deadline));
            var errors = failure.Flatten().InnerExceptions;
            Assert.HasCount(2, errors);
            Assert.IsTrue(errors.Any(error => ReferenceEquals(error, observerFailure)));
            Assert.IsTrue(errors.Any(error => ReferenceEquals(error, cleanupFailure)));
            Assert.IsTrue(service.CleanupFinished.Task.IsCompleted);
        }
        finally
        {
            service.ReleaseWork.TrySetResult();
            service.ReleaseCleanup.TrySetResult();
            await run.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
    }

    [TestMethod]
    [DataRow(0, 1, false)]
    [DataRow(1, 0, true)]
    [DataRow(1, 1, true)]
    [DataRow(0, 0, false)]
    public async Task BuilderRun_RecoveryBudgetRemainsCaptured(int capturedBudget, int laterDefault, bool recovers)
    {
        using var http = new HttpClient();
        var service = new OverflowProvider(http)
        {
            ContextRecoveryMaxRetries = capturedBudget,
            ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 1000, KeepRecentCount = 1 }
        };
        for (var i = 0; i < 6; i++)
            service.ActivateChat.Messages.Add(new Message(i % 2 == 0 ? ActorRole.User : ActorRole.Assistant, "history-" + i));
        var request = service.CreateRequest("input");
        service.ContextRecoveryMaxRetries = laterDefault;
        await using var run = await request.StartRunAsync();
        if (recovers)
        {
            Assert.AreEqual("recovered", (await run.Result.WaitAsync(Deadline)).Text);
            Assert.AreEqual(2, service.Rounds);
            Assert.AreEqual(1, service.Summaries);
            Assert.HasCount(1, service.ActivateChat.Messages);
        }
        else
        {
            await Assert.ThrowsAsync<AIServiceException>(() => run.Result.WaitAsync(Deadline));
            Assert.AreEqual(1, service.Rounds);
            Assert.AreEqual(0, service.Summaries);
            Assert.HasCount(7, service.ActivateChat.Messages);
        }
    }

    private abstract class FakeProvider(HttpClient http) : AIService("offline", "https://offline.invalid/", http)
    {
        public override string Provider => "SyntheticCallback";
        protected override HttpRequestMessage CreateMessageRequest() => throw new AssertFailedException("No HTTP expected.");
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new AssertFailedException("No HTTP expected.");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => throw new AssertFailedException("No HTTP expected.");
        protected override string ExtractResponseContent(string response) => throw new AssertFailedException("No HTTP expected.");
        protected override string StreamParseJson(string json) => throw new AssertFailedException("No HTTP expected.");
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
        public override Task<string> GetCompletionAsync(Message message) => Task.FromResult("answer");
    }

    private sealed class GatedCallbackProvider(HttpClient http) : FakeProvider(http)
    {
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource CancellationObserved { get; } = Signal();
        public TaskCompletionSource CleanupStarted { get; } = Signal();
        public TaskCompletionSource CleanupFinished { get; } = Signal();
        public TaskCompletionSource ReleaseWork { get; } = Signal();
        public TaskCompletionSource ReleaseCleanup { get; } = Signal();
        public CancellationToken ProviderToken { get; private set; }
        public bool CompleteImmediately { get; set; }
        public bool IgnoreCancellation { get; init; }
        public bool ThrowCancellationCallback { get; init; }
        public Exception? WorkFailure { get; init; }
        public Exception? CleanupFailure { get; init; }

        public override async Task StreamCompletionAsync(Message message, Func<string, Task> received)
        {
            using var settings = BeginRequestSettingsScope();
            using var features = BeginRequestFeaturesScope(message);
            if (CompleteImmediately) { await received("answer"); return; }
            ProviderToken = RequestCancellationToken;
            using var registration = ProviderToken.Register(() =>
            {
                CancellationObserved.TrySetResult();
                if (ThrowCancellationCallback) throw new ApplicationException("cancellation callback failed");
            });
            Started.TrySetResult();
            try
            {
                await received("answer");
                if (IgnoreCancellation) await ReleaseWork.Task;
                else await ReleaseWork.Task.WaitAsync(ProviderToken);
                if (WorkFailure != null) throw WorkFailure;
            }
            finally
            {
                CleanupStarted.TrySetResult();
                await ReleaseCleanup.Task;
                CleanupFinished.TrySetResult();
                if (CleanupFailure != null) throw CleanupFailure;
            }
        }
    }

    private sealed class OverflowProvider(HttpClient http) : FakeProvider(http)
    {
        public int Rounds { get; private set; }
        public int Summaries { get; private set; }
        public override Task<string> GetCompletionAsync(Message message)
        {
            using var settings = BeginRequestSettingsScope();
            using var features = BeginRequestFeaturesScope(message);
            Summaries++;
            return Task.FromResult("compressed summary");
        }
        public override Task StreamCompletionAsync(Message message, Func<string, Task> received)
            => throw new AssertFailedException("Callback adapter is not used in recovery-budget tests.");
        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(StreamOptions options, bool useFunctions,
            FunctionCallingPolicy policy, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            Rounds++;
            if (Rounds == 1)
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Error, Content = "synthetic overflow",
                    Metadata = new Dictionary<string, object> { [AIHttpErrorFactory.ContextLengthExceededKey] = true }
                };
            else yield return new StreamingContent { Type = StreamingContentType.Text, Content = "recovered" };
        }
    }
}
