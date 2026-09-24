using Mythosia.AI.Builders;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.xAI;

[TestClass]
[TestCategory("Unit")]
public class XAIGrok47ContractTests
{
    [TestMethod]
    public void ModelAddition_PreservesDefaultsAndExistingAliases()
    {
        Assert.AreEqual("grok-4.7", typeof(AIModels.xAI).GetField(nameof(AIModels.xAI.Grok4_7))!.GetRawConstantValue());
        using var client = new HttpClient(new CaptureHandler());
        var service = new XAIService("offline-key", client);
        Assert.AreEqual(AIModels.xAI.Grok4_5, service.Model);
        Assert.AreEqual(GrokReasoning.Auto, service.ReasoningEffort);
        Assert.AreEqual("grok-4.5-latest", typeof(AIModels.xAI).GetField(nameof(AIModels.xAI.Grok4_5Latest))!.GetRawConstantValue());
        Assert.AreEqual("grok-build-latest", typeof(AIModels.xAI).GetField(nameof(AIModels.xAI.GrokBuildLatest))!.GetRawConstantValue());
    }

    [TestMethod]
    public void Capabilities_DescribeImplementedFeaturesAndTheSharedContextCeilingWithoutHttp()
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        var caps = service.CreateRequest("inspect").WithFunctions(Lookup(() => { })).GetCapabilities();
        Assert.AreEqual("xAI", caps.Provider);
        Assert.AreEqual(AIModels.xAI.Grok4_7, caps.Model);
        foreach (var support in new[] { caps.Streaming, caps.FunctionCalling, caps.Reasoning,
                     caps.NativeReasoning, caps.ImageInput, caps.StructuredOutput, caps.Temperature, caps.TopP })
            Assert.AreEqual(CapabilitySupport.Supported, support);
        foreach (var support in new[] { caps.AsyncFunctionCalling, caps.Steering, caps.ThinkingToggle,
                     caps.WebSearch, caps.FileSearch, caps.ReasoningCachePreservation, caps.FrequencyPenalty, caps.PresencePenalty })
            Assert.AreEqual(CapabilitySupport.Unsupported, support);
        CollectionAssert.AreEqual(new[] { ReasoningLevel.Auto, ReasoningLevel.Low, ReasoningLevel.Medium,
                ReasoningLevel.High, ReasoningLevel.XHigh }, caps.ReasoningLevels.ToArray());
        CollectionAssert.AreEqual(caps.ReasoningLevels.ToArray(), caps.NativeReasoningLevels.ToArray());
        Assert.AreEqual(500000u, caps.MaxOutputTokens);
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow("completion")]
    [DataRow("stream")]
    [DataRow("run")]
    public async Task EveryEffort_UsesChatCompletionsAcrossNativeAndCommonEntryPoints(string path)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        service.MaxTokens = 500000;
        foreach (var effort in new[] { GrokReasoning.Auto, GrokReasoning.Low, GrokReasoning.Medium, GrokReasoning.High, GrokReasoning.XHigh })
        {
            var wire = effort == GrokReasoning.Auto ? null : effort.ToString().ToLowerInvariant();
            service.ReasoningEffort = effort;
            Assert.AreEqual("answer", await InvokeAsync(service, path));
            AssertRequest(handler.Requests.Last(), wire, maxTokens: 500000);

            service.ReasoningEffort = GrokReasoning.High;
            service.WithReasoning(Enum.Parse<ReasoningLevel>(effort.ToString()));
            Assert.AreEqual("answer", await InvokeAsync(service, path));
            AssertRequest(handler.Requests.Last(), wire, maxTokens: 500000);
            Assert.AreEqual(GrokReasoning.High, service.ReasoningEffort);
        }
    }

    [TestMethod]
    [DataRow(GrokReasoning.None)]
    [DataRow((GrokReasoning)999)]
    public async Task InvalidNativeEffort_IsRejectedBeforeHttpAndHistory(GrokReasoning effort)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        service.ReasoningEffort = effort;
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.CreateRequest("invalid").GetCompletionAsync());
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(ReasoningLevel.None)]
    [DataRow(ReasoningLevel.Minimal)]
    [DataRow(ReasoningLevel.Max)]
    public async Task InvalidCommonEffort_MatchesCapabilitiesAndIsRejectedBeforeHttp(ReasoningLevel effort)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        var request = service.CreateRequest("invalid").WithReasoning(effort);
        Assert.AreEqual(CapabilitySupport.Unsupported, request.GetCapabilities().GetReasoningSupport(effort));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => request.GetCompletionAsync());
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task CommonSearchAndCacheFeatures_AreRejectedInsteadOfSilentlyDropped()
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        var request = service.CreateRequest("invalid");
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => request.WithWebSearch().GetCompletionAsync());
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => request.WithFileSearch(new FileSearchStore("xAI", "test")).GetCompletionAsync());
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => request.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync());
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow("completion")]
    [DataRow("stream")]
    [DataRow("run")]
    public async Task ToolRounds_PreserveEffortSpeedAndNativeCallIds(string path)
    {
        var handler = new CaptureHandler(ToolReply, TextReply("answer", "default"));
        var service = Create(handler);
        var calls = 0;
        service.Functions.Add(Lookup(() => calls++));
        service.ConfigureRequestFeatures(new AIRequestFeatures
        {
            Reasoning = new ReasoningOptions { Level = ReasoningLevel.XHigh }, Speed = InferenceSpeed.Fast
        });

        Assert.AreEqual("answer", await InvokeAsync(service, path));
        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, handler.Requests.Count);
        foreach (var request in handler.Requests) AssertRequest(request, "xhigh", "priority");
        var messages = handler.Requests[1].Body.GetProperty("messages").EnumerateArray().ToArray();
        var assistant = messages.Single(m => m.GetProperty("role").GetString() == "assistant");
        Assert.AreEqual("call_lookup", assistant.GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.AreEqual("call_lookup", messages.Single(m => m.GetProperty("role").GetString() == "tool")
            .GetProperty("tool_call_id").GetString());
        Assert.AreEqual(2, service.LastProcessing.Count);
        Assert.AreEqual(InferenceSpeed.Fast, service.LastProcessing[0].AppliedSpeed);
        Assert.AreEqual(InferenceSpeed.Standard, service.LastProcessing[1].AppliedSpeed);
        Assert.IsTrue(service.LastProcessing[1].IsDowngraded);

        await InvokeAsync(service, path);
        AssertRequest(handler.Requests[2], "high");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Builder_CapturesModelAndNativeDefaultsAndKeepsBranchesIndependent(bool run)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        var basis = service.CreateRequest("captured");
        var derived = basis.WithReasoning(ReasoningLevel.Low).WithSpeed(InferenceSpeed.Fast);
        service.ChangeModel(AIModels.xAI.Grok4_5);
        service.ReasoningEffort = GrokReasoning.Medium;

        Assert.AreEqual(AIModels.xAI.Grok4_7, basis.GetCapabilities().Model);
        Assert.AreEqual(CapabilitySupport.Supported, derived.GetCapabilities().GetReasoningSupport(ReasoningLevel.XHigh));
        Assert.AreEqual("answer", await InvokeBuilderAsync(derived, run));
        AssertRequest(handler.Requests[0], "low", "priority");
        Assert.AreEqual("answer", await InvokeBuilderAsync(basis, run));
        AssertRequest(handler.Requests[1], "high");
        Assert.AreEqual(AIModels.xAI.Grok4_5, service.Model);
        Assert.AreEqual(GrokReasoning.Medium, service.ReasoningEffort);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InternalProfiles_UseLowestSupportedEffortWithoutMutatingService(bool summarize)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        var profile = summarize ? RequestProfiles.Summarization : RequestProfiles.QueryRewrite;
        Assert.AreEqual("answer", await service.CreateRequest("internal").WithProfile(profile).GetCompletionAsync());
        Assert.AreEqual("low", handler.Requests[0].Body.GetProperty("reasoning_effort").GetString());
        Assert.AreEqual(profile.MaxTokens!.Value, handler.Requests[0].Body.GetProperty("max_tokens").GetUInt32());
        Assert.AreEqual(GrokReasoning.High, service.ReasoningEffort);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        await service.CreateRequest("ordinary").GetCompletionAsync();
        AssertRequest(handler.Requests[1], "high");
    }

    [TestMethod]
    [DataRow("https://api.x.ai/v1/", InferenceSpeed.ProviderDefault, null)]
    [DataRow("https://api.x.ai/v1/", InferenceSpeed.Standard, "default")]
    [DataRow("https://api.x.ai/v1/", InferenceSpeed.Fast, "priority")]
    [DataRow("https://us.api.x.ai/v1/", InferenceSpeed.ProviderDefault, null)]
    [DataRow("https://us.api.x.ai/v1/", InferenceSpeed.Standard, "default")]
    [DataRow("https://us.api.x.ai/v1/", InferenceSpeed.Fast, "priority")]
    public async Task Speed_OnOfficialEndpointsUsesServiceTierWithoutChangingModel(string endpoint, InferenceSpeed speed, string? tier)
    {
        var handler = new CaptureHandler(TextReply("answer", tier));
        using var client = new HttpClient(handler);
        var service = new XAIService("offline-key", AIModels.xAI.Grok4_7, client) { ReasoningEffort = GrokReasoning.High };
        client.BaseAddress = new Uri(endpoint);
        var request = service.CreateRequest("speed").WithSpeed(speed);
        Assert.AreEqual(CapabilitySupport.Supported, request.GetCapabilities().GetSpeedSupport(speed));
        Assert.AreEqual("answer", await request.GetCompletionAsync());
        Assert.AreEqual(new Uri(endpoint).Host, handler.Requests.Single().Uri.Host);
        Assert.AreEqual(AIModels.xAI.Grok4_7, handler.Requests.Single().Body.GetProperty("model").GetString());
        AssertOptional(handler.Requests.Single().Body, "service_tier", tier);
        Assert.AreEqual(speed, service.LastProcessing.Single().RequestedSpeed);
        Assert.AreEqual(tier, service.LastProcessing.Single().RawAppliedMode);
    }

    [TestMethod]
    [DataRow("https://eu.api.x.ai/v1/")]
    [DataRow("https://us.api.x.ai/custom/")]
    [DataRow("https://example.com/v1/")]
    public async Task UnverifiedEndpoint_DoesNotClaimPrioritySupport(string endpoint)
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new XAIService("offline-key", AIModels.xAI.Grok4_7, client);
        client.BaseAddress = new Uri(endpoint);
        var request = service.CreateRequest("unsupported").WithSpeed(InferenceSpeed.Fast);
        Assert.AreEqual(CapabilitySupport.Unknown, request.GetCapabilities().FastSpeed);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => request.GetCompletionAsync());
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HttpFailure_DoesNotLeakReasoningOrSpeedIntoNextRequest(bool run)
    {
        var handler = new CaptureHandler(TextReply("offline error", status: HttpStatusCode.BadRequest));
        var service = Create(handler);
        var failed = service.CreateRequest("fail").WithReasoning(ReasoningLevel.XHigh).WithSpeed(InferenceSpeed.Fast);
        await Assert.ThrowsExactlyAsync<AIServiceException>(() => InvokeBuilderAsync(failed, run));
        AssertRequest(handler.Requests[0], "xhigh", "priority");
        Assert.AreEqual("answer", await service.CreateRequest("next").GetCompletionAsync());
        AssertRequest(handler.Requests[1], "high");
    }

    private static XAIService Create(CaptureHandler handler) => new("offline-key", AIModels.xAI.Grok4_7, new HttpClient(handler))
    {
        ReasoningEffort = GrokReasoning.High, Temperature = 0.25f, TopP = 0.75f,
        FrequencyPenalty = 0.5f, PresencePenalty = 0.5f
    };

    private static FunctionDefinition Lookup(Action invoked) => new()
    {
        Name = "lookup", Description = "Gets a synthetic value.",
        Handler = _ => { invoked(); return Task.FromResult("42"); }
    };

    private static async Task<string> InvokeBuilderAsync(AIRequestBuilder request, bool streaming)
    {
        if (!streaming) return await request.GetCompletionAsync();
        await using var run = await request.StartRunAsync();
        return (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text;
    }

    private static async Task<string> InvokeAsync(XAIService service, string path)
    {
        if (path == "completion") return await service.GetCompletionAsync("test");
        if (path == "run")
        {
            await using var run = await service.StartRunAsync("test");
            var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(service.LastProcessing.Select(p => p.RawAppliedMode).ToArray(),
                result.Processing.Select(p => p.RawAppliedMode).ToArray());
            return result.Text;
        }
        var text = new StringBuilder();
        await foreach (var chunk in service.StreamAsync("test", StreamOptions.WithFunctions))
        {
            Assert.AreNotEqual(StreamingContentType.Error, chunk.Type, chunk.Content);
            if (chunk.Type == StreamingContentType.Text) text.Append(chunk.Content);
        }
        return text.ToString();
    }

    private static void AssertRequest(CapturedRequest request, string? effort, string? tier = null, uint maxTokens = 8000)
    {
        Assert.AreEqual("/v1/chat/completions", request.Uri.AbsolutePath);
        Assert.AreEqual("grok-4.7", request.Body.GetProperty("model").GetString());
        AssertOptional(request.Body, "reasoning_effort", effort);
        AssertOptional(request.Body, "service_tier", tier);
        Assert.AreEqual(0.25f, request.Body.GetProperty("temperature").GetSingle());
        Assert.AreEqual(0.75f, request.Body.GetProperty("top_p").GetSingle());
        Assert.AreEqual(maxTokens, request.Body.GetProperty("max_tokens").GetUInt32());
        foreach (var name in new[] { "frequency_penalty", "presence_penalty", "stop" })
            Assert.IsFalse(request.Body.TryGetProperty(name, out _), $"Unexpected unsupported field: {name}");
    }

    private static void AssertOptional(JsonElement body, string name, string? value)
    {
        if (value == null) Assert.IsFalse(body.TryGetProperty(name, out _), $"Expected {name} to be omitted.");
        else Assert.AreEqual(value, body.GetProperty(name).GetString());
    }

    private sealed record CapturedRequest(Uri Uri, JsonElement Body);
    private sealed record Reply(string Json, string Stream, HttpStatusCode Status = HttpStatusCode.OK);

    private static Reply TextReply(string text, string? tier = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var ordinary = new Dictionary<string, object?> { ["id"] = "response_text", ["service_tier"] = tier,
            ["choices"] = new[] { new { message = new { role = "assistant", content = text }, finish_reason = "stop" } } };
        var stream = new Dictionary<string, object?> { ["id"] = "response_text", ["service_tier"] = tier,
            ["choices"] = new[] { new { delta = new { role = "assistant", content = text }, finish_reason = "stop" } } };
        return new Reply(JsonSerializer.Serialize(ordinary), JsonSerializer.Serialize(stream), status);
    }

    private static readonly Reply ToolReply = new(
        """{"id":"response_tool","service_tier":"priority","choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_lookup","type":"function","function":{"name":"lookup","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""",
        """{"id":"response_tool","service_tier":"priority","choices":[{"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call_lookup","type":"function","function":{"name":"lookup","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""");

    private sealed class CaptureHandler(params Reply[] replies) : HttpMessageHandler
    {
        private readonly Queue<Reply> _replies = new(replies);
        public List<CapturedRequest> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = document.RootElement.Clone();
            Requests.Add(new CapturedRequest(request.RequestUri!, body));
            var reply = _replies.Count > 0 ? _replies.Dequeue() : TextReply("answer");
            var stream = body.GetProperty("stream").GetBoolean() && reply.Status == HttpStatusCode.OK;
            return new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(stream ? "data: " + reply.Stream + "\n\ndata: [DONE]\n\n" : reply.Json,
                    Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }
}
