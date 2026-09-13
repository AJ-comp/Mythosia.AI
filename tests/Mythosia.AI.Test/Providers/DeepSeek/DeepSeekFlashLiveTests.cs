using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Rag;
using SkiaSharp;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.DeepSeek;

[TestClass]
[TestCategory("Live")]
[TestCategory("DeepSeek")]
[TestCategory("DeepSeekFlash")]
[DoNotParallelize]
public class DeepSeekFlashLiveTests
{
    private const string Calculation = "A warehouse has 17 cartons with 19 pieces each. It ships 8 cartons, then receives 37 loose pieces. " +
        "How many pieces remain? Reply with only the integer.";

    [TestMethod]
    [DataRow(DeepSeekReasoning.Auto)]
    [DataRow(DeepSeekReasoning.Low)]
    [DataRow(DeepSeekReasoning.High)]
    [DataRow(DeepSeekReasoning.Max)]
    public async Task NativeReasoning_EveryEffortReturnsTheCorrectAnswer(DeepSeekReasoning level)
    {
        using var probe = await DeepSeekFlashLiveProbe.CreateAsync();
        probe.Service.WithDeepSeekReasoning(level);
        if (level == DeepSeekReasoning.Max) probe.Service.MaxTokens = 16384;
        var answer = await probe.ExecuteAsync(Calculation, DeepSeekFlashExecutionMode.Completion);
        Assert.AreEqual("208", answer.Text.Trim());
        Assert.AreEqual(1, probe.Requests.Count);
        AssertThinking(probe.Requests[0], true, level == DeepSeekReasoning.Auto ? null : level.ToString().ToLowerInvariant());
        Assert.AreEqual(answer.Text, probe.Service.ActivateChat.Messages.Last().Content);
        var providerReasoning = probe.Requests[0].Reasoning();
        if (providerReasoning.Length > 0)
            Assert.AreEqual(providerReasoning, probe.Service.ActivateChat.Messages.Last().Metadata!["deepseek_reasoning_content"]);
        probe.AssertTransport(false);
        Console.WriteLine($"LIVE_DEEPSEEK_FLASH_OK feature=native-reasoning level={level}");
    }

    [TestMethod]
    [DataRow(DeepSeekFlashExecutionMode.Completion)]
    [DataRow(DeepSeekFlashExecutionMode.LegacyCallback)]
    public async Task DefaultThinkingOff_WorksThroughExistingCompletionAndCallback(DeepSeekFlashExecutionMode mode)
    {
        using var probe = await DeepSeekFlashLiveProbe.CreateAsync();
        var nonce = "ECHO_" + Guid.NewGuid().ToString("N");
        var answer = await probe.ExecuteAsync("Reply with only this exact identifier, preserving every character: " + nonce, mode);
        Assert.AreEqual(nonce, answer.Text.Trim());
        Assert.AreEqual(1, probe.Requests.Count);
        Assert.AreEqual(probe.Requests[0].AnswerText(), answer.Text,
            "Completion and the legacy callback must deliver the actual provider answer without alteration.");
        AssertThinking(probe.Requests[0], false, null);
        Assert.IsFalse(probe.Service.ThinkingEnabled);
        Assert.AreEqual(DeepSeekReasoning.Auto, probe.Service.ReasoningEffort);
        probe.AssertTransport(mode != DeepSeekFlashExecutionMode.Completion);
        Console.WriteLine($"LIVE_DEEPSEEK_FLASH_OK feature=default-thinking-off mode={mode}");
    }

    [TestMethod]
    public async Task CommonReasoning_RunMapsXHighToHighAndPreservesDefaults()
    {
        using var probe = await DeepSeekFlashLiveProbe.CreateAsync();
        probe.Service.WithReasoning(ReasoningLevel.XHigh);
        var answer = await probe.ExecuteAsync(Calculation, DeepSeekFlashExecutionMode.Run);
        Assert.AreEqual("208", answer.Text.Trim());
        Assert.AreEqual(1, probe.Requests.Count);
        AssertThinking(probe.Requests[0], true, "high");
        Assert.IsFalse(probe.Service.ThinkingEnabled);
        Assert.AreEqual(DeepSeekReasoning.Auto, probe.Service.ReasoningEffort);
        probe.AssertTransport(true);
        Console.WriteLine("LIVE_DEEPSEEK_FLASH_OK feature=common-reasoning-run");
    }

