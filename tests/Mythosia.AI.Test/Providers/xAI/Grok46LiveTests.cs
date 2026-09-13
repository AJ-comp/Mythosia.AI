using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Rag;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.xAI;

[TestClass]
[TestCategory("Live")]
[TestCategory("xAI")]
[TestCategory("Grok46")]
[DoNotParallelize]
public class Grok46LiveTests
{
    private const string Calculation = "A warehouse has 17 cartons with 19 pieces each. It ships 8 cartons, then receives 37 loose pieces. " +
        "How many pieces remain? Reply with only the integer.";

    [TestMethod]
    [DataRow(GrokReasoning.Auto)]
    [DataRow(GrokReasoning.Low)]
    [DataRow(GrokReasoning.Medium)]
    [DataRow(GrokReasoning.High)]
    [DataRow(GrokReasoning.XHigh)]
    public async Task ProviderReasoning_AcceptsEverySupportedEffortAndOmitsAuto(GrokReasoning level)
    {
        using var probe = await Grok46LiveProbe.CreateAsync();
        probe.Service.ReasoningEffort = level;
        var answer = await probe.ExecuteAsync(Calculation, Grok46ExecutionMode.Completion);
        Assert.AreEqual("208", answer.Text.Trim());
        Assert.AreEqual(1, probe.Requests.Count);
        AssertEffort(probe.Requests[0], level == GrokReasoning.Auto ? null : level.ToString().ToLowerInvariant());
        Assert.AreEqual(answer.Text, probe.Service.ActivateChat.Messages.Last().Content);
        probe.AssertTransport(streaming: false);
        Console.WriteLine($"LIVE_GROK46_OK feature=provider-reasoning level={level}");
    }

    [TestMethod]
    [DataRow(ReasoningLevel.Auto, Grok46ExecutionMode.Run)]
    [DataRow(ReasoningLevel.Low, Grok46ExecutionMode.LegacyCallback)]
    [DataRow(ReasoningLevel.Medium, Grok46ExecutionMode.RichStream)]
    [DataRow(ReasoningLevel.High, Grok46ExecutionMode.Run)]
    [DataRow(ReasoningLevel.XHigh, Grok46ExecutionMode.Run)]
    public async Task CommonReasoning_WorksThroughStreamingAndRun(ReasoningLevel level, Grok46ExecutionMode mode)
    {
        using var probe = await Grok46LiveProbe.CreateAsync();
        probe.Service.WithReasoning(level);
        var answer = await probe.ExecuteAsync(Calculation, mode);
        Assert.AreEqual("208", answer.Text.Trim());
        Assert.AreEqual(1, probe.Requests.Count);
        AssertEffort(probe.Requests[0], level == ReasoningLevel.Auto ? null : level.ToString().ToLowerInvariant());
        Assert.AreEqual(answer.Text, probe.Service.ActivateChat.Messages.Last().Content);
        Assert.AreEqual(GrokReasoning.Auto, probe.Service.ReasoningEffort,
            "Common per-request reasoning must not mutate the provider-specific default.");
        probe.AssertTransport(streaming: true);
        Console.WriteLine($"LIVE_GROK46_OK feature=common-reasoning mode={mode} level={level}");
    }

