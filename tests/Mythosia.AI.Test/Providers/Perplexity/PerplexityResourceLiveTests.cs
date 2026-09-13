using Mythosia.AI.Models.Perplexity;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlatformProbe = Mythosia.AI.Tests.Perplexity.PerplexityPlatformLiveTests.PlatformProbe;

namespace Mythosia.AI.Tests.Perplexity;

// Prepared for later execution. No account resources are created by these tests.
// Load validates explicit opt-in and selected resource settings before reading any credential.
[TestClass]
[TestCategory("Live")]
[TestCategory("Perplexity")]
[TestCategory("PerplexityResources")]
[DoNotParallelize]
public class PerplexityResourceLiveTests
{
    [TestMethod]
    public async Task Profile_UsesSavedInstructionsAndRequestedVersion()
    {
        var settings = PerplexityResourceLiveSettings.Load("Profile");
        const string prompt = "Follow the saved verification profile and return only its verification token.";
        AssertMarkerNotInInput(prompt, settings);
        await using var probe = await PlatformProbe.CreateAsync("profile");
        probe.Service.SystemMessage = string.Empty;
        probe.Service.AgentOptions.Profile = new PerplexityProfile { Id = settings.Id, Version = settings.Version };
        var job = await probe.SubmitAsync(prompt);
        var response = await job.WaitForCompletionAsync(cancellationToken: probe.Token);
        AssertCompleted(response);
        Assert.IsTrue(response.Text.Trim() == settings.ExpectedText,
            "The saved profile must supply the verification token absent from the request.");
        var body = probe.Submissions.Single().Body!;
        Assert.IsFalse(body.ContainsKey("model") || body.ContainsKey("preset") || body.ContainsKey("instructions"),
            "The test must use the saved profile's model and instructions without overrides.");
        AssertReference(body["profile"]!, settings);
        AssertMarkerNotInInput(body.ToJsonString(), settings);
        probe.AssertTransport();
        RecordSuccess("profile", response);
    }

    [TestMethod]
    public async Task CustomSkill_LoadsUploadedInstructionsAndReturnsTheirPrivateToken()
    {
        var settings = PerplexityResourceLiveSettings.Load("CustomSkill");
        const string prompt = "Load the provided custom verification skill and follow its instructions to return only its verification token.";
        AssertMarkerNotInInput(prompt, settings);
        await using var probe = await PlatformProbe.CreateAsync("custom-skill");
        probe.Service.AgentOptions.Skills = [new PerplexitySkill
        {
            Type = PerplexitySkillType.Custom, Id = settings.Id, Version = settings.Version
        }];
        var job = await probe.SubmitAsync(prompt);
        var response = await job.WaitForCompletionAsync(cancellationToken: probe.Token);
        AssertCompleted(response);
        Assert.IsTrue(response.Text.Trim() == settings.ExpectedText,
            "The uploaded skill must supply the verification token absent from the request.");
        Assert.IsTrue(Output(response).Any(item => item["type"]?.GetValue<string>() == "skill_loaded"),
            "The server must record skill loading as well as execute its verification instructions.");
        var body = probe.Submissions.Single().Body!;
        Assert.HasCount(1, body["skills"]!.AsArray());
        AssertReference(body["skills"]![0]!, settings);
        AssertMarkerNotInInput(body.ToJsonString(), settings);
        probe.AssertTransport();
        RecordSuccess("custom-skill", response);
    }

    [TestMethod]
    public async Task Connector_ReadsTheVerificationFixtureThroughTheAllowedTool()
    {
        var settings = PerplexityResourceLiveSettings.Load("Connector");
        var prompt = $"Use only the read-only tool {settings.ConnectorTool} from {settings.ConnectorServerLabel}. " +
            "Read the synthetic verification fixture, make no changes, and return only its verification token. " + settings.ConnectorPrompt;
        AssertMarkerNotInInput(prompt, settings);
        await using var probe = await PlatformProbe.CreateAsync("connector");
        probe.Service.AgentOptions.Tools = [PerplexityHostedTools.Connector(settings.Id,
            settings.ConnectorServerLabel!, [settings.ConnectorTool!])];
        var job = await probe.SubmitAsync(prompt);
        var response = await job.WaitForCompletionAsync(cancellationToken: probe.Token);
        AssertCompleted(response);
        Assert.IsTrue(response.Text.Trim() == settings.ExpectedText,
            "The connector must retrieve the fixture's token absent from the request.");
        var calls = Output(response).Where(item => item["type"]?.GetValue<string>() == "mcp_call").ToArray();
        Assert.IsNotEmpty(calls, "A completed answer alone does not prove connector execution.");
        foreach (var call in calls)
        {
            Assert.IsTrue(call["connector_id"]?.GetValue<string>() == settings.Id &&
                call["server_label"]?.GetValue<string>() == settings.ConnectorServerLabel &&
                call["name"]?.GetValue<string>() == settings.ConnectorTool,
                "Every connector call must target the configured resource and allowlisted tool.");
            Assert.IsNull(call["error"], "Authentication and tool errors are live failures, even with HTTP 200.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(call["output"]?.GetValue<string>()),
                "The connector must return a nonempty tool result.");
        }
        Assert.IsTrue(calls.Any(call => call["output"]!.GetValue<string>().Contains(settings.ExpectedText, StringComparison.Ordinal)),
            "The actual tool output must contain the fixture token.");
        var body = probe.Submissions.Single().Body!;
        Assert.HasCount(1, body["tools"]!.AsArray());
        var tool = body["tools"]![0]!;
        Assert.IsTrue(tool["type"]?.GetValue<string>() == "connector" && tool["id"]?.GetValue<string>() == settings.Id);
        Assert.HasCount(1, tool["allowed_tools"]!.AsArray());
        Assert.IsTrue(tool["allowed_tools"]![0]!.GetValue<string>() == settings.ConnectorTool);
        AssertMarkerNotInInput(body.ToJsonString(), settings);
        probe.AssertTransport();
        RecordSuccess("connector", response);
    }

    private static void AssertReference(JsonNode reference, PerplexityResourceLiveSettings settings)
    {
        Assert.IsTrue(reference["type"]?.GetValue<string>() == "custom" && reference["id"]?.GetValue<string>() == settings.Id,
            "The request must reference the configured account resource.");
        Assert.IsTrue(reference["version"]?.GetValue<string>() == settings.Version,
            "The request must preserve the configured optional version.");
    }

    private static void AssertMarkerNotInInput(string input, PerplexityResourceLiveSettings settings)
        => Assert.IsFalse(input.Contains(settings.ExpectedText, StringComparison.OrdinalIgnoreCase),
            "Use a unique fixture token stored only in the account resource, never in the test input or resource identifiers.");

    private static void AssertCompleted(PerplexityAgentResponse response)
    {
        Assert.AreEqual("completed", response.Status);
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Usage);
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.Id));
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.Text));
    }

    private static IEnumerable<JsonObject> Output(PerplexityAgentResponse response)
        => JsonNode.Parse(response.OutputJson)!.AsArray().OfType<JsonObject>();

    private static void RecordSuccess(string scenario, PerplexityAgentResponse response)
        => Console.WriteLine("LIVE_PERPLEXITY_RESOURCE_RESULT " + JsonSerializer.Serialize(new
        {
            scenario, responseId = response.Id, completed = response.Status == "completed",
            requestReferenceVerified = true, executionVerified = true
        }));
}
