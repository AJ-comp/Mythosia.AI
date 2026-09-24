using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Mythosia.AI.Tests.OpenAI;

[TestClass]
[TestCategory("Live")]
[TestCategory("OpenAI")]
[TestCategory("FunctionCalling")]
[TestCategory("AsyncToolCalling")]
[DoNotParallelize]
public class OpenAIAsyncToolCallingLiveTests
{
    private const string ToolName = "read_live_verification_token";
    private const string Prompt =
        "Call read_live_verification_token exactly once. This lookup permits asynchronous execution. " +
        "While its result is pending, give one brief independent tip for packing a bag; " +
        "do not call the lookup again. End that response without waiting: the application sends the result in a later request. Once the actual tool result arrives, " +
        "include its verification_token verbatim in your final answer. Never invent the token.";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Gpt6Astra_AsyncTool_ContinuesBeforeResultAndDeliversOriginalCall(bool streaming)
    {
        await VerifyAsync(AIModels.OpenAI.Gpt6Astra, allowAsync: true, expectNativeAsync: true, streaming);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra, false, false)]
    [DataRow(AIModels.OpenAI.Gpt6Astra, false, true)]
    [DataRow(AIModels.OpenAI.Gpt5_6Sol, true, false)]
    [DataRow(AIModels.OpenAI.Gpt5_6Sol, true, true)]
    public async Task DisabledOrUnsupportedAsyncTool_WaitsForResult(string model, bool allowAsync, bool streaming)
    {
        await VerifyAsync(model, allowAsync, expectNativeAsync: false, streaming);
    }

    internal static async Task VerifyAsync(string model, bool allowAsync, bool expectNativeAsync, bool streaming)
    {
        // Use the same credential source as the existing provider live contracts. Do not log keys.
        var apiKey = await LiveTestSecrets.GetAsync("momedit-openai-secret");
        var probe = new ToolProbe(expectNativeAsync);
        using var observer = new RequestObserver(probe);
        using var client = new HttpClient(observer) { Timeout = TimeSpan.FromMinutes(5) };
        var service = new ObservedOpenAIService(apiKey, model, client, probe, observer)
        {
            MaxTokens = 4096,
            ForceFunctionName = ToolName,
            Gpt6ReasoningEffort = Gpt6Reasoning.Low,
            Gpt6ReasoningSummary = null,
            Gpt6Verbosity = Verbosity.Low,
            Gpt5_6ReasoningEffort = Gpt5_6Reasoning.None,
            Gpt5_6ReasoningSummary = null,
            DefaultPolicy = new FunctionCallingPolicy
            {
                MaxRounds = 5,
                TimeoutSeconds = 240,
                ExecutionMode = FunctionExecutionMode.Sequential,
                MaxConcurrency = 2
            }
        };
        service.ActivateChat.SystemMessage =
            "You are running a tool integration check. Use the requested tool exactly once. " +
            "When a tool call is asynchronous, continue the independent part without waiting. " +
            "Do not guess a tool result. Keep each response brief.";
        var definition = new FunctionDefinition
        {
            Name = ToolName,
            Description = "Read an unpredictable verification token. Call exactly once; no arguments. " +
                          "This lookup may take time. Its result is required for the final answer.",
            AllowAsync = allowAsync,
            Handler = probe.RunAsync
        };
        service.Functions.Add(definition);

        var events = new List<StreamingContent>();
        Task<string>? operation = null;
        string answer;
        try
        {
            operation = streaming
                ? ReadStreamAsync(service, probe, events)
                : service.GetCompletionAsync(Prompt);
            answer = await operation.WaitAsync(TimeSpan.FromMinutes(5));
        }
        finally
        {
            // The library drains started handlers on failure. Always unblock our test-owned job.
            probe.Release();
            if (operation != null)
            {
                try { await operation.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch { /* Preserve the original provider failure or assertion. */ }
            }
            WriteDiagnostics(service, observer, probe, events, model, streaming);
        }

        Assert.IsFalse(probe.GateTimedOut, "The model never completed a continuation while the tool was pending.");
        Assert.AreEqual(allowAsync, definition.AllowAsync, "Capability checks must preserve caller permission.");
        Assert.AreEqual(1, probe.InvocationCount, "The real model must call the tool exactly once.");

        var callBatches = service.ActivateChat.Messages.Where(message => message.FunctionCallBatch != null)
            .Select(message => message.FunctionCallBatch!).ToArray();
        var callBatch = Assert.ContainsSingle(callBatches);
        var call = Assert.ContainsSingle(callBatch.Calls);
        Assert.AreEqual(ToolName, call.Name);
        Assert.AreEqual(expectNativeAsync, call.IsAsync, "A synchronous model response cannot pass the native async contract.");
        var resultBatches = service.ActivateChat.Messages.Where(message => message.FunctionCallResultBatch != null)
            .Select(message => message.FunctionCallResultBatch!).ToArray();
        var resultBatch = Assert.ContainsSingle(resultBatches);
        var result = Assert.ContainsSingle(resultBatch.Results);
        Assert.AreEqual(callBatch.Id, resultBatch.FunctionCallBatchId);
        Assert.AreEqual(call.Id, result.Call.Id);
        Assert.IsFalse(result.IsError, result.Content);
        StringAssert.Contains(result.Content, probe.Token);
        StringAssert.Contains(answer, probe.Token, "The secret test token occurs only in the tool result, never in the prompt.");

        var requests = observer.Requests.ToArray();
        Assert.IsTrue(requests.Length >= (expectNativeAsync ? 3 : 2));
        Assert.IsTrue(requests.All(request => request.Success), "Every observed real API request must succeed.");
        Assert.AreEqual("function", requests[0].ToolChoice, "The fixture must force the initial verification call.");
        Assert.AreEqual(ToolName, requests[0].ForcedToolName);
        Assert.IsTrue(requests.Skip(1).All(request => request.ToolChoice == "none" && request.ForcedToolName == null),
            "Continuation requests must prohibit new calls while preserving the original call and its result.");
        foreach (var request in requests)
        {
            Assert.AreEqual(model, request.Model);
            Assert.AreEqual(streaming, request.Streaming);
            Assert.AreEqual(expectNativeAsync, request.ToolHasAsyncProperty,
                "Disabled and unsupported tools must omit the async field entirely.");
            Assert.AreEqual(expectNativeAsync, request.ToolAllowsAsync);
            Assert.AreEqual("api.openai.com", request.Host);
            Assert.AreEqual("/v1/responses", request.Path);
            Assert.IsTrue(request.OutputIds.Count(id => id == call.Id) <= 1,
                "Replayed requests must not duplicate the original call's result.");
        }

        var continuation = requests[1];
        Assert.IsTrue(continuation.CallIds.Contains(call.Id));
        if (expectNativeAsync)
        {
            Assert.IsTrue(continuation.PendingAtSend, "The actual second request must be sent before the handler finishes.");
            Assert.IsTrue(continuation.AsyncCallIds.Contains(call.Id));
            Assert.IsFalse(continuation.OutputIds.Contains(call.Id));
            Assert.IsTrue(probe.ValidatedContinuationWhilePending,
                "The real server must complete a response with the original tool result still withheld.");
        }
        else
        {
            Assert.IsFalse(continuation.PendingAtSend);
            Assert.IsTrue(continuation.OutputIds.Contains(call.Id));
        }
        Assert.AreEqual(1, requests[^1].OutputIds.Count(id => id == call.Id));

        if (streaming)
        {
            Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error),
                string.Join(" | ", events.Where(item => item.Type == StreamingContentType.Error).Select(item => item.Content)));
            Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
            var callEvent = Assert.ContainsSingle(events.Where(item => item.Type == StreamingContentType.FunctionCall));
            var resultEvent = Assert.ContainsSingle(events.Where(item => item.Type == StreamingContentType.FunctionResult));
            Assert.AreEqual(call.Id, callEvent.FunctionCall!.Id);
            Assert.AreEqual(call.Id, resultEvent.FunctionResult!.Call.Id);
            Assert.AreEqual(callBatch.Id, callEvent.FunctionCallBatchId);
            Assert.AreEqual(callBatch.Id, resultEvent.FunctionCallBatchId);
        }

        Console.WriteLine($"LIVE_ASYNC_OK model={model} streaming={streaming} native_async={expectNativeAsync} " +
                          $"requests={requests.Length} handlers={probe.InvocationCount} results={resultBatch.Results.Count} " +
                          $"validated_pending_continuation={probe.ValidatedContinuationWhilePending}");
        foreach (var request in requests)
            Console.WriteLine($"  status={request.StatusCode} request_id={request.RequestId} " +
                              $"pending_at_send={request.PendingAtSend} calls={request.CallIds.Length} outputs={request.OutputIds.Length}");
    }

    private static void WriteDiagnostics(OpenAIService service, RequestObserver observer, ToolProbe probe,
        IReadOnlyList<StreamingContent> events, string model, bool streaming)
    {
        // Log identifiers and protocol decisions only: never request headers, prompts, tool arguments, or results.
        Console.WriteLine($"LIVE_ASYNC_DIAGNOSTICS model={model} streaming={streaming} " +
                          $"handlers={probe.InvocationCount} gate_timed_out={probe.GateTimedOut} " +
                          $"validated_pending_continuation={probe.ValidatedContinuationWhilePending}");
        var requestIndex = 0;
        foreach (var request in observer.Requests)
            Console.WriteLine("LIVE_ASYNC_REQUEST " + JsonSerializer.Serialize(new
            {
                index = requestIndex++, request.StatusCode, request.RequestId, request.PendingAtSend,
                request.ToolChoice, request.ForcedToolName, request.CallIds, request.AsyncCallIds, request.OutputIds,
                request.ResponseStatus, request.IncompleteReason, request.ResponseOutputTypes, request.ResponseCallIds,
                request.ResponseOutputTokens
            }));
        foreach (var message in service.ActivateChat.Messages)
        {
            if (message.FunctionCallBatch is { } calls)
                Console.WriteLine("LIVE_ASYNC_HISTORY_CALLS " + JsonSerializer.Serialize(new
                {
                    batchId = calls.Id,
                    calls = calls.Calls.Select(call => new { call.Id, call.Name, call.IsAsync }).ToArray()
                }));
            if (message.FunctionCallResultBatch is { } results)
                Console.WriteLine("LIVE_ASYNC_HISTORY_RESULTS " + JsonSerializer.Serialize(new
                {
                    batchId = results.FunctionCallBatchId,
                    calls = results.Results.Select(result => new { callId = result.Call.Id, result.IsError }).ToArray()
                }));
        }
        foreach (var item in events.Where(item => item.FunctionCall != null || item.FunctionResult != null))
            Console.WriteLine("LIVE_ASYNC_EVENT " + JsonSerializer.Serialize(new
            {
                type = item.Type.ToString(), item.RoundIndex, item.FunctionCallBatchId,
                callId = item.FunctionCall?.Id ?? item.FunctionResult?.Call.Id,
                isAsync = item.FunctionCall?.IsAsync ?? item.FunctionResult?.Call.IsAsync
            }));
    }

    private static async Task<string> ReadStreamAsync(
        OpenAIService service, ToolProbe probe, List<StreamingContent> events)
    {
        await foreach (var item in service.StreamAsync(Prompt, StreamOptions.WithFunctions))
        {
            Assert.IsFalse(probe.Pending && (item.Type == StreamingContentType.Completion || item.IsFinalRound),
                "A stream cannot signal final completion while its real tool handler remains pending.");
            events.Add(item);
        }
        return string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content));
    }

    private sealed class ToolProbe(bool holdForContinuation)
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;
        private int _completed;
        public string Token { get; } = "LIVE_TOKEN_" + Guid.NewGuid().ToString("N");
        public int InvocationCount => Volatile.Read(ref _started);
        public bool Completed => Volatile.Read(ref _completed) > 0;
        public bool Pending => InvocationCount > Volatile.Read(ref _completed);
        public bool GateTimedOut { get; private set; }
        public bool ValidatedContinuationWhilePending { get; private set; }

        public async Task<string> RunAsync(Dictionary<string, object> arguments)
        {
            Interlocked.Increment(ref _started);
            try
            {
                if (holdForContinuation)
                {
                    try { await _release.Task.WaitAsync(TimeSpan.FromSeconds(120)); }
                    catch (TimeoutException) { GateTimedOut = true; }
                }
                else
                {
                    // Make waiting observable: a broken fallback would send the next request
                    // during this interval instead of including this handler's completed result.
                    await Task.Delay(300);
                }
                return JsonSerializer.Serialize(new { verification_token = Token });
            }
            finally
            {
                Interlocked.Increment(ref _completed);
            }
        }

        public void OnValidatedContinuation()
        {
            if (holdForContinuation && !_release.Task.IsCompleted)
            {
                ValidatedContinuationWhilePending = Pending;
                Release();
            }
        }

        public void Release() => _release.TrySetResult();
    }

    // Fix the fixture's single-call policy in the active request settings:
    // the first request forces the lookup, and later requests forbid new model calls. Registered
    // tools, real responses, pending handlers, and result delivery remain on the production path.
    // The observer releases the test-owned handler only after the production parser validates
    // a real continuation from OpenAI.
    private sealed class ObservedOpenAIService(
        string apiKey, string model, HttpClient client, ToolProbe probe, RequestObserver observer)
        : OpenAIService(apiKey, model, client)
    {
        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            if (observer.Requests.IsEmpty)
            {
                var request = base.CreateFunctionMessageRequest();
                // A forced tool still permits multiple calls when parallel_tool_calls=true.
                // Bound this integration fixture to one real call; do not mock its response.
                var body = System.Text.Json.Nodes.JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!;
                body["parallel_tool_calls"] = false;
                request.Content.Dispose();
                request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
                return request;
            }

            var previousMode = RequestFunctionCallMode;
            try
            {
                SetExecutionSetting(nameof(FunctionCallMode), FunctionCallMode.None);
                return base.CreateFunctionMessageRequest();
            }
            finally
            {
                SetExecutionSetting(nameof(FunctionCallMode), previousMode);
            }
        }

        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
        {
            var parsed = base.ExtractFunctionCalls(response);
            if (HasPendingAsyncFunctions && observer.Requests.Count >= 2)
                probe.OnValidatedContinuation();
            return parsed;
        }

        protected override StreamingContent? ValidateStreamTermination(
            bool doneMarkerReceived, bool completionEventReceived, StreamDiagnostics diagnostics)
        {
            var error = base.ValidateStreamTermination(doneMarkerReceived, completionEventReceived, diagnostics);
            if (error == null && completionEventReceived && HasPendingAsyncFunctions && observer.Requests.Count >= 2)
                probe.OnValidatedContinuation();
            return error;
        }
    }

    private sealed class RequestObserver(ToolProbe probe) : DelegatingHandler(new HttpClientHandler())
    {
        public ConcurrentQueue<RequestRecord> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Observe only this synthetic test's payload; never capture Authorization headers.
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var input = root.GetProperty("input").EnumerateArray().ToArray();
            var calls = input.Where(item => HasType(item, "function_call")).ToArray();
            var tool = root.GetProperty("tools").EnumerateArray().Single(item => item.GetProperty("name").GetString() == ToolName);
            var record = new RequestRecord
            {
                Host = request.RequestUri!.Host,
                Path = request.RequestUri.AbsolutePath,
                Model = root.GetProperty("model").GetString()!,
                Streaming = root.TryGetProperty("stream", out var stream) && stream.ValueKind == JsonValueKind.True,
                ToolHasAsyncProperty = tool.TryGetProperty("async", out _),
                ToolAllowsAsync = tool.TryGetProperty("async", out var asyncFlag) && asyncFlag.ValueKind == JsonValueKind.True,
                ToolChoice = root.TryGetProperty("tool_choice", out var choice)
                    ? choice.ValueKind == JsonValueKind.String ? choice.GetString()!
                        : choice.TryGetProperty("type", out var choiceType) ? choiceType.GetString()! : "<object>"
                    : "<omitted>",
                ForcedToolName = choice.ValueKind == JsonValueKind.Object && choice.TryGetProperty("name", out var choiceName)
                    ? choiceName.GetString() : null,
                // A deferred job is already pending when its call is replayed, even if the
                // thread pool has not started the handler's synchronous prefix yet.
                PendingAtSend = calls.Length > 0 && !probe.Completed,
                CallIds = calls.Select(item => item.GetProperty("call_id").GetString()!).ToArray(),
                AsyncCallIds = calls.Where(item => item.TryGetProperty("async", out var flag) && flag.ValueKind == JsonValueKind.True)
                    .Select(item => item.GetProperty("call_id").GetString()!).ToArray(),
                OutputIds = input.Where(item => HasType(item, "function_call_output"))
                    .Select(item => item.GetProperty("call_id").GetString()!).ToArray()
            };
            Requests.Enqueue(record);
            var response = await base.SendAsync(request, cancellationToken);
            record.Success = response.IsSuccessStatusCode;
            record.StatusCode = (int)response.StatusCode;
            record.RequestId = response.Headers.TryGetValues("x-request-id", out var requestIds)
                ? requestIds.FirstOrDefault() ?? "<unavailable>" : "<unavailable>";
            if (!record.Streaming && response.IsSuccessStatusCode)
            {
                try
                {
                    using var responseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    var responseRoot = responseDocument.RootElement;
                    record.ResponseStatus = responseRoot.TryGetProperty("status", out var status) ? status.GetString() : null;
                    record.IncompleteReason = responseRoot.TryGetProperty("incomplete_details", out var details) &&
                        details.ValueKind == JsonValueKind.Object && details.TryGetProperty("reason", out var reason)
                        ? reason.GetString() : null;
                    record.ResponseOutputTokens = responseRoot.TryGetProperty("usage", out var usage) &&
                        usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("output_tokens", out var outputTokens) &&
                        outputTokens.TryGetInt32(out var tokenCount) ? tokenCount : null;
                    if (responseRoot.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                    {
                        var items = output.EnumerateArray().ToArray();
                        record.ResponseOutputTypes = items.Select(item => item.TryGetProperty("type", out var type)
                            ? type.GetString() ?? "<null>" : "<omitted>").ToArray();
                        record.ResponseCallIds = items.Where(item => HasType(item, "function_call"))
                            .Select(item => item.GetProperty("call_id").GetString()!).ToArray();
                    }
                }
                catch (JsonException)
                {
                    // Leave malformed-response handling to the production parser.
                    record.ResponseStatus = "<invalid-json>";
                }
            }
            return response;
        }

        private static bool HasType(JsonElement element, string type) =>
            element.TryGetProperty("type", out var value) && value.GetString() == type;
    }

    private sealed class RequestRecord
    {
        public required string Host { get; init; }
        public required string Path { get; init; }
        public required string Model { get; init; }
        public bool Streaming { get; init; }
        public bool ToolHasAsyncProperty { get; init; }
        public bool ToolAllowsAsync { get; init; }
        public bool PendingAtSend { get; init; }
        public required string ToolChoice { get; init; }
        public string? ForcedToolName { get; init; }
        public string? ResponseStatus { get; set; }
        public string? IncompleteReason { get; set; }
        public string[] ResponseOutputTypes { get; set; } = [];
        public string[] ResponseCallIds { get; set; } = [];
        public int? ResponseOutputTokens { get; set; }
        public required string[] CallIds { get; init; }
        public required string[] AsyncCallIds { get; init; }
        public required string[] OutputIds { get; init; }
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string RequestId { get; set; } = "<unavailable>";
    }
}
