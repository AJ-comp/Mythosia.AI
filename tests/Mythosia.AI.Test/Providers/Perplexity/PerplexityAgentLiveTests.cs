using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Rag;
using SkiaSharp;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Perplexity;

[TestClass]
[TestCategory("Live")]
[TestCategory("Perplexity")]
[TestCategory("PerplexityAgent")]
[DoNotParallelize]
public class PerplexityAgentLiveTests
{
    [TestMethod]
    [DataRow(PerplexityAgentExecutionMode.Completion)]
    [DataRow(PerplexityAgentExecutionMode.Callback)]
    [DataRow(PerplexityAgentExecutionMode.RichStream)]
    [DataRow(PerplexityAgentExecutionMode.Run)]
    public async Task DefaultSonar_AllPublicExecutionPathsPreserveProviderText(PerplexityAgentExecutionMode mode)
    {
        using var probe = await PerplexityAgentLiveProbe.CreateAsync();
        var expected = "ECHO_" + Guid.NewGuid().ToString("N");
        var answer = await probe.ExecuteAsync("Reply with only this exact identifier: " + expected, mode);
        Assert.AreEqual(expected, answer.Text.Trim());
        Assert.HasCount(1, probe.Requests);
        Assert.AreEqual("perplexity/sonar", probe.Requests[0].Body["model"]!.GetValue<string>());
        Assert.IsTrue(probe.Requests[0].Body["tools"]!.AsArray().Any(tool => tool!["type"]!.GetValue<string>() == "web_search"));
        Assert.AreEqual(answer.Text, probe.Service.ActivateChat.Messages.Last().Content);
    }

    [TestMethod]
    [DataRow(PerplexityPreset.Fast)]
    [DataRow(PerplexityPreset.Low)]
    [DataRow(PerplexityPreset.Medium)]
    [DataRow(PerplexityPreset.High)]
    [DataRow(PerplexityPreset.XHigh)]
    [DataRow(PerplexityPreset.WideResearch)]
    public async Task EveryPreset_IsAcceptedWithoutOverridingItsModel(PerplexityPreset preset)
    {
        using var probe = await PerplexityAgentLiveProbe.CreateAsync();
        probe.Service.UsePreset(preset);
        probe.Service.AgentOptions.MaxSteps = 3;
        var expected = "PRESET_" + Guid.NewGuid().ToString("N");
        var answer = await probe.ExecuteAsync("This is an exact-copy task requiring no research. Reply with only this identifier: " + expected, PerplexityAgentExecutionMode.Run);
        Assert.AreEqual(expected, answer.Text.Trim());
        Assert.HasCount(1, probe.Requests);
        var request = probe.Requests[0].Body;
        Assert.AreEqual(preset == PerplexityPreset.WideResearch ? "wide-research" : preset.ToString().ToLowerInvariant(), request["preset"]!.GetValue<string>());
        Assert.IsFalse(request.ContainsKey("model"));
        Assert.IsFalse(request.ContainsKey("tools"));
        Assert.AreEqual("perplexity/sonar", probe.Service.Model);
    }

