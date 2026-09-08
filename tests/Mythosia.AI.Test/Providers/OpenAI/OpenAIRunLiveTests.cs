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
        var result = await run.Result.WaitAsync(TimeSpan.FromMinutes(4));

        StringAssert.Contains(result, token);
        Console.WriteLine("LIVE_RUN_OK model=gpt-6-astra mode=result-only");
    }

    [TestMethod]
    public async Task Astra_SteerDuringText_CallbackAndResultShareContinuation()
    {
        using var client = new HttpClient();
        var service = await CreateServiceAsync(AIModels.OpenAI.Gpt6Astra, client);
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
        var result = await run.Result.WaitAsync(TimeSpan.FromMinutes(3));

        StringAssert.Contains(result, token, "An accepted steering request must produce the actual continuation.");
        Assert.AreEqual(result, observed.ToString(), "The callback and Result must share the same generated text.");
        Console.WriteLine("LIVE_RUN_OK model=gpt-6-astra mode=callback-steering");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Astra_SteerWhileToolPending_PreservesCallAndFinalCompletion(bool nativeAsync)
    {
        using var client = new HttpClient();
        var service = await CreateServiceAsync(AIModels.OpenAI.Gpt6Astra, client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toolToken = "RUN_TOOL_" + Guid.NewGuid().ToString("N");
        var steerToken = "RUN_DIRECTIVE_" + Guid.NewGuid().ToString("N");
        int invocations = 0;
        int finished = 0;
        const string toolName = "read_run_token";
        service.SystemMessage =
            "For this integration check, perform exactly the requested action. Keep reasoning and output minimal. " +
            "Never guess a tool result.";
        service.ForceFunctionName = nativeAsync ? null : toolName;
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

        var result = await run.Result.WaitAsync(TimeSpan.FromMinutes(3));
        await observation.WaitAsync(TimeSpan.FromSeconds(30));
        StringAssert.Contains(result, toolToken);
        StringAssert.Contains(result, steerToken);
        Assert.AreEqual(1, invocations);
        Assert.AreEqual(1, finished);
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
        Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error));
        var call = Assert.ContainsSingle(service.ActivateChat.Messages
            .Where(message => message.FunctionCallBatch != null).SelectMany(message => message.FunctionCallBatch!.Calls));
        Assert.AreEqual(nativeAsync, call.IsAsync, "A synchronous call cannot pass the native async test.");
        var functionResult = Assert.ContainsSingle(events.Where(item => item.Type == StreamingContentType.FunctionResult));
        Assert.AreEqual(call.Id, functionResult.FunctionResult!.Call.Id);
        Console.WriteLine($"LIVE_RUN_OK model=gpt-6-astra mode=tool-steering native_async={nativeAsync} handlers={invocations}");
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
        var result = await run.Result.WaitAsync(TimeSpan.FromMinutes(4));

        StringAssert.Contains(result, token);
        Assert.AreEqual(1, invocations);
        var call = Assert.ContainsSingle(service.ActivateChat.Messages
            .Where(message => message.FunctionCallBatch != null).SelectMany(message => message.FunctionCallBatch!.Calls));
        Assert.IsFalse(call.IsAsync);
        Console.WriteLine("LIVE_RUN_OK model=gpt-5.6-sol mode=unsupported-steering-tool-fallback");
    }

    private static async Task<OpenAIService> CreateServiceAsync(string model, HttpClient client)
    {
        var key = await LiveTestSecrets.GetAsync("momedit-openai-secret");
        var service = new OpenAIService(key, model, client)
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
            if (root.TryGetProperty("response", out var response))
            {
                if (response.TryGetProperty("id", out var id)) responseId = id.GetString();
                if (response.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object &&
                    details.TryGetProperty("reason", out var reasonValue)) reason = reasonValue.GetString();
                if (response.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                    outputs = string.Join(",", output.EnumerateArray().Select(item =>
                        (item.TryGetProperty("type", out var itemType) ? itemType.GetString() : "unknown") + ":" +
                        (item.TryGetProperty("status", out var itemStatus) ? itemStatus.GetString() : "none")));
            }
            Console.WriteLine($"LIVE_RUN_EVENT type={type} response_id={responseId} incomplete_reason={reason} outputs={outputs}");
        };
        return service;
    }

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
