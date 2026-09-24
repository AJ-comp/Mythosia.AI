using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.OpenAI;

[TestClass]
[TestCategory("Live")]
[TestCategory("OpenAI")]
[TestCategory("Run")]
[DoNotParallelize]
public class OpenAIRunLiveTests
{
    [TestMethod]
    public async Task Astra_ResultCompletesWithoutStreamConsumer()
    {
        using var client = new HttpClient();
        var service = await CreateServiceAsync(AIModels.OpenAI.Gpt6Astra, client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var token = "RUN_FINAL_" + Guid.NewGuid().ToString("N");

        await using var run = await service.StartRunAsync("Reply with exactly: " + token,
            cancellationToken: cancellation.Token);
        Assert.IsTrue(run.CanSteer);
        var result = (await run.Result.WaitAsync(TimeSpan.FromMinutes(4))).Text;

        StringAssert.Contains(result, token);
        Console.WriteLine("LIVE_RUN_OK model=gpt-6-astra mode=result-only");
    }

    [TestMethod]
    public Task Astra_SteerDuringText_CallbackAndResultShareContinuation()
        => VerifyTextSteeringAsync(AIModels.OpenAI.Gpt6Astra);

    internal static async Task VerifyTextSteeringAsync(string model)
    {
        using var client = new HttpClient();
        var service = await CreateServiceAsync(model, client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var firstText = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new StringBuilder();
        var token = "RUN_STEER_" + Guid.NewGuid().ToString("N");

        await using var run = await service.StartRunAsync(
            "Write 60 numbered one-line descriptions of everyday objects, starting immediately with item 1. " +
            "Continue the list unless I give you a new instruction.",
            onText: text => { observed.Append(text); firstText.TrySetResult(); },
            cancellationToken: cancellation.Token);
        await AwaitSignalOrFailureAsync(firstText.Task, run, TimeSpan.FromSeconds(100));
        Assert.IsFalse(run.Result.IsCompleted, "Steering must be tested during the original output.");
        await run.SteerAsync("Stop the enumeration. Your next response must contain exactly this verification token: " + token,
            cancellation.Token);
        var result = (await run.Result.WaitAsync(TimeSpan.FromMinutes(3))).Text;

        StringAssert.Contains(result, token, "An accepted steering request must produce the actual continuation.");
        Assert.AreEqual(result, observed.ToString(), "The callback and Result must share the same generated text.");
        Console.WriteLine($"LIVE_RUN_OK model={model} mode=callback-steering");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Astra_SteerWhileToolPending_PreservesCallAndFinalCompletion(bool nativeAsync)
        => VerifyToolSteeringAsync(AIModels.OpenAI.Gpt6Astra, nativeAsync);

    internal static async Task VerifyToolSteeringAsync(string model, bool nativeAsync)
    {
        const string toolName = "read_run_token";
        using var client = new HttpClient();
        var service = await CreateServiceAsync(model, client, toolName);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toolToken = "RUN_TOOL_" + Guid.NewGuid().ToString("N");
        var steerToken = "RUN_DIRECTIVE_" + Guid.NewGuid().ToString("N");
        int invocations = 0;
        int finished = 0;
        service.SystemMessage =
            "For this integration check, perform exactly the requested action. Keep reasoning and output minimal. " +
            "Never guess a tool result.";
        service.Functions.Add(new FunctionDefinition
        {
            Name = toolName,
            Description = "Read the final answer's required verification token exactly once. No arguments. This may take time.",
            AllowAsync = nativeAsync,
            Handler = async _ =>
            {
                Interlocked.Increment(ref invocations);
                started.TrySetResult();
                try
                {
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(130));
                    return JsonSerializer.Serialize(new { verification_token = toolToken });
                }
                finally { Interlocked.Increment(ref finished); }
            }
        });

        await using var run = await service.StartRunAsync(
            nativeAsync
                ? "Call read_run_token exactly once using asynchronous execution, then say WAIT and end this response immediately. " +
                  "Do not wait for the result in this response. The application will provide it in a later response. " +
                  "Once the result arrives, include its actual verification_token verbatim in your final answer."
                : "Call read_run_token exactly once, then include its actual verification_token verbatim in your final answer.",
            cancellationToken: cancellation.Token);
        var events = new List<StreamingContent>();
        var observation = ObserveAsync(run, events, cancellation.Token);
        try
        {
            await AwaitSignalOrFailureAsync(started.Task, run, TimeSpan.FromSeconds(100));
            Assert.IsFalse(run.Result.IsCompleted);
            await run.SteerAsync("Also include the following token verbatim in your final answer alongside the tool result: " + steerToken,
                cancellation.Token);
            Assert.AreEqual(0, Volatile.Read(ref finished), "Steering must be accepted while the real tool handler is still blocked.");
        }
        finally { release.TrySetResult(); }

        var result = (await run.Result.WaitAsync(TimeSpan.FromMinutes(3))).Text;
        await observation.WaitAsync(TimeSpan.FromSeconds(30));
        var calls = service.ActivateChat.Messages
            .Where(message => message.FunctionCallBatch != null).SelectMany(message => message.FunctionCallBatch!.Calls).ToArray();
        var functionResults = events.Where(item => item.Type == StreamingContentType.FunctionResult).ToArray();
        Console.WriteLine("LIVE_RUN_CALLS " + JsonSerializer.Serialize(new
        {
            model, nativeAsync, invocations, finished,
            calls = calls.Select(call => new { call.Id, call.IsAsync }),
            resultCallIds = functionResults.Select(item => item.FunctionResult!.Call.Id)
        }));
        service.AssertSingleCallRequestPolicy(nativeAsync);
        StringAssert.Contains(result, toolToken);
        StringAssert.Contains(result, steerToken);
        Assert.AreEqual(1, invocations);
        Assert.AreEqual(1, finished);
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
        Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error));
        var call = Assert.ContainsSingle(calls);
        Assert.AreEqual(nativeAsync, call.IsAsync, "A synchronous call cannot pass the native async test.");
        var functionResult = Assert.ContainsSingle(functionResults);
        Assert.AreEqual(call.Id, functionResult.FunctionResult!.Call.Id);
        service.AssertSingleCallReplay(call.Id);
        Console.WriteLine($"LIVE_RUN_OK model={model} mode=tool-steering native_async={nativeAsync} handlers={invocations}");
    }