    [TestMethod]
    [DataRow(ReasoningLevel.Auto)]
    [DataRow(ReasoningLevel.Minimal)]
    [DataRow(ReasoningLevel.Low)]
    [DataRow(ReasoningLevel.Medium)]
    [DataRow(ReasoningLevel.High)]
    [DataRow(ReasoningLevel.XHigh)]
    [DataRow(ReasoningLevel.Max)]
    public async Task AgentReasoning_AllDocumentedEffortsUseTheRequestedWireValue(ReasoningLevel level)
    {
        using var probe = await PerplexityAgentLiveProbe.CreateAsync();
        probe.Service.ChangeModel("openai/gpt-5.6-luna");
        probe.Service.AgentOptions.DisableWebSearch = true;
        probe.Service.WithReasoning(level);
        var answer = await probe.ExecuteAsync("What is 19 plus 23? Reply with only the integer.", PerplexityAgentExecutionMode.RichStream);
        Assert.AreEqual("42", answer.Text.Trim());
        Assert.HasCount(1, probe.Requests);
        Assert.AreEqual(level == ReasoningLevel.Auto ? null : level.ToString().ToLowerInvariant(), probe.Requests[0].Body["reasoning"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("openai/gpt-5.6-luna", probe.Requests[0].Body["model"]!.GetValue<string>());
        Assert.AreEqual(ReasoningLevel.Auto, probe.Service.AgentOptions.ReasoningEffort);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeJsonSchema_WorksWithTypedCompletionAndStreaming(bool streaming)
    {
        using var probe = await PerplexityAgentLiveProbe.CreateAsync();
        const string prompt = "Return an object with City equal to Seoul and Packages equal to 17 times 19. Use exactly City and Packages.";
        DispatchSummary result;
        if (!streaming) result = await probe.Service.GetCompletionAsync<DispatchSummary>(prompt).WaitAsync(TimeSpan.FromMinutes(8));
        else
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            var typed = probe.Service.BeginStream(prompt).As<DispatchSummary>();
            var text = new StringBuilder();
            await foreach (var part in typed.Stream(timeout.Token)) text.Append(part);
            result = await typed.Result.WaitAsync(timeout.Token);
            var observed = JsonSerializer.Deserialize<DispatchSummary>(text.ToString());
            Assert.IsNotNull(observed);
            Assert.AreEqual(result.City, observed.City);
            Assert.AreEqual(result.Packages, observed.Packages);
            Assert.AreEqual(probe.Requests.Single().AnswerText(), text.ToString());
        }
        Assert.AreEqual("Seoul", result.City);
        Assert.AreEqual(323, result.Packages);
        Assert.HasCount(1, probe.Requests);
        Assert.AreEqual("json_schema", probe.Requests[0].Body["response_format"]!["type"]!.GetValue<string>());
        Assert.IsNotNull(probe.Requests[0].Body["response_format"]!["json_schema"]!["schema"]);
        probe.AssertTransport();
    }

    [TestMethod]
    public async Task WebSearch_RespectsDomainAllowlistAndReturnsPublicCitations()
    {
        using var probe = await PerplexityAgentLiveProbe.CreateAsync();
        probe.Service.WithWebSearch(new WebSearchOptions { AllowedDomains = ["docs.perplexity.ai"] });
        var answer = await probe.ExecuteAsync("Search the official Perplexity API documentation and state the HTTP path used to create an Agent API response. Cite the documentation.", PerplexityAgentExecutionMode.Run);
        StringAssert.Contains(answer.Text, "/v1/agent");
        Assert.HasCount(1, probe.Requests);
        Assert.IsNotEmpty(probe.Service.LastCitations);
        Assert.IsTrue(answer.Events.Any(item => item.Type == StreamingContentType.Citation));
        foreach (var citation in probe.Service.LastCitations.Where(citation => citation.Url != null))
            Assert.AreEqual("docs.perplexity.ai", new Uri(citation.Url!).Host);
        Assert.AreEqual("docs.perplexity.ai", probe.Requests[0].Body["tools"]![0]!["filters"]!["search_domain_filter"]![0]!.GetValue<string>());
        var followUp = await probe.ExecuteAsync("Without searching again, repeat only the HTTP path for creating an Agent response from your previous answer.", PerplexityAgentExecutionMode.Completion);
        StringAssert.Contains(followUp.Text, "/v1/agent");
        Assert.HasCount(2, probe.Requests);
        var replay = probe.Requests[1].Body["input"]!.AsArray().OfType<JsonObject>().ToArray();
        Assert.IsTrue(replay.All(item => item["type"]?.GetValue<string>() is "message" or "function_call" or "function_call_output"),
            "The Agent input union excludes output-only hosted search traces.");
        Assert.IsTrue(replay.Any(item => item["role"]?.GetValue<string>() == "assistant" &&
            item.ToJsonString().Contains("/v1/agent", StringComparison.Ordinal)),
            "The previous public answer must remain available to a follow-up question.");
    }

    [TestMethod]
    [DataRow(PerplexityAgentExecutionMode.Completion)]
    [DataRow(PerplexityAgentExecutionMode.Run)]
    public async Task ClientFunction_ResultAndNativeIdsSurviveLaterUserTurn(PerplexityAgentExecutionMode mode)
    {
        using var probe = await PerplexityAgentLiveProbe.CreateAsync();
        probe.Service.ChangeModel("openai/gpt-5.6-luna");
        probe.Service.AgentOptions.DisableWebSearch = true;
        var reference = "DISPATCH_" + Guid.NewGuid().ToString("N");
        var executions = 0;
        probe.Service.Functions.Add(new FunctionDefinition
        {
            Name = "get_dispatch_reference",
            Description = "Returns the current dispatch reference. The reference is available only through this function.",
            Handler = _ => { executions++; return Task.FromResult(JsonSerializer.Serialize(new { reference })); }
        });
        var answer = await probe.ExecuteAsync("Call get_dispatch_reference exactly once. Return only the exact reference supplied by that function.", mode);
        Assert.AreEqual(1, executions);
        Assert.HasCount(2, probe.Requests);
        StringAssert.Contains(answer.Text, reference);
        Assert.AreEqual(reference, probe.Requests[1].AnswerText().Trim());
        var native = probe.Requests[0].FunctionCalls().Single();
        var callId = native["call_id"]!.GetValue<string>();
        var input = probe.Requests[1].Body["input"]!.AsArray();
        Assert.AreEqual(callId, input.Single(item => item?["type"]?.GetValue<string>() == "function_call")!["call_id"]!.GetValue<string>());
        Assert.AreEqual(callId, input.Single(item => item?["type"]?.GetValue<string>() == "function_call_output")!["call_id"]!.GetValue<string>());
        if (native["thought_signature"] != null)
            Assert.IsTrue(JsonNode.DeepEquals(native["thought_signature"], input.Single(item => item?["type"]?.GetValue<string>() == "function_call")!["thought_signature"]));
        var followUp = await probe.ExecuteAsync("Repeat only the same dispatch reference from the previous answer. Do not call the function again.", mode);
        Assert.AreEqual(reference, followUp.Text.Trim());
        Assert.AreEqual(1, executions);
        Assert.HasCount(3, probe.Requests);
        Assert.IsTrue(probe.Requests[2].Body["input"]!.AsArray().Any(item => item?["type"]?.GetValue<string>() == "function_call_output"));
    }

    [TestMethod]
    public async Task PngImageInput_ReachesTheVisionModel()
    {
        using var probe = await PerplexityAgentLiveProbe.CreateAsync();
        probe.Service.ChangeModel("openai/gpt-5.6-luna");
        probe.Service.AgentOptions.DisableWebSearch = true;
        using var bitmap = new SKBitmap(96, 96);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = encoded.ToArray();
        var message = new Message(ActorRole.User, new List<MessageContent>
        {
            new TextContent("Name this image's dominant color. Reply with only one lowercase English color word."),
            new ImageContent(bytes, "image/png")
        });
        var answer = await probe.Service.GetCompletionAsync(message).WaitAsync(TimeSpan.FromMinutes(8));
        Assert.AreEqual("blue", answer.Trim());
        Assert.HasCount(1, probe.Requests);
        var part = probe.Requests[0].Body["input"]![0]!["content"]!.AsArray().Single(item => item!["type"]!.GetValue<string>() == "input_image")!;
        Assert.AreEqual("data:image/png;base64," + Convert.ToBase64String(bytes), part["image_url"]!.GetValue<string>());
        Assert.AreEqual(probe.Requests[0].AnswerText(), answer);
        probe.AssertTransport();
    }

    [TestMethod]
    public async Task RagRewrite_SuppressesHostedConfigurationAndRestoresItForTheAnswer()
    {
        using var probe = await PerplexityAgentLiveProbe.CreateAsync();
        probe.Service.UsePreset(PerplexityPreset.Low);
        var reference = "DELIVERY_" + Guid.NewGuid().ToString("N");
        var rag = probe.Service.WithRag(builder => builder
            .AddText($"The Seoul warehouse delivery reference is {reference}. This is synthetic test data.", id: "delivery")
            .UseLocalEmbedding(64).WithQueryRewriter(512));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var observed = new StringBuilder();
        await using var run = await rag.StartRunAsync("Search the indexed document for the exact delivery reference for the Seoul warehouse and copy it verbatim.",
            part => observed.Append(part), cancellationToken: timeout.Token);
        await foreach (var item in run.StreamAsync(timeout.Token)) Assert.AreNotEqual(StreamingContentType.Error, item.Type);
        var answer = (await run.Result).Text;
        var rewriteText = probe.Requests.FirstOrDefault()?.AnswerText();
        Console.WriteLine("LIVE_PERPLEXITY_AGENT_RAG_DIAGNOSTIC " + JsonSerializer.Serialize(new
        {
            requestCount = probe.Requests.Count,
            rewriteText,
            rewriteNeedsSearch = rewriteText == null ? (bool?)null : !rewriteText.Contains("[PASS]", StringComparison.OrdinalIgnoreCase),
            finalRequestContainsReference = probe.Requests.LastOrDefault()?.Body.ToJsonString().Contains(reference, StringComparison.Ordinal) ?? false,
            answerContainsReference = answer.Contains(reference, StringComparison.Ordinal)
        }));
        StringAssert.Contains(answer, reference);
        Assert.AreEqual(answer, observed.ToString());
        Assert.HasCount(2, probe.Requests);
        var rewrite = probe.Requests[0].Body;
        Assert.IsFalse(rewrite.ContainsKey("preset"));
        Assert.AreEqual("perplexity/sonar", rewrite["model"]!.GetValue<string>());
        Assert.HasCount(0, rewrite["tools"]!.AsArray());
        Assert.IsFalse(rewrite.ContainsKey("reasoning"));
        Assert.AreEqual(512, rewrite["max_output_tokens"]!.GetValue<int>());
        Assert.IsFalse(rewrite.ToJsonString().Contains(reference, StringComparison.Ordinal));
        Assert.AreEqual("low", probe.Requests[1].Body["preset"]!.GetValue<string>());
        StringAssert.Contains(probe.Requests[1].Body.ToJsonString(), reference);
        Assert.AreEqual(PerplexityPreset.Low, probe.Service.AgentOptions.Preset);
        Assert.AreEqual("perplexity/sonar", probe.Service.Model);
        Assert.AreEqual(probe.Requests[1].AnswerText(), answer);
        probe.AssertTransport();
    }

    public sealed class DispatchSummary
    {
        public string City { get; set; } = string.Empty;
        public int Packages { get; set; }
    }
}