    [TestMethod]
    [DataRow(DeepSeekFlashExecutionMode.Completion)]
    [DataRow(DeepSeekFlashExecutionMode.Run)]
    public async Task DependentTools_ReplayActualIdsAndAllReasoningIntoLaterUserTurn(DeepSeekFlashExecutionMode mode)
    {
        using var probe = await DeepSeekFlashLiveProbe.CreateAsync();
        probe.Service.WithDeepSeekReasoning();
        var ticket = "TICKET_" + Guid.NewGuid().ToString("N");
        var reference = "DISPATCH_" + Guid.NewGuid().ToString("N");
        var executions = new List<string>();
        probe.Service.Functions.Add(new FunctionDefinition
        {
            Name = "issue_dispatch_ticket",
            Description = "Issues the current Seoul warehouse ticket. Call once, then pass its exact ticket to resolve_dispatch_ticket.",
            Handler = _ =>
            {
                executions.Add("issue");
                return Task.FromResult(JsonSerializer.Serialize(new { ticket }));
            }
        });
        probe.Service.Functions.Add(new FunctionDefinition
        {
            Name = "resolve_dispatch_ticket",
            Description = "Returns the dispatch reference for the exact ticket issued by issue_dispatch_ticket. The ticket must come from that tool.",
            Parameters = new FunctionParameters
            {
                Properties = new Dictionary<string, ParameterProperty> { ["ticket"] = new() { Type = "string" } },
                Required = new List<string> { "ticket" }
            },
            Handler = args =>
            {
                Assert.AreEqual(ticket, args["ticket"].ToString());
                CollectionAssert.AreEqual(new[] { "issue" }, executions);
                executions.Add("resolve");
                return Task.FromResult(JsonSerializer.Serialize(new { dispatch_reference = reference }));
            }
        });
        var answer = await probe.ExecuteAsync("Obtain the Seoul warehouse dispatch reference. First call issue_dispatch_ticket once, " +
            "then call resolve_dispatch_ticket once with the exact issued ticket. Reply with only the exact dispatch_reference returned by the second tool.", mode);
        CollectionAssert.AreEqual(new[] { "issue", "resolve" }, executions);
        Assert.AreEqual(3, probe.Requests.Count, "The second tool depends on data available only after the first handler finishes.");
        var finalProviderAnswer = probe.Requests[2].AnswerText();
        Assert.AreEqual(reference, finalProviderAnswer.Trim());
        Assert.IsTrue(probe.Requests[2].Choices().Any(choice => choice["finish_reason"]?.GetValue<string>() == "stop"));
        if (mode == DeepSeekFlashExecutionMode.Completion)
            Assert.AreEqual(finalProviderAnswer, answer.Text);
        else
        {
            Assert.AreEqual(string.Concat(probe.Requests.Select(request => request.AnswerText())), answer.Text);
            StringAssert.Contains(answer.Text, reference);
        }
        Assert.IsFalse(probe.Requests[0].Body.ToJsonString().Contains(ticket, StringComparison.Ordinal));
        Assert.IsFalse(probe.Requests[1].Body.ToJsonString().Contains(reference, StringComparison.Ordinal));
        var followUp = await probe.ExecuteAsync("Repeat only the exact dispatch reference you just obtained. Reuse the previous result; no additional tool calls are needed.", mode);
        Assert.AreEqual(reference, followUp.Text.Trim());
        Assert.AreEqual(4, probe.Requests.Count);
        Assert.AreEqual(probe.Requests[3].AnswerText(), followUp.Text);
        Assert.AreEqual(2, executions.Count);

        var nativeIds = probe.Requests.Take(2).Select(request => request.NativeCallIds().Single()).ToArray();
        Assert.AreNotEqual(nativeIds[0], nativeIds[1]);
        Assert.IsTrue(probe.Requests.Take(2).Any(request => !string.IsNullOrWhiteSpace(request.Reasoning())),
            "At least one actual tool turn must return nonempty reasoning to exercise real reasoning replay.");
        for (var requestIndex = 1; requestIndex < 4; requestIndex++)
        {
            var messages = probe.Requests[requestIndex].Body["messages"]!.AsArray().OfType<JsonObject>().ToArray();
            var assistants = messages.Where(message => message["role"]?.GetValue<string>() == "assistant").ToArray();
            Assert.AreEqual(requestIndex, assistants.Length);
            for (var assistantIndex = 0; assistantIndex < assistants.Length; assistantIndex++)
            {
                var reasoning = probe.Requests[assistantIndex].Reasoning();
                Assert.AreEqual(reasoning, assistants[assistantIndex]["reasoning_content"]?.GetValue<string>() ?? string.Empty,
                    "All actual preceding assistant reasoning, including the final answer, must survive a new user turn.");
                if (assistantIndex < 2)
                    Assert.AreEqual(nativeIds[assistantIndex], assistants[assistantIndex]["tool_calls"]![0]!["id"]?.GetValue<string>());
            }
            var toolResults = messages.Where(message => message["role"]?.GetValue<string>() == "tool").ToArray();
            CollectionAssert.AreEqual(nativeIds.Take(Math.Min(requestIndex, 2)).ToArray(), toolResults.Select(result => result["tool_call_id"]!.GetValue<string>()).ToArray());
        }
        var publicIds = probe.Service.ActivateChat.Messages.Where(message => message.FunctionCallBatch != null)
            .SelectMany(message => message.FunctionCallBatch!.Calls).Select(call => call.Id).ToArray();
        CollectionAssert.AreEqual(nativeIds, publicIds);
        if (mode == DeepSeekFlashExecutionMode.Run)
        {
            Assert.AreEqual(2, answer.Events.Count(item => item.Type == StreamingContentType.FunctionCall));
            Assert.AreEqual(2, answer.Events.Count(item => item.Type == StreamingContentType.FunctionResult));
        }
        foreach (var request in probe.Requests) AssertThinking(request, true, "high");
        probe.AssertTransport(mode != DeepSeekFlashExecutionMode.Completion);
        Console.WriteLine($"LIVE_DEEPSEEK_FLASH_OK feature=dependent-tools-and-later-user-reasoning mode={mode}");
    }