    [TestMethod]
    public async Task Sol_UnsupportedSteering_DoesNotPreventRunWithTools()
    {
        using var client = new HttpClient();
        var service = await CreateServiceAsync(AIModels.OpenAI.Gpt5_6Sol, client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var token = "RUN_FALLBACK_" + Guid.NewGuid().ToString("N");
        int invocations = 0;
        service.ForceFunctionName = "read_fallback_token";
        service.Functions.Add(new FunctionDefinition
        {
            Name = "read_fallback_token",
            Description = "Read the verification token. Call exactly once, with no arguments.",
            AllowAsync = true,
            Handler = _ =>
            {
                Interlocked.Increment(ref invocations);
                return Task.FromResult(token);
            }
        });

        await using var run = await service.StartRunAsync(
            "Call read_fallback_token exactly once, then reply with the exact token returned by that tool.",
            cancellationToken: cancellation.Token);
        Assert.IsFalse(run.CanSteer);
        await Assert.ThrowsAsync<NotSupportedException>(() => run.SteerAsync("additional instruction", cancellation.Token));
        var result = (await run.Result.WaitAsync(TimeSpan.FromMinutes(4))).Text;

        StringAssert.Contains(result, token);
        Assert.AreEqual(1, invocations);
        var call = Assert.ContainsSingle(service.ActivateChat.Messages
            .Where(message => message.FunctionCallBatch != null).SelectMany(message => message.FunctionCallBatch!.Calls));
        Assert.IsFalse(call.IsAsync);
        Console.WriteLine("LIVE_RUN_OK model=gpt-5.6-sol mode=unsupported-steering-tool-fallback");
    }

    private static async Task<ObservedRunService> CreateServiceAsync(string model, HttpClient client, string? singleCallToolName = null)
    {
        var key = await LiveTestSecrets.GetAsync("momedit-openai-secret");
        var service = new ObservedRunService(key, model, client, singleCallToolName)
        {
            MaxTokens = 4096,
            Gpt6ReasoningEffort = Gpt6Reasoning.Low,
            Gpt6ReasoningSummary = null,
            Gpt6Verbosity = Verbosity.Low,
            Gpt5_6ReasoningEffort = Gpt5_6Reasoning.None,
            Gpt5_6ReasoningSummary = null,
            DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 8, TimeoutSeconds = 240 }
        };
        service.StreamRawLineCallback = line =>
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) return;
            using var document = JsonDocument.Parse(line[6..]);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            if (type is not ("response.created" or "response.completed" or "response.incomplete" or
                "response.failed" or "error" or "response.steer.accepted" or "response.steer.pending" or "response.steer.failed")) return;
            var responseId = "";
            var reason = "";
            var outputs = "";
            var callIds = Array.Empty<string?>();
            if (root.TryGetProperty("response", out var response))
            {
                if (response.TryGetProperty("id", out var id)) responseId = id.GetString();
                if (response.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object &&
                    details.TryGetProperty("reason", out var reasonValue)) reason = reasonValue.GetString();
                if (response.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                {
                    outputs = string.Join(",", output.EnumerateArray().Select(item =>
                        (item.TryGetProperty("type", out var itemType) ? itemType.GetString() : "unknown") + ":" +
                        (item.TryGetProperty("status", out var itemStatus) ? itemStatus.GetString() : "none")));
                    callIds = output.EnumerateArray()
                        .Where(item => item.TryGetProperty("type", out var itemType) && itemType.GetString() == "function_call")
                        .Select(item => item.TryGetProperty("call_id", out var callId) ? callId.GetString() : null).ToArray();
                }
            }
            Console.WriteLine($"LIVE_RUN_EVENT type={type} response_id={responseId} incomplete_reason={reason} outputs={outputs} call_ids={string.Join(",", callIds)}");
        };
        return service;
    }

    // Run uses WebSocket, so observe the actual request factory rather than an HTTP handler.
    // Bound this fixture to one provider call while retaining the real pending handler and results.
    private sealed class ObservedRunService(string apiKey, string model, HttpClient client, string? singleCallToolName)
        : OpenAIService(apiKey, model, client)
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<RunRequestRecord> _requests = new();
        private int _requestCount;

        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            if (singleCallToolName == null) return base.CreateFunctionMessageRequest();
            var index = Interlocked.Increment(ref _requestCount);
            var previousMode = RequestFunctionCallMode;
            var previousForce = RequestForceFunctionName;
            HttpRequestMessage request;
            try
            {
                if (index == 1) SetExecutionSetting(nameof(ForceFunctionName), singleCallToolName);
                else SetExecutionSetting(nameof(FunctionCallMode), FunctionCallMode.None);
                request = base.CreateFunctionMessageRequest();
            }
            finally
            {
                SetExecutionSetting(nameof(FunctionCallMode), previousMode);
                SetExecutionSetting(nameof(ForceFunctionName), previousForce);
            }

            var body = System.Text.Json.Nodes.JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!;
            if (index == 1)
            {
                body["parallel_tool_calls"] = false;
                request.Content.Dispose();
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }
            using var document = JsonDocument.Parse(body.ToJsonString());
            var root = document.RootElement;
            var choice = root.GetProperty("tool_choice");
            var input = root.GetProperty("input").EnumerateArray().ToArray();
            var tool = root.GetProperty("tools").EnumerateArray()
                .Single(item => item.GetProperty("name").GetString() == singleCallToolName);
            var record = new RunRequestRecord(index,
                choice.ValueKind == JsonValueKind.String ? choice.GetString()! : choice.GetProperty("type").GetString()!,
                choice.ValueKind == JsonValueKind.Object ? choice.GetProperty("name").GetString() : null,
                root.TryGetProperty("parallel_tool_calls", out var parallel) ? parallel.GetBoolean() : null,
                tool.TryGetProperty("async", out var asyncFlag) && asyncFlag.ValueKind == JsonValueKind.True,
                input.Where(item => IsInputType(item, "function_call")).Select(item => item.GetProperty("call_id").GetString()!).ToArray(),
                input.Where(item => IsInputType(item, "function_call_output")).Select(item => item.GetProperty("call_id").GetString()!).ToArray());
            _requests.Enqueue(record);
            Console.WriteLine("LIVE_RUN_REQUEST " + JsonSerializer.Serialize(record));
            return request;
        }

        public void AssertSingleCallRequestPolicy(bool nativeAsync)
        {
            var requests = _requests.OrderBy(request => request.Index).ToArray();
            Assert.IsTrue(requests.Length >= 2, "The real run must send the first request and a continuation.");
            CollectionAssert.AreEqual(Enumerable.Range(1, requests.Length).ToArray(), requests.Select(request => request.Index).ToArray());
            Assert.AreEqual("function", requests[0].ToolChoice);
            Assert.AreEqual(singleCallToolName, requests[0].ForcedToolName);
            Assert.AreEqual(false, requests[0].ParallelToolCalls, "A forced first tool must not permit parallel duplicate calls.");
            Assert.IsTrue(requests.Skip(1).All(request => request.ToolChoice == "none" && request.ForcedToolName == null),
                "Every continuation must forbid new model calls while preserving registered tools and the pending result.");
            Assert.IsTrue(requests.All(request => request.ToolAllowsAsync == nativeAsync),
                "The actual tool declaration must retain the requested asynchronous permission on every request.");
        }

        public void AssertSingleCallReplay(string callId)
        {
            var requests = _requests.OrderBy(request => request.Index).ToArray();
            Assert.IsEmpty(requests[0].CallIds);
            Assert.IsEmpty(requests[0].OutputIds);
            foreach (var request in requests.Skip(1))
            {
                CollectionAssert.AreEqual(new[] { callId }, request.CallIds, "Continuations must replay the original provider call exactly once.");
                Assert.IsTrue(request.OutputIds.Length <= 1 && request.OutputIds.All(id => id == callId),
                    "Any tool result sent by a continuation must preserve the original call ID without duplication.");
            }
            Assert.IsTrue(requests.Any(request => request.OutputIds.Contains(callId)), "The real result must be delivered in a continuation request.");
        }

        private static bool IsInputType(JsonElement item, string type)
            => item.TryGetProperty("type", out var value) && value.GetString() == type;
    }

    private sealed record RunRequestRecord(int Index, string ToolChoice, string? ForcedToolName,
        bool? ParallelToolCalls, bool ToolAllowsAsync, string[] CallIds, string[] OutputIds);

    private static async Task ObserveAsync(AIRun run, List<StreamingContent> events, CancellationToken cancellationToken)
    {
        await foreach (var item in run.StreamAsync(cancellationToken)) events.Add(item);
    }

    private static async Task AwaitSignalOrFailureAsync(Task signal, AIRun run, TimeSpan timeout)
    {
        var first = await Task.WhenAny(signal, run.Result).WaitAsync(timeout);
        if (first == run.Result)
        {
            await run.Result;
            Assert.Fail("The real run finished before reaching the requested integration checkpoint.");
        }
        await signal;
    }
}
