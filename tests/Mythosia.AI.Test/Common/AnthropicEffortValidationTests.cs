using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AnthropicEffortValidationTests
{
    public enum EntryPath { Direct, Builder, Stream, Run, ConversationCount, PromptCount }

    public static IEnumerable<object[]> InvalidEffortCases()
    {
        foreach (var row in AnthropicThinkingProfileTests.ProfileCases()
            .Select(row => (string)row[0]).Distinct())
        foreach (var path in Enum.GetValues<EntryPath>())
            yield return new object[] { row, path };
    }

    [TestMethod]
    [DynamicData(nameof(InvalidEffortCases))]
    public async Task UndefinedNativeEffort_IsRejectedBeforeHttp(string model, EntryPath path)
    {
        using var transport = new RecordingTransport();
        using var http = new HttpClient(transport);
        var service = new AnthropicService("offline-key", model, http)
        {
            AdaptiveThinkingEffort = (ClaudeReasoningEffort)1234
        };

        var error = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => Execute(service, path));

        Assert.AreEqual(nameof(service.AdaptiveThinkingEffort), error.ParamName);
        Assert.IsEmpty(transport.Requests);
        Assert.IsEmpty(service.ActivateChat.Messages, "Rejected options must not retain an unsent prompt.");
        Assert.AreEqual((ClaudeReasoningEffort)1234, service.AdaptiveThinkingEffort);
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    public async Task CommonReasoning_ReplacesUnusedInvalidNativeEffort(string model)
    {
        using var transport = new RecordingTransport();
        using var http = new HttpClient(transport);
        var service = new AnthropicService("offline-key", model, http)
        {
            AdaptiveThinkingEffort = (ClaudeReasoningEffort)1234
        };

        Assert.AreEqual("answer", await service.CreateRequest("override")
            .WithReasoning(ReasoningLevel.High).GetCompletionAsync());

        Assert.AreEqual("high", transport.Requests.Single()["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual((ClaudeReasoningEffort)1234, service.AdaptiveThinkingEffort);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.GetCompletionAsync("native"));
        Assert.HasCount(1, transport.Requests);
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    [DataRow(AIModels.Anthropic.ClaudeHaiku4_5_251001)]
    public async Task IsolatedProfile_ReplacesInvalidEffortWithoutChangingDefaults(string model)
    {
        using var transport = new RecordingTransport();
        using var http = new HttpClient(transport);
        var service = new AnthropicService("offline-key", model, http)
        {
            AdaptiveThinkingEffort = (ClaudeReasoningEffort)(-1)
        };

        Assert.AreEqual("answer", await service.GetCompletionAsync("helper", RequestProfiles.QueryRewrite));
        Assert.IsEmpty(service.ActivateChat.Messages);
        Assert.AreEqual((ClaudeReasoningEffort)(-1), service.AdaptiveThinkingEffort);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.GetInputTokenCountAsync("native"));
        Assert.HasCount(1, transport.Requests);
    }

    [TestMethod]
    public async Task Builder_ValidatesCapturedEffortRatherThanLaterServiceDefault()
    {
        using var transport = new RecordingTransport();
        using var http = new HttpClient(transport);
        var service = new AnthropicService("offline-key", AIModels.Anthropic.ClaudeOpus5_5, http);
        var valid = service.CreateRequest("valid");
        service.AdaptiveThinkingEffort = (ClaudeReasoningEffort)1234;
        var invalid = service.CreateRequest("invalid");
        service.AdaptiveThinkingEffort = ClaudeReasoningEffort.Low;

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => invalid.GetCompletionAsync());
        Assert.IsEmpty(transport.Requests);
        Assert.AreEqual("answer", await valid.GetCompletionAsync());
        Assert.AreEqual("medium", transport.Requests.Single()["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual(ClaudeReasoningEffort.Low, service.AdaptiveThinkingEffort);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RejectedEffort_DoesNotSummarizeOrRetainInput_AndRetrySendsOnlyAcceptedInput(bool builder)
    {
        using var transport = new RecordingTransport();
        using var http = new HttpClient(transport);
        var service = new AnthropicService("offline-key", AIModels.Anthropic.ClaudeSonnet4_6, http)
        {
            AdaptiveThinkingEffort = (ClaudeReasoningEffort)1234,
            ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 2, KeepRecentCount = 2 }
        };
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "parent first"));
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "parent answer"));
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "parent second"));
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "parent final"));
        var original = service.ActivateChat.Messages.ToArray();

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => builder
            ? service.CreateRequest("unsent rejected input").GetCompletionAsync()
            : service.GetCompletionAsync("unsent rejected input"));

        Assert.IsEmpty(transport.Requests);
        Assert.IsNull(service.ConversationPolicy.CurrentSummary);
        CollectionAssert.AreEqual(original, service.ActivateChat.Messages.ToArray());
        service.AdaptiveThinkingEffort = ClaudeReasoningEffort.Low;
        Assert.AreEqual("answer", await service.GetCompletionAsync("accepted retry"));
        Assert.HasCount(2, transport.Requests, "A valid retry still summarizes normally before completion.");
        Assert.IsFalse(transport.Requests.Any(body => body.ToJsonString().Contains("unsent rejected input", StringComparison.Ordinal)));
        StringAssert.Contains(transport.Requests.Last().ToJsonString(), "accepted retry");
    }

    private static async Task Execute(AnthropicService service, EntryPath path)
    {
        switch (path)
        {
            case EntryPath.Direct: await service.GetCompletionAsync("native"); break;
            case EntryPath.Builder: await service.CreateRequest("native").GetCompletionAsync(); break;
            case EntryPath.Stream:
                await foreach (var _ in service.CreateRequest("native").StreamAsync()) { }
                break;
            case EntryPath.Run:
                await using (var run = await service.CreateRequest("native").StartRunAsync())
                    await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
                break;
            case EntryPath.ConversationCount: await service.GetInputTokenCountAsync(); break;
            case EntryPath.PromptCount: await service.GetInputTokenCountAsync("native"); break;
            default: throw new ArgumentOutOfRangeException(nameof(path));
        }
    }

    private sealed class RecordingTransport : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"id":"offline","type":"message","role":"assistant","content":[{"type":"text","text":"answer"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}
                    """, Encoding.UTF8, "application/json")
            };
        }
    }
}
