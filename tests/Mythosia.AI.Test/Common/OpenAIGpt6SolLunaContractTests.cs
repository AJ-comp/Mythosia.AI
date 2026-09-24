using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("OpenAI")]
public class OpenAIGpt6SolLunaContractTests
{
    private const string Answer = """
        {"id":"resp_answer","status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"answer"}]}]}
        """;

    public static IEnumerable<object[]> ModelEfforts =>
        from model in new[] { AIModels.OpenAI.Gpt6Sol, AIModels.OpenAI.Gpt6Luna }
        from effort in Enum.GetValues<Gpt6Reasoning>()
        select new object[] { model, effort, effort == Gpt6Reasoning.Auto ? "medium" : effort.ToString().ToLowerInvariant() };

    [TestMethod]
    [DynamicData(nameof(ModelEfforts))]
    public async Task Completion_UsesEachSupportedEffortAndOnlyNoneAllowsSampling(string model, Gpt6Reasoning effort, string expected)
    {
        using var handler = new CaptureHandler(Answer);
        using var client = new HttpClient(handler);
        var service = CreateService(model, client);
        service.WithGpt6Parameters(effort, Verbosity.High, ReasoningSummary.Detailed);
        service.MaxTokens = 200000;

        Assert.AreEqual("answer", await service.GetCompletionAsync("answer"));

        var body = handler.Bodies.Single();
        Assert.AreEqual("/v1/responses", handler.Paths.Single());
        Assert.AreEqual(model, body.GetProperty("model").GetString());
        Assert.AreEqual(expected, Effort(body));
        Assert.AreEqual(128000, body.GetProperty("max_output_tokens").GetInt32());
        Assert.AreEqual("high", body.GetProperty("text").GetProperty("verbosity").GetString());
        Assert.AreEqual("current_turn", body.GetProperty("reasoning").GetProperty("context").GetString());
        AssertSampling(body, effort == Gpt6Reasoning.None);
        Assert.AreEqual(effort != Gpt6Reasoning.None, body.GetProperty("reasoning").TryGetProperty("summary", out _));
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task ProMode_PreservesModelAndExplicitBudget(string model)
    {
        using var handler = new CaptureHandler(Answer);
        using var client = new HttpClient(handler);
        var service = CreateService(model, client);
        service.MaxTokens = 128;
        service.WithGpt6Parameters(Gpt6Reasoning.Max, Verbosity.Low, ReasoningSummary.Detailed, Gpt6ReasoningMode.Pro);

        await service.GetCompletionAsync("reason");

        var body = handler.Bodies.Single();
        Assert.AreEqual(model, body.GetProperty("model").GetString());
        Assert.AreEqual("max", Effort(body));
        Assert.AreEqual("pro", body.GetProperty("reasoning").GetProperty("mode").GetString());
        Assert.AreEqual(128, body.GetProperty("max_output_tokens").GetInt32());
        AssertSampling(body, false);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task DisableReasoningProfile_UsesNoneAndRestoresProDefaults(string model)
    {
        using var handler = new CaptureHandler(Answer, Answer);
        using var client = new HttpClient(handler);
        var service = CreateService(model, client);
        service.WithGpt6Parameters(Gpt6Reasoning.Max, Verbosity.High, ReasoningSummary.Detailed, Gpt6ReasoningMode.Pro);
        service.MaxTokens = 16000;

        await service.GetCompletionAsync("quick answer", new AIRequestProfile { DisableReasoning = true, MaxTokens = 128 });
        await service.GetCompletionAsync("reason");

        Assert.AreEqual("none", Effort(handler.Bodies[0]));
        Assert.AreEqual(128, handler.Bodies[0].GetProperty("max_output_tokens").GetInt32());
        Assert.IsFalse(handler.Bodies[0].GetProperty("reasoning").TryGetProperty("mode", out _));
        Assert.IsFalse(handler.Bodies[0].GetProperty("reasoning").TryGetProperty("summary", out _));
        Assert.AreEqual("max", Effort(handler.Bodies[1]));
        Assert.AreEqual("pro", handler.Bodies[1].GetProperty("reasoning").GetProperty("mode").GetString());
        Assert.AreEqual(16000u, service.MaxTokens);
        Assert.AreEqual(Gpt6Reasoning.Max, service.Gpt6ReasoningEffort);
        Assert.AreEqual(Gpt6ReasoningMode.Pro, service.Gpt6ReasoningMode);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra)]
    [DataRow("gpt-6-astra-2026-09-01")]
    public async Task AstraNativeNone_IsRejectedBeforeNetworkAndHistoryMutation(string model)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(model, client);
        service.Gpt6ReasoningEffort = Gpt6Reasoning.None;

        await Assert.ThrowsAsync<NotSupportedException>(() => service.GetCompletionAsync("invalid"));

        Assert.IsEmpty(handler.Bodies);
        Assert.IsEmpty(service.ActivateChat.Messages);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task BuilderReasoningSamplingAndSpeed_AreCapturedAndDoNotMutateDefaults(string model)
    {
        using var handler = new CaptureHandler(Answer, Answer, Answer);
        using var client = new HttpClient(handler);
        var service = CreateService(model, client);
        service.Gpt6ReasoningEffort = Gpt6Reasoning.High;
        var basis = service.CreateRequest("answer");
        var quick = basis.WithReasoning(ReasoningLevel.None).WithSpeed(InferenceSpeed.Fast);
        var standard = basis.WithSpeed(InferenceSpeed.Standard);
        service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
        service.Gpt6ReasoningEffort = Gpt6Reasoning.Max;
        service.Temperature = 0.9f;
        service.TopP = 0.9f;

        Assert.AreEqual(CapabilitySupport.Supported, quick.GetCapabilities().Temperature);
        Assert.AreEqual(CapabilitySupport.Supported, quick.GetCapabilities().TopP);
        Assert.AreEqual(CapabilitySupport.Unsupported, basis.GetCapabilities().Temperature);
        Assert.AreEqual(CapabilitySupport.Unsupported, basis.GetCapabilities().TopP);
        await quick.GetCompletionAsync();
        await standard.GetCompletionAsync();
        await basis.GetCompletionAsync();

        Assert.AreEqual("none", Effort(handler.Bodies[0]));
        AssertSampling(handler.Bodies[0], true);
        Assert.AreEqual("fast", handler.Bodies[0].GetProperty("service_tier").GetString());
        Assert.AreEqual("high", Effort(handler.Bodies[1]));
        AssertSampling(handler.Bodies[1], false);
        Assert.AreEqual("default", handler.Bodies[1].GetProperty("service_tier").GetString());
        Assert.AreEqual("high", Effort(handler.Bodies[2]));
        Assert.IsFalse(handler.Bodies[2].TryGetProperty("service_tier", out _));
        Assert.IsTrue(handler.Bodies.All(body => body.GetProperty("model").GetString() == model));
        Assert.AreEqual(AIModels.OpenAI.Gpt6Astra, service.Model);
        Assert.AreEqual(Gpt6Reasoning.Max, service.Gpt6ReasoningEffort);
        Assert.AreEqual(0.9f, service.Temperature);
        Assert.AreEqual(0.9f, service.TopP);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol, false)]
    [DataRow(AIModels.OpenAI.Gpt6Sol, true)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, false)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, true)]
    public async Task NoneFunctionContinuation_PreservesSamplingToolsAndResponsesTransport(string model, bool streaming)
    {
        const string toolResponse = """
            {"id":"resp_tool","status":"completed","output":[{"id":"fc_read","type":"function_call","status":"completed","call_id":"call_read","name":"read_value","arguments":"{}"}]}
            """;
        using var handler = new CaptureHandler(streaming ? Sse(toolResponse) : toolResponse, streaming ? Sse(Answer, "answer") : Answer);
        using var client = new HttpClient(handler);
        var service = CreateService(model, client);
        var invocations = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "read_value", AllowAsync = true, Handler = _ => { invocations++; return Task.FromResult("42"); }
        });
        var request = service.CreateRequest("read the value").WithReasoning(ReasoningLevel.None);
        Assert.AreEqual(CapabilitySupport.Supported, request.GetCapabilities().Temperature);
        Assert.AreEqual(CapabilitySupport.Supported, request.GetCapabilities().TopP);

        if (streaming)
        {
            service.ConfigureRequestFeatures(new AIRequestFeatures { Reasoning = new ReasoningOptions { Level = ReasoningLevel.None } });
            var events = new List<StreamingContent>();
            await foreach (var item in service.StreamAsync("read the value", StreamOptions.WithFunctions)) events.Add(item);
            Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error));
            Assert.AreEqual("answer", string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        }
        else Assert.AreEqual("answer", await request.GetCompletionAsync());

        Assert.AreEqual(1, invocations);
        Assert.AreEqual(2, handler.Bodies.Count);
        Assert.IsTrue(handler.Paths.All(path => path == "/v1/responses"));
        foreach (var body in handler.Bodies)
        {
            Assert.AreEqual(model, body.GetProperty("model").GetString());
            Assert.AreEqual("none", Effort(body));
            AssertSampling(body, true);
            Assert.AreEqual("read_value", body.GetProperty("tools")[0].GetProperty("name").GetString());
            Assert.IsTrue(body.GetProperty("tools")[0].GetProperty("async").GetBoolean());
        }
        var outputs = handler.Bodies[1].GetProperty("input").EnumerateArray()
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "function_call_output").ToArray();
        Assert.AreEqual(1, outputs.Length);
        Assert.AreEqual("call_read", outputs[0].GetProperty("call_id").GetString());
        Assert.AreEqual("42", outputs[0].GetProperty("output").GetString());
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    [DataRow("gpt-6-sol-2026-09-22")]
    [DataRow("gpt-6-luna-2026-09-22")]
    public void KnownModels_ExposeNativeRunSearchAndCompleteEffortContract(string model)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(model, client);
        var capabilities = service.GetCapabilities();
        CollectionAssert.AreEquivalent(new[] { ReasoningLevel.Auto, ReasoningLevel.None, ReasoningLevel.Low, ReasoningLevel.Medium,
            ReasoningLevel.High, ReasoningLevel.XHigh, ReasoningLevel.Max }, capabilities.ReasoningLevels.ToArray());
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.ThinkingToggle);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.AsyncFunctionCalling);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Steering);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.ReasoningCachePreservation);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.WebSearch);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.FileSearch);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.ImageInput);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.StructuredOutput);
        Assert.AreEqual(model == AIModels.OpenAI.Gpt6Sol || model == AIModels.OpenAI.Gpt6Luna
            ? CapabilitySupport.Supported : CapabilitySupport.Unsupported, capabilities.GetSpeedSupport(InferenceSpeed.Fast));
        Assert.AreEqual(128000u, capabilities.MaxOutputTokens);
        service.Gpt6ReasoningMode = Gpt6ReasoningMode.Pro;
        Assert.AreEqual(CapabilitySupport.Unsupported, service.GetCapabilities().ReasoningCachePreservation);
        Assert.IsEmpty(handler.Bodies);
    }

    [TestMethod]
    [DataRow("gpt-6-sol-experimental")]
    [DataRow("gpt-6-luna-experimental")]
    [DataRow("gpt-6-sol-2026-99-99")]
    [DataRow("gpt-6-luna-2026-09-22-extra")]
    [DataRow("gpt-6-terra")]
    public async Task UnknownModelSuffixes_DoNotInheritNativeFeatures(string model)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(model, client);
        var capabilities = service.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.Reasoning);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.AsyncFunctionCalling);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.Steering);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.ReasoningCachePreservation);
        Assert.AreNotEqual(CapabilitySupport.Supported, capabilities.GetSpeedSupport(InferenceSpeed.Fast));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.CreateRequest("unknown").WithSpeed(InferenceSpeed.Fast).GetCompletionAsync());
        Assert.IsEmpty(handler.Bodies);
        Assert.IsEmpty(service.ActivateChat.Messages);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol, ReasoningLevel.High, ReasoningLevel.None)]
    [DataRow(AIModels.OpenAI.Gpt6Sol, ReasoningLevel.None, ReasoningLevel.High)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, ReasoningLevel.High, ReasoningLevel.None)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, ReasoningLevel.None, ReasoningLevel.High)]
    public async Task PreservedCacheSampling_FollowsCurrentEffortInsteadOfOriginalWireBaseline(string model, ReasoningLevel initial, ReasoningLevel changed)
    {
        using var handler = new CaptureHandler(Answer, Answer, Answer);
        using var client = new HttpClient(handler);
        var service = CreateService(model, client);
        service.ConfigureRequestFeatures(new AIRequestFeatures { Reasoning = new ReasoningOptions { Level = initial } });
        await service.GetCompletionAsync("initial");
        service.ConfigureRequestFeatures(new AIRequestFeatures
        {
            Reasoning = new ReasoningOptions { Level = changed, Cache = CachePreservation.Required }
        });
        await service.GetCompletionAsync("changed");
        var expectedSampling = changed == ReasoningLevel.None ? CapabilitySupport.Supported : CapabilitySupport.Unsupported;
        Assert.AreEqual(expectedSampling, service.GetCapabilities().Temperature);
        Assert.AreEqual(expectedSampling, service.GetCapabilities().TopP);
        await service.GetCompletionAsync("keep changed effort");

        AssertSampling(handler.Bodies[0], initial == ReasoningLevel.None);
        foreach (var body in handler.Bodies.Skip(1))
        {
            Assert.AreEqual(initial.ToString().ToLowerInvariant(), Effort(body), "Cache preservation keeps the original reasoning prefix.");
            AssertSampling(body, changed == ReasoningLevel.None);
            var update = body.GetProperty("input").EnumerateArray()
                .Single(item => item.TryGetProperty("type", out var type) && type.GetString() == "configuration_update");
            Assert.AreEqual(changed.ToString().ToLowerInvariant(), update.GetProperty("reasoning").GetProperty("effort").GetString());
        }
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol, ReasoningLevel.High)]
    [DataRow(AIModels.OpenAI.Gpt6Sol, ReasoningLevel.Auto)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, ReasoningLevel.High)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, ReasoningLevel.Auto)]
    public async Task TemporaryReasoning_DoesNotReplacePersistedNoneOrLeakSampling(string model, ReasoningLevel temporary)
    {
        using var handler = new CaptureHandler(Answer, Answer, Answer, Answer);
        using var client = new HttpClient(handler);
        var service = CreateService(model, client);
        service.ConfigureRequestFeatures(new AIRequestFeatures { Reasoning = new ReasoningOptions { Level = ReasoningLevel.High } });
        await service.GetCompletionAsync("initial");
        service.ConfigureRequestFeatures(new AIRequestFeatures
        {
            Reasoning = new ReasoningOptions { Level = ReasoningLevel.None, Cache = CachePreservation.Required }
        });
        await service.GetCompletionAsync("persist none");
        service.ConfigureRequestFeatures(new AIRequestFeatures { Reasoning = new ReasoningOptions { Level = temporary } });
        await service.GetCompletionAsync("temporary reasoning");
        await service.GetCompletionAsync("restore none");

        AssertSampling(handler.Bodies[1], true);
        AssertSampling(handler.Bodies[2], false);
        AssertSampling(handler.Bodies[3], true);
        Assert.IsTrue(handler.Bodies.All(body => Effort(body) == "high"), "Cache updates must not rewrite the initial prefix.");
        var temporaryUpdate = handler.Bodies[2].GetProperty("input").EnumerateArray()
            .Last(item => item.TryGetProperty("type", out var type) && type.GetString() == "configuration_update");
        Assert.AreEqual(temporary == ReasoningLevel.Auto ? "medium" : "high", temporaryUpdate.GetProperty("reasoning").GetProperty("effort").GetString());
        var restoredUpdate = handler.Bodies[3].GetProperty("input").EnumerateArray()
            .Last(item => item.TryGetProperty("type", out var type) && type.GetString() == "configuration_update");
        Assert.AreEqual("none", restoredUpdate.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.AreEqual(Gpt6Reasoning.Auto, service.Gpt6ReasoningEffort);
    }

    private static OpenAIService CreateService(string model, HttpClient client) => new("offline-test-key", model, client)
    { Temperature = 0.3f, TopP = 0.7f, FrequencyPenalty = 0.2f, PresencePenalty = 0.2f };

    private static string Effort(JsonElement body) => body.GetProperty("reasoning").GetProperty("effort").GetString()!;

    private static void AssertSampling(JsonElement body, bool enabled)
    {
        Assert.AreEqual(enabled, body.TryGetProperty("temperature", out var temperature));
        Assert.AreEqual(enabled, body.TryGetProperty("top_p", out var topP));
        if (enabled)
        {
            Assert.AreEqual(0.3f, temperature.GetSingle());
            Assert.AreEqual(0.7f, topP.GetSingle());
        }
        Assert.IsFalse(body.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(body.TryGetProperty("presence_penalty", out _));
    }

    private static string Sse(string response, string? delta = null) =>
        (delta == null ? "" : $"data: {{\"type\":\"response.output_text.delta\",\"delta\":{JsonSerializer.Serialize(delta)}}}\n\n") +
        $"data: {{\"type\":\"response.completed\",\"response\":{response}}}\n\n";

    private sealed class CaptureHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);
        public List<JsonElement> Bodies { get; } = new();
        public List<string> Paths { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Bodies.Add(body.RootElement.Clone());
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (_responses.Count == 0) throw new InvalidOperationException("Unexpected HTTP request.");
            var response = _responses.Dequeue();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, response.StartsWith("data:", StringComparison.Ordinal) ? "text/event-stream" : "application/json")
            };
        }
    }
}
