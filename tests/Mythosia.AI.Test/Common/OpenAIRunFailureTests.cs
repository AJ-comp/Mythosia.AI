using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("OpenAI")]
[TestCategory("Run")]
public class OpenAIRunFailureTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(5);

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Standard, false)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Standard, true)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Pro, false)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Pro, true)]
    [DataRow(AIModels.OpenAI.Gpt6Astra, Gpt6ReasoningMode.Standard, false)]
    [DataRow(AIModels.OpenAI.Gpt6Astra, Gpt6ReasoningMode.Standard, true)]
    public async Task TransportFailureDuringRequiredTool_CancelsToolAndPreservesFailure(
        string model, Gpt6ReasoningMode mode, bool throwReceiveError)
    {
        using var fixture = new ToolFixture(model, mode);
        var run = await fixture.StartAsync();
        try
        {
            var transportError = fixture.Socket.FailTransport(throwReceiveError);
            var failure = await ObserveFailureAsync(run);

            AssertTransportFailure(failure, transportError);
            Assert.IsFalse(run.Result.IsCanceled, "A transport failure must not become caller cancellation.");
            fixture.AssertCancelledAndPaired();
            Assert.AreEqual(1, fixture.Socket.Sent.Count, "A failed connection must not replay the request.");
        }
        finally { await StopAsync(run); }
        await AssertServiceReusableAsync(fixture.Service);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, false)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, true)]
    [DataRow(AIModels.OpenAI.Gpt6Astra, false)]
    [DataRow(AIModels.OpenAI.Gpt6Astra, true)]
    public async Task TransportFailureWhileSteeringRequiresAsyncResult_InterruptsRequiredInputWait(
        string model, bool throwReceiveError)
    {
        using var fixture = new ToolFixture(model, Gpt6ReasoningMode.Standard,
            asynchronous: true, pendingSteering: true);
        var run = await fixture.StartAsync();
        try
        {
            // StartAsync gates on the pending frame being published and the third reader
            // entering its incomplete MoveNextAsync with the required tool still blocked.
            // This exercises the direct required-input wait, not the ordinary async wait.
            var transportError = fixture.Socket.FailTransport(throwReceiveError);
            var failure = await ObserveFailureAsync(run);

            AssertTransportFailure(failure, transportError);
            Assert.IsFalse(run.Result.IsCanceled);
            fixture.AssertCancelledAndPaired();
            Assert.AreEqual(3, fixture.Socket.Sent.Count, "Only two creates and the accepted steer may be sent.");
        }
        finally { await StopAsync(run); }
        await AssertServiceReusableAsync(fixture.Service);
    }

    [TestMethod]
    [DataRow(Gpt6ReasoningMode.Standard, false)]
    [DataRow(Gpt6ReasoningMode.Standard, true)]
    [DataRow(Gpt6ReasoningMode.Pro, false)]
    [DataRow(Gpt6ReasoningMode.Pro, true)]
    public async Task CallerCancellationDuringTool_RemainsCancellation(
        Gpt6ReasoningMode mode, bool asynchronous)
    {
        using var fixture = new ToolFixture(AIModels.OpenAI.Gpt6_1Sol, mode, asynchronous);
        using var cancellation = new CancellationTokenSource();
        var run = await fixture.StartAsync(cancellation.Token);
        try
        {
            cancellation.Cancel();
            var failure = await ObserveFailureAsync(run);

            Assert.IsInstanceOfType<OperationCanceledException>(failure);
            Assert.IsTrue(run.Result.IsCanceled, "Aborting a cancelled socket must not replace caller cancellation.");
            fixture.AssertCancelledAndPaired();
        }
        finally { await StopAsync(run); }
        await AssertServiceReusableAsync(fixture.Service);
    }

    [TestMethod]
    [DataRow(Gpt6ReasoningMode.Standard, false)]
    [DataRow(Gpt6ReasoningMode.Standard, true)]
    [DataRow(Gpt6ReasoningMode.Pro, false)]
    [DataRow(Gpt6ReasoningMode.Pro, true)]
    public async Task NaturalPolicyTimeoutDuringTool_RemainsTimeout(
        Gpt6ReasoningMode mode, bool asynchronous)
    {
        using var fixture = new ToolFixture(AIModels.OpenAI.Gpt6_1Sol, mode, asynchronous,
            timeoutSeconds: 1);
        var run = await fixture.StartAsync();
        try
        {
            var failure = await ObserveFailureAsync(run);

            Assert.IsInstanceOfType<AIServiceException>(failure);
            StringAssert.Contains(failure.Message, "Request timeout after 1 seconds");
            Assert.IsFalse(run.Result.IsCanceled);
            fixture.AssertCancelledAndPaired();
        }
        finally { await StopAsync(run); }
        await AssertServiceReusableAsync(fixture.Service);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ThrowingToolCancellationCallback_DoesNotHideTransportFailureOrSkipCleanup(bool asynchronous)
    {
        using var fixture = new ToolFixture(AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Standard,
            asynchronous: asynchronous, throwingCancellationCallback: true);
        var run = await fixture.StartAsync();
        try
        {
            var original = fixture.Socket.FailTransport(throwReceiveError: true);
            var failure = await ObserveFailureAsync(run);

            Assert.IsTrue(EnumerateFailures(failure).Any(item => ReferenceEquals(item, original)),
                "Cleanup callback failures must not erase the original transport exception.");
            Assert.IsTrue(fixture.ThrowingCallbackInvoked.Task.IsCompleted);
            fixture.AssertCancelledAndPaired();
            Assert.IsTrue(fixture.Socket.IsDisposed);
        }
        finally { await StopAsync(run); }
        await AssertServiceReusableAsync(fixture.Service);
    }

    [TestMethod]
    public async Task AsyncScopeCleanupThrowsBeforeReceiverCancels_PreservesTransportAndCallbackFailures()
    {
        using var fixture = new ToolFixture(AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Standard,
            asynchronous: true, throwingCancellationCallback: true);
        var run = await fixture.StartAsync();
        var abortEntered = NewSignal();
        var cleanupCallbackObserved = NewSignal();
        fixture.Socket.BeforeFirstAbort = () =>
        {
            // Fail has awakened the async-result coordinator but has not reached its
            // lifetime cancellation yet. Let source cleanup own the throwing callback,
            // so its AggregateException can arrive before any internal cancellation.
            abortEntered.TrySetResult();
            fixture.ThrowingCallbackInvoked.Task.WaitAsync(TestDeadline).GetAwaiter().GetResult();
            cleanupCallbackObserved.TrySetResult();
        };
        try
        {
            var original = fixture.Socket.FailTransport(throwReceiveError: true);
            await abortEntered.Task.WaitAsync(TestDeadline);
            var failure = await ObserveFailureAsync(run);
            var failures = EnumerateFailures(failure).ToArray();

            Assert.IsTrue(cleanupCallbackObserved.Task.IsCompleted,
                "The cleanup callback must run before the receiver resumes lifetime cancellation.");
            Assert.IsTrue(failures.Any(item => ReferenceEquals(item, original)),
                "A non-cancellation cleanup exception must not replace the transport failure.");
            Assert.IsTrue(failures.Any(item => item.Message == "tool cancellation callback failed"),
                "The secondary cleanup callback failure must also remain observable.");
            Assert.IsFalse(run.Result.IsCanceled);
            fixture.AssertCancelledAndPaired();
            Assert.IsTrue(fixture.Socket.IsDisposed);
        }
        finally { await StopAsync(run); }
        await AssertServiceReusableAsync(fixture.Service);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Standard)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Pro)]
    [DataRow(AIModels.OpenAI.Gpt6Astra, Gpt6ReasoningMode.Standard)]
    public async Task CompletedResponseThenNormalClose_PreservesSuccessfulResult(
        string model, Gpt6ReasoningMode mode)
    {
        using var socket = new ScriptedSocket();
        var service = new SocketService(socket, model, mode);
        // Hold the send until the receiver has consumed the close frame. Thus cleanup
        // races with a terminal event that is already queued, not an arbitrary delay.
        socket.OnSend = _ =>
        {
            socket.Push(Created("complete", model), Text("finished"), Completed("complete"));
            socket.CloseFromServer();
        };
        socket.BeforeSendReturns = _ => socket.CloseConsumed.Task;

        await using var run = await service.StartRunAsync("finish normally");
        Assert.AreEqual("finished", (await run.Result.WaitAsync(TestDeadline)).Text);
        Assert.IsFalse(run.Result.IsCanceled);
        Assert.IsTrue(socket.IsDisposed);
        Assert.AreEqual(1, socket.Sent.Count);
        await AssertServiceReusableAsync(service);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra, Gpt6ReasoningMode.Standard)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Standard)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Pro)]
    public async Task FinalResponseAndCloseBeforeToolOutputSendReturns_PreservesSuccessfulResult(
        string model, Gpt6ReasoningMode mode)
    {
        using var socket = new ScriptedSocket();
        var service = new SocketService(socket, model, mode);
        var calls = 0;
        const string callId = "call_slow";
        service.Functions.Add(new FunctionDefinition
        {
            Name = "slow", Handler = _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult("tool result");
            }
        });
        socket.OnSend = payload =>
        {
            if (socket.Sent.Count == 1)
            {
                socket.Push(Created("tool", model), ToolCompleted("tool", callId, asynchronous: false));
                return;
            }
            Assert.AreEqual(2, socket.Sent.Count, "The completed tool needs exactly one continuation.");
            Assert.AreEqual("tool", payload.GetProperty("previous_response_id").GetString());
            var output = Assert.ContainsSingle(payload.GetProperty("input").EnumerateArray().ToArray());
            Assert.AreEqual("function_call_output", output.GetProperty("type").GetString());
            Assert.AreEqual(callId, output.GetProperty("call_id").GetString());
            Assert.AreEqual("tool result", output.GetProperty("output").GetString());
            socket.Push(Created("final", model), Text("finished with tool"), Completed("final"));
            socket.CloseFromServer();
        };
        // The server has accepted the output and finished its response while the local
        // send task is still pending. A subsequent normal close cannot discard that result.
        socket.BeforeSendReturns = _ => socket.Sent.Count == 2
            ? socket.CloseConsumed.Task : Task.CompletedTask;

        await using var run = await service.StartRunAsync("use the tool and finish");
        Assert.AreEqual("finished with tool", (await run.Result.WaitAsync(TestDeadline)).Text);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, socket.Sent.Count);
        Assert.IsTrue(socket.IsDisposed);
        var results = service.ActivateChat.Messages.Where(message => message.FunctionCallResultBatch != null)
            .SelectMany(message => message.FunctionCallResultBatch!.Results)
            .Where(result => result.Call.Id == callId).ToArray();
        Assert.AreEqual(1, results.Length);
        Assert.IsFalse(results[0].IsCancelled);
        Assert.IsFalse(results[0].IsError);
        await AssertServiceReusableAsync(service);
    }

    // Terminal response, pending-send, and provider-error regression coverage.

    private const string ProviderFailureReason = "sentinel provider rate limit";

    private static IEnumerable<(string Model, Gpt6ReasoningMode Mode)> TerminalModels => new[]
    {
        (AIModels.OpenAI.Gpt6Astra, Gpt6ReasoningMode.Standard),
        (AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Standard),
        (AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Pro)
    };

    public static IEnumerable<object[]> CompletedSendAbortCases => TerminalModels.SelectMany(model =>
        new[] { false, true }.SelectMany(toolOutput => new[] { false, true }.Select(cancelled =>
            new object[] { model.Model, model.Mode, toolOutput, cancelled })));

    public static IEnumerable<object[]> ProviderFailureCloseCases => TerminalModels.SelectMany(model =>
        new[] { "response.failed", "error" }.SelectMany(type => new[] { false, true }.Select(throwingCallback =>
            new object[] { model.Model, model.Mode, type, throwingCallback })));

    [TestMethod]
    [DynamicData(nameof(CompletedSendAbortCases))]
    public async Task CompletedFinalThenCloseAbortsPendingSend_PreservesSuccessfulResult(
        string model, Gpt6ReasoningMode mode, bool toolOutput, bool cancellationFailure)
    {
        using var socket = new ScriptedSocket();
        var service = new SocketService(socket, model, mode);
        var calls = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "slow", Handler = _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult("tool result");
            }
        });
        socket.OnSend = payload =>
        {
            if (toolOutput && socket.Sent.Count == 1)
            {
                socket.Push(Created("tool", model), ToolCompleted("tool", "call_slow", asynchronous: false));
                return;
            }
            if (toolOutput)
            {
                Assert.AreEqual("tool", payload.GetProperty("previous_response_id").GetString());
                var output = Assert.ContainsSingle(payload.GetProperty("input").EnumerateArray().ToArray());
                Assert.AreEqual("call_slow", output.GetProperty("call_id").GetString());
                Assert.AreEqual("tool result", output.GetProperty("output").GetString());
            }
            socket.Push(Created("final", model), Text("complete answer"), Completed("final"));
            socket.CloseFromServer();
        };
        socket.SendOperation = (_, token) => toolOutput && socket.Sent.Count == 1
            ? Task.CompletedTask : socket.WaitForSendAbortAsync(cancellationFailure, token);

        await using var run = await service.StartRunAsync("finish the request");
        Assert.AreEqual("complete answer", (await run.Result.WaitAsync(TestDeadline)).Text);
        Assert.IsFalse(run.Result.IsCanceled);
        Assert.AreEqual(toolOutput ? 2 : 1, socket.Sent.Count, "No successful request/output may be replayed.");
        Assert.AreEqual(toolOutput ? 1 : 0, calls);
        Assert.IsTrue(socket.CloseConsumed.Task.IsCompleted);
        Assert.IsTrue(socket.Aborted.Task.IsCompleted, "The pending send must be interrupted by a real socket Abort.");
        Assert.IsTrue(socket.IsDisposed);
        if (toolOutput)
        {
            var result = Assert.ContainsSingle(service.ActivateChat.Messages
                .Where(message => message.FunctionCallResultBatch != null)
                .SelectMany(message => message.FunctionCallResultBatch!.Results).ToArray());
            Assert.IsFalse(result.IsError);
            Assert.IsFalse(result.IsCancelled);
        }
        await AssertServiceReusableAsync(service);
    }

    [TestMethod]
    [DynamicData(nameof(ProviderFailureCloseCases))]
    public async Task ProviderFailureThenCloseWithDelayedConsumer_PreservesReasonAndCleanupFailures(
        string model, Gpt6ReasoningMode mode, string terminalType, bool throwingCancellationCallback)
    {
        using var fixture = new ToolFixture(model, mode, asynchronous: true,
            throwingCancellationCallback: throwingCancellationCallback);
        var callbackEntered = NewSignal();
        var releaseCallback = NewSignal();
        fixture.Socket.OnSend = _ =>
        {
            if (fixture.Socket.Sent.Count == 1)
                fixture.Socket.Push(Created("tool", model), ToolCompleted("tool", "call_slow", asynchronous: true));
            else
                fixture.Socket.Push(Created("waiting", model), Text("hold the common reader"));
        };
        var run = await fixture.Service.StartRunAsync("keep the async tool pending", _ =>
        {
            callbackEntered.TrySetResult();
            releaseCallback.Task.WaitAsync(TestDeadline).GetAwaiter().GetResult();
        });
        try
        {
            await fixture.Started.WaitAsync(TestDeadline);
            await callbackEntered.Task.WaitAsync(TestDeadline);
            // The receiver can observe the terminal error and close while the common
            // reader is deterministically held in a prior text callback.
            fixture.Socket.Push(ProviderFailure("waiting", terminalType));
            fixture.Socket.CloseFromServer();
            await fixture.Socket.Aborted.Task.WaitAsync(TestDeadline);
            releaseCallback.TrySetResult();
            var failure = await ObserveFailureAsync(run);
            AssertProviderFailure(failure, terminalType);
            Assert.IsFalse(run.Result.IsCanceled);
            if (throwingCancellationCallback)
            {
                Assert.IsTrue(fixture.ThrowingCallbackInvoked.Task.IsCompleted);
                Assert.IsTrue(EnumerateFailures(failure).Any(item => item.Message == "tool cancellation callback failed"));
            }
            fixture.AssertCancelledAndPaired();
            Assert.AreEqual(2, fixture.Socket.Sent.Count);
        }
        finally
        {
            releaseCallback.TrySetResult();
            await StopAsync(run);
        }
        await AssertServiceReusableAsync(fixture.Service);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra, false)]
    [DataRow(AIModels.OpenAI.Gpt6Astra, true)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, false)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, true)]
    public async Task ProviderErrorWhileSteeringRequiresTool_PreservesReasonAndCleanupFailures(
        string model, bool throwingCancellationCallback)
    {
        using var fixture = new ToolFixture(model, Gpt6ReasoningMode.Standard, asynchronous: true,
            pendingSteering: true, throwingCancellationCallback: throwingCancellationCallback);
        var run = await fixture.StartAsync();
        try
        {
            fixture.Socket.Push(ProviderFailure("waiting", "error"));
            fixture.Socket.CloseFromServer();
            var failure = await ObserveFailureAsync(run);
            AssertProviderFailure(failure, "error");
            Assert.IsFalse(run.Result.IsCanceled);
            if (throwingCancellationCallback)
                Assert.IsTrue(EnumerateFailures(failure).Any(item => item.Message == "tool cancellation callback failed"));
            fixture.AssertCancelledAndPaired();
            Assert.AreEqual(3, fixture.Socket.Sent.Count, "Only the original two creates and accepted steer may be sent.");
        }
        finally { await StopAsync(run); }
        await AssertServiceReusableAsync(fixture.Service);
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, false)]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    public async Task IncompleteOrMissingSuccessorThenSendAbort_DoesNotBecomeSuccess(
        bool toolOutput, bool incompleteResponse, bool cancellationFailure)
    {
        using var socket = new ScriptedSocket();
        var service = new SocketService(socket, AIModels.OpenAI.Gpt6_1Sol, Gpt6ReasoningMode.Standard);
        service.Functions.Add(new FunctionDefinition { Name = "slow", Handler = _ => Task.FromResult("tool result") });
        socket.OnSend = _ =>
        {
            if (toolOutput && socket.Sent.Count == 1)
            {
                socket.Push(Created("tool", service.Model), ToolCompleted("tool", "call_slow", asynchronous: false));
                return;
            }
            if (incompleteResponse)
                socket.Push(Created("incomplete", service.Model), Text("partial"), Incomplete("incomplete"));
            // With a tool output, the earlier completed tool response is not proof
            // that this send succeeded: no successor was received in this branch.
            socket.CloseFromServer();
        };
        socket.SendOperation = (_, token) => toolOutput && socket.Sent.Count == 1
            ? Task.CompletedTask : socket.WaitForSendAbortAsync(cancellationFailure, token);
        var run = await service.StartRunAsync("do not complete");
        try
        {
            await ObserveFailureAsync(run);
            Assert.IsTrue(socket.IsDisposed);
            Assert.AreEqual(toolOutput ? 2 : 1, socket.Sent.Count);
        }
        finally { await StopAsync(run); }
        await AssertServiceReusableAsync(service);
    }

    [TestMethod]
    [DataRow(Gpt6ReasoningMode.Standard, false, false)]
    [DataRow(Gpt6ReasoningMode.Standard, false, true)]
    [DataRow(Gpt6ReasoningMode.Standard, true, false)]
    [DataRow(Gpt6ReasoningMode.Standard, true, true)]
    [DataRow(Gpt6ReasoningMode.Pro, false, false)]
    [DataRow(Gpt6ReasoningMode.Pro, false, true)]
    [DataRow(Gpt6ReasoningMode.Pro, true, false)]
    [DataRow(Gpt6ReasoningMode.Pro, true, true)]
    public async Task CallerCancellationOrTimeoutDuringSend_PreservesClassification(
        Gpt6ReasoningMode mode, bool completedResponse, bool policyTimeout)
    {
        using var socket = new ScriptedSocket();
        var service = new SocketService(socket, AIModels.OpenAI.Gpt6_1Sol, mode);
        if (policyTimeout)
            service.DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 8, TimeoutSeconds = 1 };
        var sendStarted = NewSignal();
        socket.OnSend = _ =>
        {
            if (completedResponse)
                socket.Push(Created("final", service.Model), Text("received before cancellation"), Completed("final"));
            sendStarted.TrySetResult();
        };
        socket.SendOperation = (_, token) => socket.WaitForSendAbortAsync(cancellationFailure: true, token);
        var run = await service.StartRunAsync("interrupt pending send");
        try
        {
            await sendStarted.Task.WaitAsync(TestDeadline);
            if (completedResponse) await socket.CompletedPublished.Task.WaitAsync(TestDeadline);
            if (!policyTimeout) run.Cancel();
            var failure = await ObserveFailureAsync(run);
            if (policyTimeout)
            {
                Assert.IsInstanceOfType<AIServiceException>(failure);
                StringAssert.Contains(failure.Message, "Request timeout after 1 seconds");
                Assert.IsFalse(run.Result.IsCanceled);
            }
            else
            {
                Assert.IsInstanceOfType<OperationCanceledException>(failure);
                Assert.IsTrue(run.Result.IsCanceled);
            }
            Assert.IsTrue(socket.IsDisposed);
            Assert.AreEqual(1, socket.Sent.Count);
        }
        finally { await StopAsync(run); }
        await AssertServiceReusableAsync(service);
    }

    private static void AssertProviderFailure(Exception failure, string terminalType)
    {
        var providerFailure = EnumerateFailures(failure).OfType<AIServiceException>()
            .FirstOrDefault(item => item.ErrorDetails?.Contains(ProviderFailureReason, StringComparison.Ordinal) == true);
        Assert.IsNotNull(providerFailure, "The received provider reason must survive transport close and cleanup errors.");
        using var details = JsonDocument.Parse(providerFailure.ErrorDetails!);
        Assert.AreEqual(ProviderFailureReason, details.RootElement.GetProperty("reason").GetString());
        Assert.AreEqual(terminalType == "error" ? "error" : "failed", details.RootElement.GetProperty("status").GetString());
    }

    private static string ProviderFailure(string id, string type) => type == "error"
        ? JsonSerializer.Serialize(new { type, error = new { code = "rate_limit_exceeded", message = ProviderFailureReason } })
        : JsonSerializer.Serialize(new
        {
            type, response = new { id, status = "failed", error = new { code = "rate_limit_exceeded", message = ProviderFailureReason }, output = Array.Empty<object>() }
        });

    private static string Incomplete(string id) => JsonSerializer.Serialize(new
    {
        type = "response.incomplete", response = new { id, status = "incomplete", incomplete_details = new { reason = "max_output_tokens" }, output = Array.Empty<object>() }
    });

    private static async Task<Exception> ObserveFailureAsync(AIRun run)
    {
        Exception? failure = null;
        try { await run.Result.WaitAsync(TestDeadline); }
        catch (Exception exception) { failure = exception; }
        Assert.IsNotNull(failure, "The failed/cancelled run must not succeed.");
        Assert.IsFalse(failure is TimeoutException,
            "Run.Result did not settle after its execution was interrupted; the fixture policy is 30 seconds.");
        return failure;
    }

    private static void AssertTransportFailure(Exception failure, WebSocketException? original)
    {
        if (original != null)
            Assert.AreSame(original, failure, "Cancellation used for cleanup must preserve the original receive exception.");
        else
        {
            Assert.IsInstanceOfType<AIServiceException>(failure);
            StringAssert.Contains(failure.Message, "OpenAI WebSocket disconnected");
        }
        Assert.IsFalse(failure.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Exception> EnumerateFailures(Exception exception)
    {
        yield return exception;
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var nested in EnumerateFailures(inner)) yield return nested;
        }
        else if (exception.InnerException != null)
            foreach (var nested in EnumerateFailures(exception.InnerException)) yield return nested;
    }

    private static async Task StopAsync(AIRun run)
    {
        run.Cancel();
        await run.DisposeAsync().AsTask().WaitAsync(TestDeadline);
    }

    private static async Task AssertServiceReusableAsync(SocketService service)
    {
        using var socket = new ScriptedSocket();
        socket.OnSend = _ => socket.Push(Created("retry", service.Model), Text("retry answer"), Completed("retry"));
        service.NextSocket = socket;
        service.DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 8, TimeoutSeconds = 30 };
        await using var retry = await service.StartRunAsync("retry after cleanup");
        Assert.AreEqual("retry answer", (await retry.Result.WaitAsync(TestDeadline)).Text);
        Assert.AreEqual(1, socket.Sent.Count);
        Assert.AreEqual(2, service.Connections);
    }

    private sealed class ToolFixture : IDisposable
    {
        private const string CallId = "call_slow";
        private readonly bool _asynchronous;
        private readonly bool _pendingSteering;
        private readonly TaskCompletionSource _started = NewSignal();
        private readonly TaskCompletionSource _cancelled = NewSignal();
        private readonly TaskCompletionSource _waitingText = NewSignal();
        private int _calls;
        private int _creates;
        public ScriptedSocket Socket { get; } = new();
        public SocketService Service { get; }
        public TaskCompletionSource ThrowingCallbackInvoked { get; } = NewSignal();
        public Task Started => _started.Task;

        public ToolFixture(string model, Gpt6ReasoningMode mode, bool asynchronous = false,
            bool pendingSteering = false, int timeoutSeconds = 30, bool throwingCancellationCallback = false)
        {
            _asynchronous = asynchronous;
            _pendingSteering = pendingSteering;
            Service = new SocketService(Socket, model, mode) { ObserveRequiredInputWait = pendingSteering };
            Service.DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 8, TimeoutSeconds = timeoutSeconds };
            Service.Functions.Add(new FunctionDefinition
            {
                Name = "slow", AllowAsync = asynchronous,
                HandlerWithCancellation = async (_, token) =>
                {
                    Interlocked.Increment(ref _calls);
                    using var registration = throwingCancellationCallback
                        ? token.Register(() =>
                        {
                            ThrowingCallbackInvoked.TrySetResult();
                            throw new InvalidOperationException("tool cancellation callback failed");
                        })
                        : default;
                    _started.TrySetResult();
                    try { await Task.Delay(Timeout.Infinite, token); return "unreachable"; }
                    finally { if (token.IsCancellationRequested) _cancelled.TrySetResult(); }
                }
            });
            Socket.OnSend = payload =>
            {
                if (payload.GetProperty("type").GetString() == "response.steer")
                {
                    Socket.Push(Accepted("waiting"), Completed("waiting"), Pending("waiting", CallId));
                    return;
                }
                if (++_creates == 1)
                    Socket.Push(Created("tool", model), ToolCompleted("tool", CallId, asynchronous));
                else
                {
                    Socket.Push(Created("waiting", model), Text("waiting"));
                    if (!pendingSteering) Socket.Push(Completed("waiting"));
                }
            };
        }

        public async Task<AIRun> StartAsync(CancellationToken cancellationToken = default)
        {
            var run = await Service.StartRunAsync("use the tool", _ => _waitingText.TrySetResult(),
                cancellationToken: cancellationToken);
            try
            {
                await _started.Task.WaitAsync(TestDeadline);
                if (_pendingSteering)
                {
                    await _waitingText.Task.WaitAsync(TestDeadline);
                    await run.SteerAsync("revise after the tool").WaitAsync(TestDeadline);
                    await Service.RequiredInputWaitStarted.Task.WaitAsync(TestDeadline);
                }
                else if (_asynchronous)
                    await Service.WaitingForToolResults.Task.WaitAsync(TestDeadline);
                return run;
            }
            catch { await StopAsync(run); throw; }
        }

        public void AssertCancelledAndPaired()
        {
            Assert.IsTrue(_cancelled.Task.IsCompleted, "The cooperative handler must finish cancellation before Result settles.");
            Assert.AreEqual(1, _calls);
            var calls = Service.ActivateChat.Messages.Where(message => message.FunctionCallBatch != null)
                .SelectMany(message => message.FunctionCallBatch!.Calls).Where(call => call.Id == CallId).ToArray();
            var results = Service.ActivateChat.Messages.Where(message => message.FunctionCallResultBatch != null)
                .SelectMany(message => message.FunctionCallResultBatch!.Results).Where(result => result.Call.Id == CallId).ToArray();
            Assert.AreEqual(1, calls.Length);
            Assert.AreEqual(1, results.Length, "A recorded tool call must receive exactly one cancellation result.");
            Assert.IsTrue(results[0].IsCancelled);
            Assert.IsTrue(results[0].IsError);
            Assert.IsTrue(Socket.IsDisposed, "Result must wait for socket cleanup.");
        }

        public void Dispose() => Socket.Dispose();
    }

    private sealed class SocketService : OpenAIService
    {
        private int _readRounds;
        public ScriptedSocket NextSocket { get; set; }
        public int Connections { get; private set; }
        public bool ObserveRequiredInputWait { get; init; }
        public TaskCompletionSource RequiredInputWaitStarted { get; } = NewSignal();
        public TaskCompletionSource WaitingForToolResults { get; } = NewSignal();
        public SocketService(ScriptedSocket socket, string model, Gpt6ReasoningMode mode)
            : base("offline-run-failure", model, new HttpClient(new NoHttpHandler()))
        {
            NextSocket = socket;
            Gpt6ReasoningMode = mode;
            DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 8, TimeoutSeconds = 30 };
        }

        protected override Task<WebSocket> ConnectRunWebSocketAsync(CancellationToken cancellationToken)
        { Connections++; return Task.FromResult<WebSocket>(NextSocket); }

        protected override Task<IReadOnlyList<FunctionCallResultBatch>> CollectPendingStreamingFunctionResultsAsync(CancellationToken cancellationToken)
        {
            WaitingForToolResults.TrySetResult();
            return base.CollectPendingStreamingFunctionResultsAsync(cancellationToken);
        }

        protected override IAsyncEnumerable<string> ReadStreamingResponseLinesAsync(
            HttpResponseMessage response, StreamDiagnostics diagnostics, CancellationToken cancellationToken)
            => ObserveReaderAsync(base.ReadStreamingResponseLinesAsync(response, diagnostics, cancellationToken),
                ++_readRounds, cancellationToken);

        private async IAsyncEnumerable<string> ObserveReaderAsync(IAsyncEnumerable<string> source,
            int round, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var observeFirstWait = ObserveRequiredInputWait && round == 3;
            if (observeFirstWait)
                await NextSocket.PendingPublished.Task.WaitAsync(cancellationToken);
            await using var reader = source.GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                var next = reader.MoveNextAsync();
                if (observeFirstWait)
                {
                    Assert.IsFalse(next.IsCompleted,
                        "The published pending event must wait for its blocked required tool result.");
                    RequiredInputWaitStarted.TrySetResult();
                    observeFirstWait = false;
                }
                if (!await next.ConfigureAwait(false)) yield break;
                yield return reader.Current;
            }
        }
    }

    private sealed class NoHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new AssertFailedException("A native Run must not fall back to HTTP.");
    }

    private sealed class ScriptedSocket : WebSocket
    {
        private readonly Channel<Frame> _incoming = Channel.CreateUnbounded<Frame>();
        private readonly CancellationTokenSource _aborted = new();
        private WebSocketState _state = WebSocketState.Open;
        private bool _pendingReceived;
        private Action? _beforeFirstAbort;
        public List<JsonElement> Sent { get; } = new();
        public Action<JsonElement>? OnSend { get; set; }
        public Func<JsonElement, Task>? BeforeSendReturns { get; set; }
        public Func<JsonElement, CancellationToken, Task>? SendOperation { get; set; }
        public Action? BeforeFirstAbort { set => _beforeFirstAbort = value; }
        public TaskCompletionSource PendingPublished { get; } = NewSignal();
        public TaskCompletionSource CloseConsumed { get; } = NewSignal();
        public TaskCompletionSource Aborted { get; } = NewSignal();
        public TaskCompletionSource CompletedPublished { get; } = NewSignal();
        private bool _completedReceived;
        public bool IsDisposed { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public void Push(params string[] events)
        {
            foreach (var item in events)
                _incoming.Writer.TryWrite(new Frame(Encoding.UTF8.GetBytes(item), null));
        }
        public void CloseFromServer() => _incoming.Writer.TryWrite(new Frame(null, null));
        public WebSocketException? FailTransport(bool throwReceiveError)
        {
            var failure = throwReceiveError ? new WebSocketException("sentinel receive failure") : null;
            _incoming.Writer.TryWrite(new Frame(null, failure));
            return failure;
        }
        public override void Abort()
        {
            Interlocked.Exchange(ref _beforeFirstAbort, null)?.Invoke();
            _state = WebSocketState.Aborted;
            _aborted.Cancel();
            Aborted.TrySetResult();
        }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? description, CancellationToken cancellationToken)
        { Abort(); return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? description, CancellationToken cancellationToken)
            => CloseAsync(closeStatus, description, cancellationToken);
        public override void Dispose() { IsDisposed = true; Abort(); }
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            // Re-entry happens after the Run receiver has processed and published the
            // preceding frame, giving the required-input test a deterministic barrier.
            if (_pendingReceived) PendingPublished.TrySetResult();
            if (_completedReceived) CompletedPublished.TrySetResult();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _aborted.Token);
            var frame = await _incoming.Reader.ReadAsync(linked.Token);
            if (frame.Error != null) throw frame.Error;
            if (frame.Bytes == null)
            {
                CloseConsumed.TrySetResult();
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
            }
            if (Encoding.UTF8.GetString(frame.Bytes).Contains("response.steer.pending", StringComparison.Ordinal))
                _pendingReceived = true;
            if (Encoding.UTF8.GetString(frame.Bytes).Contains("response.completed", StringComparison.Ordinal))
                _completedReceived = true;
            Assert.IsTrue(frame.Bytes.Length <= buffer.Count);
            frame.Bytes.CopyTo(buffer.Array!, buffer.Offset);
            return new WebSocketReceiveResult(frame.Bytes.Length, WebSocketMessageType.Text, true);
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(buffer.AsMemory());
            var payload = document.RootElement.Clone();
            Sent.Add(payload);
            OnSend?.Invoke(payload);
            return SendOperation?.Invoke(payload, cancellationToken) ?? BeforeSendReturns?.Invoke(payload) ?? Task.CompletedTask;
        }

        public async Task WaitForSendAbortAsync(bool cancellationFailure, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _aborted.Token);
            try { await Task.Delay(Timeout.Infinite, linked.Token); }
            catch (OperationCanceledException) when (_aborted.IsCancellationRequested && !cancellationFailure)
            { throw new WebSocketException("pending send aborted by socket cleanup"); }
        }
        private sealed record Frame(byte[]? Bytes, Exception? Error);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string Created(string id, string model) => JsonSerializer.Serialize(new { type = "response.created", response = new { id, model, status = "in_progress" } });
    private static string Text(string text) => JsonSerializer.Serialize(new { type = "response.output_text.delta", delta = text });
    private static string Completed(string id) => JsonSerializer.Serialize(new { type = "response.completed", response = new { id, status = "completed", output = Array.Empty<object>() } });
    private static string Accepted(string id) => JsonSerializer.Serialize(new { type = "response.steer.accepted", steer = new { id = "steer_1", previous_response_id = id } });
    private static string Pending(string id, string callId) => JsonSerializer.Serialize(new { type = "response.steer.pending", steer = new { id = "steer_1", previous_response_id = id }, required_input = new[] { new { type = "function_call_output", call_id = callId } } });
    private static string ToolCompleted(string id, string callId, bool asynchronous) => JsonSerializer.Serialize(new
    {
        type = "response.completed", response = new
        {
            id, status = "completed", output = new[] { new { type = "function_call", id = "fc_slow", call_id = callId, name = "slow", arguments = "{}", status = "completed", @async = asynchronous } }
        }
    });
}