    [TestMethod]
    [DataRow(Grok46ExecutionMode.Completion)]
    [DataRow(Grok46ExecutionMode.Run)]
    public async Task AutomaticClientFunction_PreservesActualCallIdAndReturnsHandlerOnlyData(Grok46ExecutionMode mode)
    {
        using var probe = await Grok46LiveProbe.CreateAsync();
        probe.Service.WithReasoning(ReasoningLevel.Medium);
        var reference = "DISPATCH_" + Guid.NewGuid().ToString("N");
        var executions = 0;
        probe.Service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup_dispatch_reference",
            Description = "Looks up a warehouse's current dispatch reference. The reference is available only from this tool.",
            Parameters = new FunctionParameters
            {
                Properties = new Dictionary<string, ParameterProperty>
                {
                    ["warehouse"] = new() { Type = "string", Description = "The warehouse name." }
                },
                Required = new List<string> { "warehouse" }
            },
            Handler = args =>
            {
                Assert.AreEqual("Seoul", args["warehouse"].ToString());
                Interlocked.Increment(ref executions);
                return Task.FromResult(JsonSerializer.Serialize(new { dispatch_reference = reference }));
            }
        });
        var answer = await probe.ExecuteAsync(
            "Use lookup_dispatch_reference exactly once to obtain the current dispatch reference for the Seoul warehouse. " +
            "Reply with only the exact reference returned by the tool. It is not available from memory.", mode);
        StringAssert.Contains(answer.Text, reference);
        Assert.AreEqual(1, executions);
        Assert.AreEqual(2, probe.Requests.Count);
        Assert.IsFalse(probe.Requests[0].Body.ToJsonString().Contains(reference, StringComparison.Ordinal),
            "The independently generated result must first enter the conversation through the tool handler.");
        var nativeId = probe.Requests[0].NativeCallIds().Single();
        var continuation = probe.Requests[1].Body["messages"]!.AsArray().OfType<JsonObject>().ToArray();
        var replayed = continuation.Single(message => message["tool_calls"] != null)["tool_calls"]!.AsArray().Single()!;
        Assert.AreEqual(nativeId, replayed["id"]?.GetValue<string>());
        Assert.AreEqual("lookup_dispatch_reference", replayed["function"]?["name"]?.GetValue<string>());
        var arguments = JsonNode.Parse(replayed["function"]!["arguments"]!.GetValue<string>())!;
        Assert.AreEqual("Seoul", arguments["warehouse"]?.GetValue<string>());
        var toolResult = continuation.Single(message => message["role"]?.GetValue<string>() == "tool");
        Assert.AreEqual(nativeId, toolResult["tool_call_id"]?.GetValue<string>());
        StringAssert.Contains(toolResult["content"]!.GetValue<string>(), reference);
        var publicCall = probe.Service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null).FunctionCallBatch!.Calls.Single();
        Assert.AreEqual(nativeId, publicCall.Id);
        foreach (var request in probe.Requests) AssertEffort(request, "medium");
        if (mode == Grok46ExecutionMode.Run)
        {
            Assert.AreEqual(1, answer.Events.Count(item => item.Type == StreamingContentType.FunctionCall));
            Assert.AreEqual(1, answer.Events.Count(item => item.Type == StreamingContentType.FunctionResult));
        }
        probe.AssertTransport(mode != Grok46ExecutionMode.Completion);
        Console.WriteLine($"LIVE_GROK46_OK feature=client-function-correlation mode={mode}");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RagQueryRewrite_UsesLowFloorAndRestoresHighForFinalAnswer(bool useRun)
    {
        using var probe = await Grok46LiveProbe.CreateAsync();
        probe.Service.ReasoningEffort = GrokReasoning.High;
        var reference = "DELIVERY_" + Guid.NewGuid().ToString("N");
        var rag = probe.Service.WithRag(builder => builder
            .AddText($"The Seoul warehouse delivery reference is {reference}. This is synthetic test data.", id: "synthetic-delivery")
            .UseLocalEmbedding(64).WithQueryRewriter(1024));
        const string question = "What is the exact delivery reference for the Seoul warehouse? Copy it verbatim.";
        string answer;
        if (!useRun)
            answer = await rag.GetCompletionAsync(question).WaitAsync(TimeSpan.FromMinutes(4));
        else
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var observed = new StringBuilder();
            await using var run = await rag.StartRunAsync(question, chunk => observed.Append(chunk), cancellationToken: timeout.Token);
            var completed = 0;
            await foreach (var item in run.StreamAsync(timeout.Token))
            {
                Assert.AreNotEqual(StreamingContentType.Error, item.Type);
                if (item.Type == StreamingContentType.Completion) completed++;
            }
            answer = (await run.Result).Text;
            Assert.AreEqual(answer, observed.ToString());
            Assert.AreEqual(1, completed);
        }
        StringAssert.Contains(answer, reference);
        Assert.AreEqual(2, probe.Requests.Count);
        AssertEffort(probe.Requests[0], "low");
        AssertEffort(probe.Requests[1], "high");
        Assert.IsFalse(probe.Requests[0].Streaming);
        Assert.AreEqual(useRun, probe.Requests[1].Streaming);
        Assert.AreEqual(GrokReasoning.High, probe.Service.ReasoningEffort);
        Assert.IsFalse(probe.Requests[0].Body.ToJsonString().Contains(reference, StringComparison.Ordinal));
        StringAssert.Contains(probe.Requests[1].Body.ToJsonString(), reference);
        probe.AssertTransport(queryRewriteFirst: true);
        Console.WriteLine($"LIVE_GROK46_OK feature=rag-rewrite-low-floor-and-restore run={useRun}");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TypedJsonMode_ReturnsTheRequestedObjectWithoutRepair(bool streaming)
    {
        using var probe = await Grok46LiveProbe.CreateAsync();
        probe.Service.WithReasoning(ReasoningLevel.Low);
        const string prompt = "Return a JSON object with City equal to Seoul and Packages equal to 17 times 19. " +
            "Use exactly the property names City and Packages, with a string and an integer respectively.";
        DispatchSummary result;
        if (!streaming)
            result = await probe.Service.GetCompletionAsync<DispatchSummary>(prompt).WaitAsync(TimeSpan.FromMinutes(4));
        else
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var typed = probe.Service.BeginStream(prompt).As<DispatchSummary>();
            var observed = new StringBuilder();
            await foreach (var chunk in typed.Stream(timeout.Token)) observed.Append(chunk);
            result = await typed.Result.WaitAsync(timeout.Token);
            var streamed = JsonSerializer.Deserialize<DispatchSummary>(observed.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.IsNotNull(streamed);
            Assert.AreEqual(result.City, streamed.City);
            Assert.AreEqual(result.Packages, streamed.Packages);
        }
        Assert.AreEqual("Seoul", result.City);
        Assert.AreEqual(323, result.Packages);
        Assert.AreEqual(1, probe.Requests.Count, "Typed output must succeed on its first request without a repair call.");
        Assert.AreEqual("json_object", probe.Requests[0].Body["response_format"]?["type"]?.GetValue<string>());
        AssertEffort(probe.Requests[0], "low");
        probe.AssertTransport(streaming);
        Console.WriteLine($"LIVE_GROK46_OK feature=typed-json-mode streaming={streaming}");
    }

    public sealed class DispatchSummary
    {
        public string City { get; set; } = string.Empty;
        public int Packages { get; set; }
    }

    private static void AssertEffort(Grok46LiveProbe.RequestRecord request, string? expected)
    {
        Assert.AreEqual(expected, request.Body["reasoning_effort"]?.GetValue<string>());
        if (expected == null) Assert.IsFalse(request.Body.ContainsKey("reasoning_effort"), "Auto must omit the field entirely.");
    }
}
