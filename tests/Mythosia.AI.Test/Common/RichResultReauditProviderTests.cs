using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class RichResultReauditProviderTests
{
    private const string OverrideModel = "google/gemini-3.8-flash";
    private const string ResolvedModel = "provider-reported-model-version";

    public static IEnumerable<object[]> ModelSelectionCases => new[]
    {
        "default", "override", "preset", "preset-override", "profile", "profile-override",
        "models", "models-override", "models-single"
    }.SelectMany(selector => new[] { false, true }.Select(builder => new object[] { selector, builder }));

    [TestMethod]
    [DynamicData(nameof(ModelSelectionCases))]
    public async Task PerplexityResult_ReportsActualSingleRequestedModel_AndPreservesSelectorSnapshot(
        string selector, bool useBuilder)
    {
        var options = OptionsFor(selector);
        var handler = new CaptureAgentHandler();
        using var client = new HttpClient(handler);
        var service = new PerplexityService("offline", client).WithPerplexityOptions(options);
        service.DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = null, MaxRounds = 2, EnableLogging = false };

        var request = useBuilder ? service.CreateRequest("question") : null;
        if (useBuilder)
        {
            // The execution and result must both use captured provider options, including nested selectors.
            service.ChangeModel("openai/gpt-5.6-sol");
            service.AgentOptions.ModelOverride = "openai/gpt-5.6-sol";
            service.AgentOptions.Preset = null;
            if (service.AgentOptions.Profile != null) service.AgentOptions.Profile.Id = "mutated-profile";
            if (service.AgentOptions.Models is string[] models) models[0] = "openai/gpt-5.6-sol";
        }

        await using var run = request == null
            ? await service.StartRunAsync("question")
            : await request.StartRunAsync();
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
        var wire = handler.Body!;
        var expectedModel = selector switch
        {
            "default" => "perplexity/sonar",
            "override" or "preset-override" or "profile-override" => OverrideModel,
            _ => null
        };

        Assert.AreEqual(expectedModel, wire["model"]?.GetValue<string>(), "Verify the selected model that actually went over the wire first.");
        if (expectedModel == null) Assert.IsFalse(wire.ContainsKey("model"));
        if (selector.StartsWith("preset", StringComparison.Ordinal))
            Assert.AreEqual("high", wire["preset"]!.GetValue<string>());
        if (selector.StartsWith("profile", StringComparison.Ordinal))
            Assert.AreEqual("original-profile", wire["profile"]!["id"]!.GetValue<string>());
        if (selector.StartsWith("models", StringComparison.Ordinal))
        {
            Assert.AreEqual("perplexity/sonar", wire["models"]![0]!.GetValue<string>());
            Assert.AreEqual(selector == "models-single" ? 1 : 2, wire["models"]!.AsArray().Count);
        }
        Assert.AreEqual(expectedModel, result.RequestedModel,
            "RequestedModel must match the explicit model field; server-selected configurations have no single requested model.");
        Assert.AreEqual(ResolvedModel, result.Model, "Actual response model remains independent from the request selector.");
        Assert.AreEqual("answer", result.Text);
        Assert.AreEqual(1, handler.Calls);
    }

    private static PerplexityAgentOptions OptionsFor(string selector)
    {
        var options = new PerplexityAgentOptions { DisableWebSearch = true };
        if (selector == "override" || selector.EndsWith("-override", StringComparison.Ordinal))
            options.ModelOverride = OverrideModel;
        if (selector.StartsWith("preset", StringComparison.Ordinal)) options.Preset = PerplexityPreset.High;
        if (selector.StartsWith("profile", StringComparison.Ordinal))
            options.Profile = new PerplexityProfile { Id = "original-profile", Version = "1" };
        if (selector.StartsWith("models", StringComparison.Ordinal))
            options.Models = selector == "models-single"
                ? new[] { "perplexity/sonar" }
                : new[] { "perplexity/sonar", OverrideModel };
        return options;
    }

    private sealed class CaptureAgentHandler : HttpMessageHandler
    {
        public JsonObject? Body { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var terminal = new
            {
                type = "response.completed",
                response = new
                {
                    id = "resp-selected", model = ResolvedModel, status = "completed",
                    output = new[]
                    {
                        new
                        {
                            id = "msg-selected", type = "message", role = "assistant", status = "completed",
                            content = new[] { new { type = "output_text", text = "answer" } }
                        }
                    }
                }
            };
            var body = "data: " + System.Text.Json.JsonSerializer.Serialize(terminal) + "\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
