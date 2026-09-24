using Mythosia.AI.Attributes;
using Mythosia.AI.Builders;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("FunctionCalling")]
public class OpenAIAsyncToolCallingTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    public void FunctionBuilder_AsyncPermissionIsOptInAndCanBeDisabled()
    {
        Assert.IsFalse(new FunctionDefinition().AllowAsync);
        Assert.IsFalse(FunctionBuilder.Create("default_tool").Build().AllowAsync);
        Assert.IsTrue(FunctionBuilder.Create("async_tool").WithAsync().Build().AllowAsync);
        Assert.IsFalse(FunctionBuilder.Create("sync_tool").WithAsync().WithAsync(false).Build().AllowAsync);
    }

    [TestMethod]
    public async Task AttributeRegistration_PreservesAsyncPermissionForInstanceAndStaticFunctions()
    {
        var handler = new ScriptedHandler(TextResponse("done"));
        var service = CreateService(handler);
        service.WithFunctions(new AttributedTools());
        service.WithStaticFunctions<AttributedTools>();

        await service.GetCompletionAsync("List available tools.");

        Assert.IsTrue(service.Functions.Single(function => function.Name == "instance_async").AllowAsync);
        Assert.IsTrue(service.Functions.Single(function => function.Name == "static_async").AllowAsync);
        Assert.IsFalse(service.Functions.Single(function => function.Name == "default_sync").AllowAsync);
        using var document = JsonDocument.Parse(handler.Requests[0].Body);
        foreach (var tool in document.RootElement.GetProperty("tools").EnumerateArray())
        {
            if (tool.GetProperty("name").GetString() == "default_sync")
                Assert.IsFalse(tool.TryGetProperty("async", out _));
            else
                Assert.IsTrue(tool.GetProperty("async").GetBoolean());
        }
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra)]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task AsyncCall_ContinuesModelBeforeHandlerFinishesAndReplaysItsResultOnce(string model)
    {
        var gate = new GatedTool("weather");
        var handler = new ScriptedHandler(
            ToolResponse(Call("weather", isAsync: true)),
            TextResponse("I can explain packing while weather is loading.", phase: "commentary", includeReasoning: true),
            TextResponse("The weather is sunny."));
        var service = CreateService(handler, gate.Definition);
        service.ChangeModel(model);
        service.ForceFunctionName = "weather";
        var completion = service.GetCompletionAsync("Get the weather and explain packing.");

        try
        {
            await gate.Started.Task.WaitAsync(TestTimeout);
            var continuingRequest = await handler.RequestAt(1).WaitAsync(TestTimeout);
            Assert.IsFalse(completion.IsCompleted, "A completed model round must not abandon its pending tool.");
            AssertOutputs(continuingRequest);
            using (var document = JsonDocument.Parse(continuingRequest.Body))
            {
                var input = document.RootElement.GetProperty("input").EnumerateArray().ToArray();
                Assert.AreEqual("auto", document.RootElement.GetProperty("tool_choice").GetString(),
                    "Forced selection applies only to the first model request.");
                var reasoning = input.Single(item => HasType(item, "reasoning"));
                Assert.AreEqual("encrypted-async-reasoning", reasoning.GetProperty("encrypted_content").GetString());
                var call = input.Single(item => HasType(item, "function_call"));
                Assert.AreEqual("call_weather", call.GetProperty("call_id").GetString());
                Assert.IsTrue(call.GetProperty("async").GetBoolean());
            }

            gate.Complete("sunny");
            AssertResponseTexts(await completion.WaitAsync(TestTimeout),
                "I can explain packing while weather is loading.", "The weather is sunny.");
            Assert.AreEqual(3, handler.Requests.Count);
            AssertOutputs(handler.Requests[2], ("call_weather", "sunny"));
            using (var finalRequest = JsonDocument.Parse(handler.Requests[2].Body))
            {
                var input = finalRequest.RootElement.GetProperty("input").EnumerateArray().ToArray();
                Assert.IsTrue(input.Any(item => HasType(item, "reasoning") &&
                    item.GetProperty("encrypted_content").GetString() == "encrypted-intermediate-reasoning"));
                var intermediateMessage = input.Single(item => HasType(item, "message") &&
                    item.GetProperty("content")[0].GetProperty("text").GetString() == "I can explain packing while weather is loading.");
                Assert.AreEqual("commentary", intermediateMessage.GetProperty("phase").GetString());
            }
            AssertHistoryResults(service, "call_weather");
            Assert.IsTrue(service.ActivateChat.Messages.SelectMany(message =>
                message.FunctionCallBatch?.Calls ?? Array.Empty<FunctionCall>()).Single().IsAsync);
        }
        finally
        {
            gate.Complete("sunny");
            await DrainAsync(completion);
        }
    }

    [TestMethod]
    public async Task AsyncBatch_DeliversEachCompletedSubsetWithoutWaitingForRemainingCalls()
    {
        var first = new GatedTool("first");
        var second = new GatedTool("second");
        var handler = new ScriptedHandler(
            ToolResponse(Call("first", true), Call("second", true)),
            TextResponse("Both tools are running."),
            TextResponse("The first result is ready."),
            TextResponse("Both results are ready."));
        var service = CreateService(handler, first.Definition, second.Definition);
        var completion = service.GetCompletionAsync("Start both independent tools.");

        try
        {
            await Task.WhenAll(first.Started.Task, second.Started.Task).WaitAsync(TestTimeout);
            AssertOutputs(await handler.RequestAt(1).WaitAsync(TestTimeout));

            first.Complete("first-result");
            var partialRequest = await handler.RequestAt(2).WaitAsync(TestTimeout);
            AssertOutputs(partialRequest, ("call_first", "first-result"));
            Assert.IsFalse(completion.IsCompleted);
            Assert.IsFalse(second.IsCompleted);

            second.Complete("second-result");
            AssertResponseTexts(await completion.WaitAsync(TestTimeout),
                "Both tools are running.", "The first result is ready.", "Both results are ready.");
            AssertOutputs(handler.Requests[3], ("call_first", "first-result"), ("call_second", "second-result"));
            AssertHistoryResults(service, "call_first", "call_second");
            Assert.AreEqual(1, first.InvocationCount);
            Assert.AreEqual(1, second.InvocationCount);
        }
        finally
        {
            first.Complete("first-result");
            second.Complete("second-result");
            await DrainAsync(completion);
        }
    }

    [TestMethod]
    public async Task MixedBatch_WaitsForSynchronousCallWhileAsyncCallRemainsPending()
    {
        var slow = new GatedTool("slow");
        var required = new GatedTool("required");
        var handler = new ScriptedHandler(
            ToolResponse(Call("slow", true), Call("required", false)),
            TextResponse("The required result is ready."),
            TextResponse("Everything is ready."));
        var service = CreateService(handler, slow.Definition, required.Definition);
        var completion = service.GetCompletionAsync("Use both tools.");

        try
        {
            await Task.WhenAll(slow.Started.Task, required.Started.Task).WaitAsync(TestTimeout);
            Assert.AreEqual(1, handler.Requests.Count, "The synchronous result is still required for model continuation.");
            required.Complete("required-result");
            var continuingRequest = await handler.RequestAt(1).WaitAsync(TestTimeout);
            AssertOutputs(continuingRequest, ("call_required", "required-result"));
            Assert.IsFalse(slow.IsCompleted);
            Assert.IsFalse(completion.IsCompleted);

            slow.Complete("slow-result");
            AssertResponseTexts(await completion.WaitAsync(TestTimeout),
                "The required result is ready.", "Everything is ready.");
            AssertOutputs(handler.Requests[2], ("call_required", "required-result"), ("call_slow", "slow-result"));
            AssertHistoryResults(service, "call_slow", "call_required");
        }
        finally
        {
            slow.Complete("slow-result");
            required.Complete("required-result");
            await DrainAsync(completion);
        }
    }

    [TestMethod]
    public async Task SynchronousWaitBeforeAsyncLaunch_StartsLaunchBeforeAwaitingDependentHandler()
    {
        var slow = new GatedTool("slow");
        var waiting = new FunctionDefinition
        {
            Name = "wait_for_launch",
            Handler = async _ =>
            {
                await slow.Started.Task.WaitAsync(TestTimeout);
                return "launch-confirmed";
            }
        };
        var handler = new ScriptedHandler(
            ToolResponse(Call("wait_for_launch", false), Call("slow", true)),
            TextResponse("The launch was confirmed."),
            TextResponse("The slow result is ready."));
        var service = CreateService(handler, waiting, slow.Definition);
        var completion = service.GetCompletionAsync("Start the slow tool and wait for its launch.");

        try
        {
            await slow.Started.Task.WaitAsync(TestTimeout);
            AssertOutputs(await handler.RequestAt(1).WaitAsync(TestTimeout), ("call_wait_for_launch", "launch-confirmed"));
            Assert.IsFalse(slow.IsCompleted);
            slow.Complete("slow-result");
            AssertResponseTexts(await completion.WaitAsync(TestTimeout),
                "The launch was confirmed.", "The slow result is ready.");
            AssertHistoryResults(service, "call_wait_for_launch", "call_slow");
        }
        finally
        {
            slow.Complete("slow-result");
            await DrainAsync(completion);
        }
    }

    [TestMethod]
    public async Task AsyncConcurrencyLimit_SpansModelRoundsWithoutBlockingRequiredCalls()
    {
        var first = new GatedTool("first");
        var second = new GatedTool("second");
        var activeCount = 0;
        var peakCount = 0;
        foreach (var tool in new[] { first, second })
        {
            var originalHandler = tool.Definition.Handler!;
            tool.Definition.Handler = async arguments =>
            {
                var active = Interlocked.Increment(ref activeCount);
                RecordMaximum(ref peakCount, active);
                try { return await originalHandler(arguments); }
                finally { Interlocked.Decrement(ref activeCount); }
            };
        }
        var requiredStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var required = new FunctionDefinition
        {
            Name = "required",
            Handler = _ =>
            {
                requiredStarted.TrySetResult();
                return Task.FromResult("required-result");
            }
        };
        var handler = new ScriptedHandler(
            Reply(ToolResponse(Call("first", true))),
            async _ =>
            {
                await first.Started.Task.WaitAsync(TestTimeout);
                return await Reply(ToolResponse(Call("second", true), Call("required", false)))(CancellationToken.None);
            },
            Reply(TextResponse("The first lookup is still running.")),
            Reply(TextResponse("The first lookup completed; the second is running.")),
            Reply(TextResponse("Both lookups completed.")));
        var service = CreateService(handler, first.Definition, second.Definition, required);
        service.DefaultPolicy = new FunctionCallingPolicy { MaxConcurrency = 1 };
        var completion = service.GetCompletionAsync("Perform the lookups and required work.");

        try
        {
            await first.Started.Task.WaitAsync(TestTimeout);
            await requiredStarted.Task.WaitAsync(TestTimeout);
            AssertOutputs(await handler.RequestAt(2).WaitAsync(TestTimeout), ("call_required", "required-result"));
            Assert.IsFalse(second.Started.Task.IsCompleted, "The second round's native call must wait for the occupied pool slot.");
            Assert.AreEqual(1, Volatile.Read(ref activeCount));

            first.Complete("first-result");
            await second.Started.Task.WaitAsync(TestTimeout);
            AssertOutputs(await handler.RequestAt(3).WaitAsync(TestTimeout),
                ("call_required", "required-result"), ("call_first", "first-result"));
            Assert.IsFalse(completion.IsCompleted);
            Assert.AreEqual(1, Volatile.Read(ref activeCount));

            second.Complete("second-result");
            AssertResponseTexts(await completion.WaitAsync(TestTimeout),
                "The first lookup is still running.", "The first lookup completed; the second is running.", "Both lookups completed.");
            Assert.AreEqual(1, peakCount, "One native pool slot must be shared by calls launched in different model rounds.");
            Assert.AreEqual(0, activeCount);
            Assert.AreEqual(5, handler.Requests.Count);
            AssertHistoryResults(service, "call_first", "call_second", "call_required");
        }
        finally
        {
            first.Complete("first-result");
            second.Complete("second-result");
            await DrainAsync(completion);
        }
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra, true, false, true)]
    [DataRow(AIModels.OpenAI.Gpt6Astra, false, true, false)]
    [DataRow(AIModels.OpenAI.Gpt5_6Sol, true, true, false)]
    [DataRow("gpt-6-unknown", true, true, false)]
    [DataRow("gpt-6-sol-experimental", true, true, false)]
    [DataRow("gpt-6-luna-2026-99-99", true, true, false)]
    public async Task IneligibleCall_PreservesSynchronousExecution(
        string model, bool allowAsync, bool responseAsync, bool expectedRequestPermission)
    {
        var gate = new GatedTool("weather");
        gate.Definition.AllowAsync = allowAsync;
        var handler = new ScriptedHandler(
            ToolResponse(Call("weather", responseAsync)),
            TextResponse("done"));
        var service = CreateService(handler, gate.Definition);
        service.ChangeModel(model);
        var completion = service.GetCompletionAsync("Get the weather.");

        try
        {
            await gate.Started.Task.WaitAsync(TestTimeout);
            Assert.AreEqual(1, handler.Requests.Count, "Model continuation must wait for an ineligible asynchronous call.");
            using (var document = JsonDocument.Parse(handler.Requests[0].Body))
            {
                var tool = document.RootElement.GetProperty("tools")[0];
                if (expectedRequestPermission)
                    Assert.IsTrue(tool.GetProperty("async").GetBoolean());
                else
                    Assert.IsFalse(tool.TryGetProperty("async", out _));
            }

            Assert.AreEqual(allowAsync, gate.Definition.AllowAsync, "Capability checks must not overwrite the caller's setting.");
            gate.Complete("sunny");
            Assert.AreEqual("done", await completion.WaitAsync(TestTimeout));
            AssertOutputs(handler.Requests[1], ("call_weather", "sunny"));
            AssertHistoryResults(service, "call_weather");
        }
        finally
        {
            gate.Complete("sunny");
            await DrainAsync(completion);
        }
    }

    [TestMethod]
    public async Task UnsupportedProvider_OmitsAsyncOptionAndWaitsForOrdinaryToolResult()
    {
        var gate = new GatedTool("weather");
        var handler = new ScriptedHandler(
            "{\"id\":\"msg_tools\",\"type\":\"message\",\"role\":\"assistant\",\"stop_reason\":\"tool_use\",\"content\":[{\"type\":\"tool_use\",\"id\":\"call_weather\",\"name\":\"weather\",\"input\":{}}]}",
            "{\"id\":\"msg_final\",\"type\":\"message\",\"role\":\"assistant\",\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"done\"}]}");
        var service = new AnthropicService("offline-test-key", AIModels.Anthropic.ClaudeOpus5, new HttpClient(handler));
        service.Functions.Add(gate.Definition);
        var completion = service.GetCompletionAsync("Get the weather.");

        try
        {
            await gate.Started.Task.WaitAsync(TestTimeout);
            Assert.AreEqual(1, handler.Requests.Count);
            using (var document = JsonDocument.Parse(handler.Requests[0].Body))
                Assert.IsFalse(document.RootElement.GetProperty("tools")[0].TryGetProperty("async", out _));
            Assert.IsTrue(gate.Definition.AllowAsync);
            gate.Complete("sunny");
            Assert.AreEqual("done", await completion.WaitAsync(TestTimeout));
            Assert.AreEqual(2, handler.Requests.Count);
            AssertHistoryResults(service, "call_weather");
        }
        finally
        {
            gate.Complete("sunny");
            await DrainAsync(completion);
        }
    }

    [TestMethod]
    public async Task AsyncHandlerFailure_IsDeliveredAsOneErrorResultAndDoesNotAbandonOtherCalls()
    {
        var slow = new GatedTool("slow");
        var failing = new GatedTool("failing");
        var handler = new ScriptedHandler(
            ToolResponse(Call("failing", true), Call("slow", true)),
            TextResponse("Both tools are running."),
            TextResponse("One tool failed; the other is running."),
            TextResponse("The remaining tool finished."));
        var service = CreateService(handler, failing.Definition, slow.Definition);
        var completion = service.GetCompletionAsync("Use both tools.");

        try
        {
            await Task.WhenAll(slow.Started.Task, failing.Started.Task).WaitAsync(TestTimeout);
            AssertOutputs(await handler.RequestAt(1).WaitAsync(TestTimeout));
            failing.Fail(new InvalidOperationException("weather unavailable"));
            var request = await handler.RequestAt(2).WaitAsync(TestTimeout);
            AssertOutputs(request, ("call_failing", "Error executing function: weather unavailable"));
            Assert.IsFalse(slow.IsCompleted);
            slow.Complete("slow-result");
            AssertResponseTexts(await completion.WaitAsync(TestTimeout),
                "Both tools are running.", "One tool failed; the other is running.", "The remaining tool finished.");
            AssertHistoryResults(service, "call_failing", "call_slow");
            var results = HistoryResults(service);
            Assert.IsTrue(results.Single(result => result.Call.Id == "call_failing").IsError);
            Assert.IsFalse(results.Single(result => result.Call.Id == "call_slow").IsError);
        }
        finally
        {
            failing.Fail(new InvalidOperationException("weather unavailable"));
            slow.Complete("slow-result");
            await DrainAsync(completion);
        }
    }

    [TestMethod]
    public async Task ProviderFailure_DrainsStartedHandlersBeforeReturningAndDoesNotSendAnotherRequest()
    {
        var gate = new GatedTool("weather");
        var handler = new ScriptedHandler(
            Reply(ToolResponse(Call("weather", true))),
            ReplyAfterStarted(gate, "{\"error\":{\"message\":\"provider rejected continuation\"}}", HttpStatusCode.BadRequest));
        var service = CreateService(handler, gate.Definition);
        var completion = service.GetCompletionAsync("Get the weather.");

        try
        {
            await handler.RequestAt(1).WaitAsync(TestTimeout);
            Assert.IsFalse(completion.IsCompleted, "Failure cleanup must account for already-started, uncancellable handlers.");
            gate.Complete("sunny");
            await Assert.ThrowsExactlyAsync<AIServiceException>(() => completion.WaitAsync(TestTimeout));
            Assert.AreEqual(2, handler.Requests.Count);
            AssertHistoryResults(service, "call_weather");
        }
        finally
        {
            gate.Complete("sunny");
            await DrainAsync(completion);
        }
    }

    [TestMethod]
    public async Task DuplicateCallIdInLaterRound_DoesNotExecuteTheSameCallTwice()
    {
        var gate = new GatedTool("weather");
        var handler = new ScriptedHandler(
            Reply(ToolResponse(Call("weather", true))),
            ReplyAfterStarted(gate, ToolResponse(Call("weather", true))));
        var service = CreateService(handler, gate.Definition);
        var completion = service.GetCompletionAsync("Get the weather.");

        try
        {
            await handler.RequestAt(1).WaitAsync(TestTimeout);
            gate.Complete("sunny");
            var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => completion.WaitAsync(TestTimeout));
            StringAssert.Contains(exception.Message, "call_weather");
            Assert.AreEqual(1, gate.InvocationCount);
            Assert.AreEqual(2, handler.Requests.Count);
            AssertHistoryResults(service, "call_weather");
        }
        finally
        {
            gate.Complete("sunny");
            await DrainAsync(completion);
        }
    }

    [TestMethod]
    public async Task MaximumRounds_DrainsStartedHandlersAndKeepsHistoryConsistent()
    {
        var gate = new GatedTool("weather");
        var waiting = new FunctionDefinition
        {
            Name = "wait_for_launch",
            Handler = async _ =>
            {
                await gate.Started.Task.WaitAsync(TestTimeout);
                return "launch-confirmed";
            }
        };
        var handler = new ScriptedHandler(ToolResponse(Call("weather", true), Call("wait_for_launch", false)));
        var service = CreateService(handler, gate.Definition, waiting);
        service.DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 1 };
        var completion = service.GetCompletionAsync("Get the weather.");

        try
        {
            await gate.Started.Task.WaitAsync(TestTimeout);
            Assert.IsFalse(completion.IsCompleted);
            gate.Complete("sunny");
            var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => completion.WaitAsync(TestTimeout));
            StringAssert.Contains(exception.Message, "Maximum rounds");
            Assert.AreEqual(1, gate.InvocationCount);
            Assert.AreEqual(1, handler.Requests.Count);
            AssertHistoryResults(service, "call_weather", "call_wait_for_launch");
            Assert.AreEqual("sunny", HistoryResults(service).Single(result => result.Call.Id == "call_weather").Content);
        }
        finally
        {
            gate.Complete("sunny");
            await DrainAsync(completion);
        }
    }

    [TestMethod]
    public async Task IncompleteResponse_DoesNotStartAsyncHandlersOrCommitPartialCalls()
    {
        var gate = new GatedTool("weather");
        var handler = new ScriptedHandler(ToolResponse(Call("weather", true)).Replace(
            "\"status\":\"completed\",\"output\"", "\"status\":\"incomplete\",\"output\"", StringComparison.Ordinal));
        var service = CreateService(handler, gate.Definition);

        await Assert.ThrowsExactlyAsync<AIServiceException>(() => service.GetCompletionAsync("Get the weather."));

        Assert.AreEqual(0, gate.InvocationCount);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.FunctionCallBatch != null));
        Assert.AreEqual(0, HistoryResults(service).Length);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Stream_PendingCallsDoNotMarkIntermediateRoundFinalOrCompleteTheStream()
    {
        var gate = new GatedTool("weather");
        var handler = new ScriptedHandler(
            AsStream(ToolResponse(Call("weather", true))),
            AsStream(TextResponse("Packing advice."), "Packing advice."),
            AsStream(TextResponse("Weather is sunny."), "Weather is sunny."));
        var service = CreateService(handler, gate.Definition);
        var events = new ConcurrentQueue<StreamingContent>();
        var consumption = ConsumeAsync(service, events);

        try
        {
            await gate.Started.Task.WaitAsync(TestTimeout);
            await handler.RequestAt(1).WaitAsync(TestTimeout);
            Assert.IsFalse(consumption.IsCompleted);
            Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Completion));
            Assert.IsFalse(events.Any(item => item.IsFinalRound));
            AssertOutputs(handler.Requests[1]);

            gate.Complete("sunny");
            await consumption.WaitAsync(TestTimeout);
            Assert.AreEqual(3, handler.Requests.Count);
            AssertOutputs(handler.Requests[2], ("call_weather", "sunny"));
            Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
            Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.FunctionCall));
            Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.FunctionResult));
            Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error));
            CollectionAssert.AreEqual(new[] { false, false, true }, events
                .Where(item => item.Type == StreamingContentType.RoundUsage)
                .Select(item => item.IsFinalRound).ToArray());
            Assert.IsTrue(events.Single(item => item.Type == StreamingContentType.FunctionCall).FunctionCall!.IsAsync);
            AssertHistoryResults(service, "call_weather");
        }
        finally
        {
            gate.Complete("sunny");
            await DrainAsync(consumption);
        }
    }

    [TestMethod]
    public async Task Stream_CompletedSynchronousFlagOverridesEarlierAsyncCallAnnouncement()
    {
        var gate = new GatedTool("weather");
        var toolStream = "data: {\"type\":\"response.output_item.added\",\"output_index\":1,\"item\":" +
            Call("weather", true) + "}\n\n" + AsStream(ToolResponse(Call("weather", false)));
        var handler = new ScriptedHandler(
            toolStream,
            AsStream(TextResponse("done"), "done"));
        var service = CreateService(handler, gate.Definition);
        var events = new ConcurrentQueue<StreamingContent>();
        var consumption = ConsumeAsync(service, events);

        try
        {
            await gate.Started.Task.WaitAsync(TestTimeout);
            Assert.AreEqual(1, handler.Requests.Count,
                "The completed call's async:false must prevent continuation until its required result is available.");
            Assert.IsFalse(consumption.IsCompleted);
            gate.Complete("sunny");
            await consumption.WaitAsync(TestTimeout);

            Assert.AreEqual(2, handler.Requests.Count);
            AssertOutputs(handler.Requests[1], ("call_weather", "sunny"));
            var storedCall = service.ActivateChat.Messages.SelectMany(message =>
                message.FunctionCallBatch?.Calls ?? Array.Empty<FunctionCall>()).Single();
            Assert.IsFalse(storedCall.IsAsync, "History must preserve the final provider flag, regardless of an earlier stream announcement.");
            AssertHistoryResults(service, "call_weather");
            Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
            Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error));
        }
        finally
        {
            gate.Complete("sunny");
            await DrainAsync(consumption);
        }
    }

    [TestMethod]
    public async Task StreamCancellation_DrainsStartedHandlersBeforePropagatingCancellation()
    {
        var gate = new GatedTool("weather");
        using var cancellation = new CancellationTokenSource();
        var handler = new ScriptedHandler(
            Reply(AsStream(ToolResponse(Call("weather", true)))),
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("An infinite delay should only exit by cancellation.");
            });
        var service = CreateService(handler, gate.Definition);
        var events = new ConcurrentQueue<StreamingContent>();
        var consumption = ConsumeAsync(service, events, cancellation.Token);

        try
        {
            await handler.RequestAt(1).WaitAsync(TestTimeout);
            await gate.Started.Task.WaitAsync(TestTimeout);
            cancellation.Cancel();
            Assert.IsFalse(consumption.IsCompleted);
            gate.Complete("sunny");
            await Assert.ThrowsAsync<OperationCanceledException>(() => consumption.WaitAsync(TestTimeout));
            Assert.AreEqual(2, handler.Requests.Count);
            Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Completion));
            AssertHistoryResults(service, "call_weather");
        }
        finally
        {
            cancellation.Cancel();
            gate.Complete("sunny");
            await DrainAsync(consumption);
        }
    }

    [TestMethod]
    public async Task StreamFailure_DrainsEarlierCallsWithoutExecutingTheFailedRoundsPartialCall()
    {
        var earlier = new GatedTool("weather");
        var incomplete = new GatedTool("incomplete");
        var failedRound = "data: {\"type\":\"response.output_item.done\",\"output_index\":0,\"item\":" +
            Call("incomplete", true) + "}\n\n" +
            "data: {\"type\":\"response.incomplete\",\"response\":{\"id\":\"resp_incomplete\",\"status\":\"incomplete\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"}}}\n\n";
        var handler = new ScriptedHandler(
            Reply(AsStream(ToolResponse(Call("weather", true)))),
            ReplyAfterStarted(earlier, failedRound));
        var service = CreateService(handler, earlier.Definition, incomplete.Definition);
        var events = new ConcurrentQueue<StreamingContent>();
        var consumption = ConsumeAsync(service, events);

        try
        {
            await handler.RequestAt(1).WaitAsync(TestTimeout);
            Assert.IsFalse(consumption.IsCompleted);
            earlier.Complete("sunny");
            await consumption.WaitAsync(TestTimeout);
            Assert.AreEqual(0, incomplete.InvocationCount);
            Assert.AreEqual(2, handler.Requests.Count);
            Assert.IsTrue(events.Any(item => item.Type == StreamingContentType.Error));
            Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Completion));
            AssertHistoryResults(service, "call_weather");
        }
        finally
        {
            earlier.Complete("sunny");
            incomplete.Complete("must not execute");
            await DrainAsync(consumption);
        }
    }

    [TestMethod]
    public async Task StreamContextOverflow_WithPendingToolTerminatesWithoutCompactionOrRetry()
    {
        const string overflow = """
            {"error":{"message":"This model's maximum context length is 128000 tokens. However, your messages resulted in 130000 tokens.","type":"invalid_request_error","code":"context_length_exceeded"}}
            """;
        var gate = new GatedTool("weather");
        var handler = new ScriptedHandler(
            Reply(AsStream(ToolResponse(Call("weather", true)))),
            ReplyAfterStarted(gate, overflow, HttpStatusCode.BadRequest));
        var service = CreateService(handler, gate.Definition);
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 100, keepRecentCount: 2);
        service.ConversationPolicy.CurrentSummary = "Existing summary.";
        for (var index = 0; index < 2; index++)
        {
            service.ActivateChat.Messages.Add(new Message(ActorRole.User, $"old question {index}"));
            service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, $"old answer {index}"));
        }
        var events = new ConcurrentQueue<StreamingContent>();
        var consumption = ConsumeAsync(service, events);

        try
        {
            await gate.Started.Task.WaitAsync(TestTimeout);
            await handler.RequestAt(1).WaitAsync(TestTimeout);
            Assert.IsFalse(consumption.IsCompleted, "The terminal provider error must still drain the pending handler.");
            gate.Complete("sunny");
            await consumption.WaitAsync(TestTimeout);

            var error = events.Single(item => item.Type == StreamingContentType.Error);
            Assert.AreEqual(true, error.Metadata![AIHttpErrorFactory.ContextLengthExceededKey]);
            Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Completion));
            Assert.AreEqual(2, handler.Requests.Count, "Pending work must prevent an internal summary request and replay of the failed round.");
            Assert.AreEqual("Existing summary.", service.ConversationPolicy.CurrentSummary);
            Assert.AreEqual(4, service.ActivateChat.Messages.Count(message => message.Content.StartsWith("old ", StringComparison.Ordinal)));
            AssertHistoryResults(service, "call_weather");
        }
        finally
        {
            gate.Complete("sunny");
            await DrainAsync(consumption);
        }
    }

    [TestMethod]
    public async Task StreamDisposal_DrainsPendingHandlersWithoutStartingAnotherModelRound()
    {
        var gate = new GatedTool("weather");
        var handler = new ScriptedHandler(AsStream(ToolResponse(Call("weather", true))));
        var service = CreateService(handler, gate.Definition);
        var enumerator = service.StreamAsync("Get the weather.", StreamOptions.WithFunctions).GetAsyncEnumerator();
        Task? disposal = null;

        try
        {
            while (await enumerator.MoveNextAsync().AsTask().WaitAsync(TestTimeout))
            {
                if (enumerator.Current.Type == StreamingContentType.RoundUsage)
                    break;
            }

            await gate.Started.Task.WaitAsync(TestTimeout);
            disposal = enumerator.DisposeAsync().AsTask();
            Assert.IsFalse(disposal.IsCompleted);
            gate.Complete("sunny");
            await disposal.WaitAsync(TestTimeout);
            Assert.AreEqual(1, handler.Requests.Count);
            AssertHistoryResults(service, "call_weather");
        }
        finally
        {
            gate.Complete("sunny");
            if (disposal != null)
                await DrainAsync(disposal);
            else
                await enumerator.DisposeAsync();
        }
    }

    [TestMethod]
    [DataRow(false, 2)]
    [DataRow(false, 3)]
    [DataRow(true, 2)]
    [DataRow(true, 3)]
    public async Task Summary_RetainsOriginalCallsAndEarlierPartialResultsAcrossInterleavedMessages(
        bool separateBatches, int keepRecentCount)
    {
        var handler = new ScriptedHandler(TextResponse("Summary of the old user message."));
        var service = CreateService(handler);
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
            triggerCount: 6, keepRecentCount: (uint)keepRecentCount);
        var first = new FunctionCall { Id = "call_first", Name = "first", Source = IdSource.OpenAI, IsAsync = true };
        var second = new FunctionCall { Id = "call_second", Name = "second", Source = IdSource.OpenAI, IsAsync = true, Index = 1 };
        var firstBatch = new FunctionCallBatch(separateBatches ? new[] { first } : new[] { first, second });
        var secondBatch = separateBatches ? new FunctionCallBatch(new[] { second }) : firstBatch;
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "old user message"));
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Started the first tool.")
        {
            FunctionCallBatch = firstBatch
        });
        service.ActivateChat.Messages.Add(separateBatches
            ? new Message(ActorRole.Assistant, "Started the second tool.") { FunctionCallBatch = secondBatch }
            : new Message(ActorRole.Assistant, "Independent work while both tools run."));
        service.ActivateChat.Messages.Add(new Message(ActorRole.Function, "first-result")
        {
            FunctionCallResultBatch = new FunctionCallResultBatch(firstBatch.Id,
                new[] { new FunctionCallResult { Call = first, Content = "first-result" } })
        });
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Working with the first result."));
        service.ActivateChat.Messages.Add(new Message(ActorRole.Function, "second-result")
        {
            FunctionCallResultBatch = new FunctionCallResultBatch(secondBatch.Id,
                new[] { new FunctionCallResult { Call = second, Content = "second-result" } })
        });
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Final answer from both results."));

        await service.ApplySummaryPolicyIfNeededAsync();

        Assert.AreEqual("Summary of the old user message.", service.ConversationPolicy.CurrentSummary);
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Content == "old user message"));
        AssertHistoryResults(service, "call_first", "call_second");
        Assert.AreEqual(firstBatch.Id, service.ActivateChat.Messages[0].FunctionCallBatch?.Id,
            "A retained asynchronous result must pull the boundary back to its original call, even through other batches.");
    }

    private static async Task ConsumeAsync(
        OpenAIService service,
        ConcurrentQueue<StreamingContent> events,
        CancellationToken cancellationToken = default)
    {
        await foreach (var item in service.StreamAsync("Use the weather tool.", StreamOptions.WithFunctions, cancellationToken))
            events.Enqueue(item);
    }

    private static OpenAIService CreateService(ScriptedHandler handler, params FunctionDefinition[] functions)
    {
        var service = new OpenAIService("offline-test-key", AIModels.OpenAI.Gpt6Astra, new HttpClient(handler));
        foreach (var function in functions)
            service.Functions.Add(function);
        return service;
    }

    private static Func<CancellationToken, Task<HttpResponseMessage>> ReplyAfterStarted(
        GatedTool tool, string response, HttpStatusCode status = HttpStatusCode.OK) => async token =>
    {
        await tool.Started.Task.WaitAsync(TestTimeout, token);
        return await Reply(response, status)(token);
    };

    private static string Call(string name, bool isAsync) => JsonSerializer.Serialize(new
    {
        id = $"fc_{name}",
        type = "function_call",
        status = "completed",
        call_id = $"call_{name}",
        name,
        arguments = "{}",
        @async = isAsync
    });

    private static string ToolResponse(params string[] calls) =>
        "{\"id\":\"resp_" + Guid.NewGuid().ToString("N") + "\",\"status\":\"completed\",\"output\":[" +
        "{\"id\":\"rs_" + Guid.NewGuid().ToString("N") + "\",\"type\":\"reasoning\",\"status\":\"completed\",\"summary\":[],\"encrypted_content\":\"encrypted-async-reasoning\"}," +
        string.Join(",", calls) + "],\"usage\":{\"input_tokens\":10,\"output_tokens\":5,\"total_tokens\":15}}";

    private static string TextResponse(string text, string phase = "final_answer", bool includeReasoning = false)
    {
        var output = new List<object>();
        if (includeReasoning)
        {
            output.Add(new { id = "rs_intermediate", type = "reasoning", status = "completed",
                summary = Array.Empty<object>(), encrypted_content = "encrypted-intermediate-reasoning" });
        }
        output.Add(new { id = $"msg_{Guid.NewGuid():N}", type = "message", status = "completed", role = "assistant", phase,
            content = new[] { new { type = "output_text", text } } });
        return JsonSerializer.Serialize(new
        {
            id = $"resp_{Guid.NewGuid():N}", status = "completed", output_text = text, output,
            usage = new { input_tokens = 10, output_tokens = 5, total_tokens = 15 }
        });
    }

    private static string AsStream(string response, string? delta = null) =>
        (delta == null ? string.Empty : "data: " + JsonSerializer.Serialize(new { type = "response.output_text.delta", delta }) + "\n\n") +
        "data: {\"type\":\"response.completed\",\"response\":" + response + "}\n\n";

    private static bool HasType(JsonElement item, string type) =>
        item.TryGetProperty("type", out var value) && value.GetString() == type;

    private static void AssertResponseTexts(string actual, params string[] expectedParts)
    {
        var offset = 0;
        foreach (var expectedPart in expectedParts)
        {
            var index = actual.IndexOf(expectedPart, offset, StringComparison.Ordinal);
            Assert.IsTrue(index >= offset, $"The returned response must preserve '{expectedPart}' in model order.");
            offset = index + expectedPart.Length;
        }
    }

    private static void RecordMaximum(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (current >= value)
                return;
        } while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static void AssertOutputs(CapturedRequest request, params (string Id, string Output)[] expected)
    {
        using var document = JsonDocument.Parse(request.Body);
        var outputs = document.RootElement.GetProperty("input").EnumerateArray()
            .Where(item => HasType(item, "function_call_output")).ToArray();
        Assert.AreEqual(expected.Length, outputs.Length, "Each result must occur exactly once in replayed history.");
        foreach (var (id, output) in expected)
            Assert.AreEqual(output, outputs.Single(item => item.GetProperty("call_id").GetString() == id).GetProperty("output").GetString());
    }

    private static FunctionCallResult[] HistoryResults(AIService service) =>
        service.ActivateChat.Messages.SelectMany(message =>
            message.FunctionCallResultBatch?.Results ?? Array.Empty<FunctionCallResult>()).ToArray();

    private static void AssertHistoryResults(AIService service, params string[] expectedCallIds)
    {
        var results = HistoryResults(service);
        CollectionAssert.AreEquivalent(expectedCallIds, results.Select(result => result.Call.Id).ToArray());
        CollectionAssert.AreEquivalent(expectedCallIds, service.ActivateChat.Messages.SelectMany(message =>
            message.FunctionCallBatch?.Calls ?? Array.Empty<FunctionCall>()).Select(call => call.Id).ToArray());
    }

    private static async Task DrainAsync(Task operation)
    {
        try { await operation.WaitAsync(TestTimeout); }
        catch { /* Preserve the original assertion while observing any operation failure. */ }
    }

    private static Func<CancellationToken, Task<HttpResponseMessage>> Reply(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        _ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8,
                body.StartsWith("data:", StringComparison.Ordinal) ? "text/event-stream" : "application/json")
        });

    private sealed class AttributedTools
    {
        [AiFunction("instance_async", "Asynchronous instance tool", AllowAsync = true)]
        public Task<string> InstanceAsync() => Task.FromResult("instance");

        [AiFunction("static_async", "Asynchronous static tool", AllowAsync = true)]
        public static Task<string> StaticAsync() => Task.FromResult("static");

        [AiFunction("default_sync", "Ordinary tool")]
        public string DefaultSync() => "ordinary";
    }

    private sealed class GatedTool
    {
        private readonly TaskCompletionSource<string> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _invocationCount;

        public GatedTool(string name)
        {
            Definition = new FunctionDefinition
            {
                Name = name,
                Description = name,
                AllowAsync = true,
                Handler = async _ =>
                {
                    Interlocked.Increment(ref _invocationCount);
                    Started.TrySetResult();
                    return await _result.Task;
                }
            };
        }

        public FunctionDefinition Definition { get; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsCompleted => _result.Task.IsCompleted;
        public int InvocationCount => Volatile.Read(ref _invocationCount);
        public void Complete(string value) => _result.TrySetResult(value);
        public void Fail(Exception exception) => _result.TrySetException(exception);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>>[] _responses;
        private readonly TaskCompletionSource<CapturedRequest>[] _requestSignals;
        private readonly ConcurrentQueue<CapturedRequest> _requests = new();
        private int _requestIndex = -1;

        public ScriptedHandler(params string[] responses) : this(responses.Select(response => Reply(response)).ToArray()) { }

        public ScriptedHandler(params Func<CancellationToken, Task<HttpResponseMessage>>[] responses)
        {
            _responses = responses;
            _requestSignals = responses.Select(_ => new TaskCompletionSource<CapturedRequest>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        }

        public IReadOnlyList<CapturedRequest> Requests => _requests.ToArray();
        public Task<CapturedRequest> RequestAt(int index) => _requestSignals[index].Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _requestIndex);
            var captured = new CapturedRequest(request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken));
            _requests.Enqueue(captured);
            if (index >= _responses.Length)
                throw new InvalidOperationException("The service requested an unexpected extra model round.");
            _requestSignals[index].TrySetResult(captured);
            return await _responses[index](cancellationToken);
        }
    }

    private sealed record CapturedRequest(Uri Uri, string Body);
}