    [TestMethod]
    [DataRow("image/png", "red", false)]
    [DataRow("image/jpeg", "blue", true)]
    public async Task NativeVision_RecognizesSyntheticPngAndJpeg(string mediaType, string color, bool run)
    {
        using var probe = await DeepSeekFlashLiveProbe.CreateAsync();
        using var bitmap = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(color == "red" ? SKColors.Red : SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(mediaType == "image/png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 95);
        var bytes = encoded.ToArray();
        var message = new Message(ActorRole.User, new List<MessageContent>
        {
            new TextContent("Name the dominant color of this image. Reply with only one lowercase English color word."),
            new ImageContent(bytes, mediaType)
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        string answer;
        if (!run) answer = await probe.Service.GetCompletionAsync(message).WaitAsync(timeout.Token);
        else
        {
            var text = new StringBuilder();
            await using var execution = await probe.Service.StartRunAsync(message, chunk => text.Append(chunk), cancellationToken: timeout.Token);
            await foreach (var item in execution.StreamAsync(timeout.Token)) Assert.AreNotEqual(StreamingContentType.Error, item.Type);
            answer = (await execution.Result).Text;
            Assert.AreEqual(answer, text.ToString());
        }
        Assert.AreEqual(color, answer.Trim());
        Assert.AreEqual(1, probe.Requests.Count);
        var content = probe.Requests[0].Body["messages"]!.AsArray().OfType<JsonObject>()
            .Single(item => item["role"]?.GetValue<string>() == "user")["content"]!.AsArray();
        Assert.AreEqual("data:" + mediaType + ";base64," + Convert.ToBase64String(bytes), content.Single(part => part?["type"]?.GetValue<string>() == "image_url")!["image_url"]?["url"]?.GetValue<string>());
        AssertThinking(probe.Requests[0], false, null);
        probe.AssertTransport(run);
        Console.WriteLine($"LIVE_DEEPSEEK_FLASH_OK feature=inline-vision mime={mediaType} run={run}");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TypedJson_ReturnsRequestedObjectWithoutRepair(bool streaming)
    {
        using var probe = await DeepSeekFlashLiveProbe.CreateAsync();
        probe.Service.WithReasoning(ReasoningLevel.Low);
        const string prompt = "Return a JSON object with City equal to Seoul and Packages equal to 17 times 19. " +
            "Use exactly the property names City and Packages, with a string and an integer respectively.";
        DispatchSummary result;
        if (!streaming) result = await probe.Service.GetCompletionAsync<DispatchSummary>(prompt).WaitAsync(TimeSpan.FromMinutes(4));
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
        Assert.AreEqual(1, probe.Requests.Count);
        Assert.AreEqual("json_object", probe.Requests[0].Body["response_format"]?["type"]?.GetValue<string>());
        AssertThinking(probe.Requests[0], true, "low");
        probe.AssertTransport(streaming);
        Console.WriteLine($"LIVE_DEEPSEEK_FLASH_OK feature=typed-json streaming={streaming}");
    }

    [TestMethod]
    public async Task RagRun_DisablesThinkingForRewriteAndRestoresHighForAnswer()
    {
        using var probe = await DeepSeekFlashLiveProbe.CreateAsync();
        probe.Service.WithDeepSeekReasoning();
        var reference = "DELIVERY_" + Guid.NewGuid().ToString("N");
        var rag = probe.Service.WithRag(builder => builder
            .AddText($"The Seoul warehouse delivery reference is {reference}. This is synthetic test data.", id: "synthetic-delivery")
            .UseLocalEmbedding(64).WithQueryRewriter(512));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var observed = new StringBuilder();
        await using var run = await rag.StartRunAsync("What is the exact delivery reference for the Seoul warehouse? Copy it verbatim.", chunk => observed.Append(chunk), cancellationToken: timeout.Token);
        var completed = 0;
        await foreach (var item in run.StreamAsync(timeout.Token))
        {
            Assert.AreNotEqual(StreamingContentType.Error, item.Type);
            if (item.Type == StreamingContentType.Completion) completed++;
        }
        var answer = (await run.Result).Text;
        StringAssert.Contains(answer, reference);
        Assert.AreEqual(answer, observed.ToString());
        Assert.AreEqual(1, completed);
        Assert.AreEqual(2, probe.Requests.Count);
        AssertThinking(probe.Requests[0], false, null);
        AssertThinking(probe.Requests[1], true, "high");
        Assert.IsFalse(probe.Requests[0].Streaming);
        Assert.IsTrue(probe.Requests[1].Streaming);
        Assert.IsTrue(probe.Service.ThinkingEnabled);
        Assert.AreEqual(DeepSeekReasoning.High, probe.Service.ReasoningEffort);
        Assert.IsFalse(probe.Requests[0].Body.ToJsonString().Contains(reference, StringComparison.Ordinal));
        StringAssert.Contains(probe.Requests[1].Body.ToJsonString(), reference);
        probe.AssertTransport(queryRewriteFirst: true);
        Console.WriteLine("LIVE_DEEPSEEK_FLASH_OK feature=rag-rewrite-off-and-restore");
    }

    public sealed class DispatchSummary
    {
        public string City { get; set; } = string.Empty;
        public int Packages { get; set; }
    }

    private static void AssertThinking(DeepSeekFlashLiveProbe.RequestRecord request, bool enabled, string? effort)
    {
        Assert.AreEqual(enabled ? "enabled" : "disabled", request.Body["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual(effort, request.Body["reasoning_effort"]?.GetValue<string>());
        if (effort == null) Assert.IsFalse(request.Body.ContainsKey("reasoning_effort"));
    }
}
