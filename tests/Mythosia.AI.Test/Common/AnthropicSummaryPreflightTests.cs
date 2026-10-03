using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("SummaryPolicy")]
public class AnthropicSummaryPreflightTests
{
    private const string OriginalSummary = "ORIGINAL_SUMMARY";
    private const string RejectedInput = "UNSENT_REJECTED_INPUT";

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    public async Task StructuredInvalidThinking_DoesNotCompact_AndValidRetryStillSummarizes(string model)
    {
        using var transport = new RecordingTransport();
        using var http = new HttpClient(transport);
        var service = CreateService(model, http);
        service.AdaptiveThinkingEffort = (ClaudeReasoningEffort)1234;
        var originalChat = service.ActivateChat;
        var originalMessages = originalChat.Messages.ToArray();

        var error = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => service.GetCompletionAsync<Answer>(RejectedInput));

        Assert.AreEqual(nameof(service.AdaptiveThinkingEffort), error.ParamName);
        AssertUnchanged(service, transport, originalChat, originalMessages);
        Assert.AreEqual((ClaudeReasoningEffort)1234, service.AdaptiveThinkingEffort);

        service.AdaptiveThinkingEffort = ClaudeReasoningEffort.High;
        var result = await service.GetCompletionAsync<Answer>("accepted structured retry");

        Assert.AreEqual("answer", result.Value);
        Assert.HasCount(2, transport.Requests);
        StringAssert.Contains(transport.Requests[0].ToJsonString(), "original-0");
        StringAssert.Contains(transport.Requests[1].ToJsonString(), "accepted structured retry");
        Assert.IsFalse(transport.Requests.Any(body => body.ToJsonString().Contains(RejectedInput, StringComparison.Ordinal)));
        Assert.AreEqual("UPDATED_SUMMARY", service.ConversationPolicy!.CurrentSummary);
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreSame(originalMessages[2], originalChat.Messages[0]);
        Assert.AreSame(originalMessages[3], originalChat.Messages[1]);
    }

    public static IEnumerable<object[]> InvalidProfileCases()
    {
        foreach (var model in Models)
        foreach (var builder in new[] { false, true })
        foreach (var serviceStateless in new[] { false, true })
        foreach (var purpose in new[] { AIRequestPurpose.Default, AIRequestPurpose.QueryRewrite })
            yield return new object[] { model, builder, serviceStateless, purpose };
    }

    [TestMethod]
    [DynamicData(nameof(InvalidProfileCases))]
    public async Task StatefulDisabledProfileWithBinding_IsRejectedBeforeCompaction(
        string model, bool builder, bool serviceStateless, AIRequestPurpose purpose)
    {
        using var transport = new RecordingTransport();
        using var http = new HttpClient(transport);
        var service = CreateService(model, http);
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High)
            .WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        service.StatelessMode = serviceStateless;
        var originalChat = service.ActivateChat;
        var originalMessages = originalChat.Messages.ToArray();
        var profile = new AIRequestProfile { Purpose = purpose, Stateless = false, DisableReasoning = true };

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => Send(service, builder, RejectedInput, profile));

        AssertUnchanged(service, transport, originalChat, originalMessages);
        // Provider extensions determine the effective configuration. They must run once
        // before it can be validated, including when that configuration is rejected.
        CollectionAssert.AreEqual(new[] { purpose }, service.ProfileApplications.ToArray());
        CollectionAssert.AreEqual(new[] { purpose }, service.ProviderProfileApplications.ToArray());
        Assert.AreEqual(serviceStateless, service.StatelessMode);
        Assert.AreEqual(ClaudeThinkingPrefixMismatchBehavior.Error, service.ThinkingPrefixMismatchBehavior);
        Assert.AreEqual(ClaudeReasoningEffort.High, service.AdaptiveThinkingEffort);

        // Rejected preparation must not leave reasoning disabled for a later valid request.
        var valid = new AIRequestProfile { Stateless = false };
        await Send(service, builder, "accepted profile retry", valid);

        Assert.HasCount(2, transport.Requests);
        Assert.AreEqual("adaptive", transport.Requests[1]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("error", transport.Requests[1]["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        Assert.IsFalse(transport.Requests.Any(body => body.ToJsonString().Contains(RejectedInput, StringComparison.Ordinal)));
        Assert.AreEqual(serviceStateless, service.StatelessMode);
    }

    public static IEnumerable<object[]> ValidProfileCases()
    {
        foreach (var model in Models)
        foreach (var builder in new[] { false, true })
        foreach (var purpose in new[] { AIRequestPurpose.Default, AIRequestPurpose.QueryRewrite })
            yield return new object[] { model, builder, purpose };
    }

    [TestMethod]
    [DynamicData(nameof(ValidProfileCases))]
    public async Task EffectiveProfile_ReplacesUnusedInvalidNativeThinking_WithoutDuplicatingHooksOrLeaking(
        string model, bool builder, AIRequestPurpose purpose)
    {
        using var transport = new RecordingTransport();
        using var http = new HttpClient(transport);
        var service = CreateService(model, http);
        service.AdaptiveThinkingEffort = (ClaudeReasoningEffort)1234;
        // No explicit Stateless override: builder execution must also isolate the profile
        // when it has already normalized the profile into its captured settings.
        var profile = new AIRequestProfile { Purpose = purpose, DisableReasoning = true };

        await Send(service, builder, "accepted profile", profile);

        Assert.HasCount(2, transport.Requests);
        CollectionAssert.AreEqual(new[] { purpose, AIRequestPurpose.Summarization }, service.ProfileApplications.ToArray());
        CollectionAssert.AreEqual(new[] { purpose, AIRequestPurpose.Summarization }, service.ProviderProfileApplications.ToArray());
        Assert.AreEqual((ClaudeReasoningEffort)1234, service.AdaptiveThinkingEffort);
        Assert.IsFalse(service.StatelessMode);
        var acceptedHistory = JsonSerializer.Serialize(service.ActivateChat.Messages);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.GetCompletionAsync(RejectedInput));

        Assert.HasCount(2, transport.Requests, "The accepted profile must not replace the next request's native settings.");
        Assert.AreEqual(acceptedHistory, JsonSerializer.Serialize(service.ActivateChat.Messages));
        Assert.HasCount(2, service.ProfileApplications);
        Assert.HasCount(2, service.ProviderProfileApplications);
    }

    private static readonly string[] Models =
    [
        AIModels.Anthropic.ClaudeOpus5,
        AIModels.Anthropic.ClaudeSonnet4_6,
        AIModels.Anthropic.ClaudeSonnet5_5
    ];

    private static ProfileTrackingService CreateService(string model, HttpClient http)
    {
        var service = new ProfileTrackingService(model, http)
        {
            ConversationPolicy = new SummaryConversationPolicy
            {
                TriggerCount = 2, KeepRecentCount = 2, CurrentSummary = OriginalSummary
            }
        };
        for (var i = 0; i < 4; i++)
            service.ActivateChat.Messages.Add(new Message(i % 2 == 0 ? ActorRole.User : ActorRole.Assistant, $"original-{i}"));
        return service;
    }

    private static Task<string> Send(AnthropicService service, bool builder, string prompt, AIRequestProfile profile)
        => builder ? service.CreateRequest(prompt).WithProfile(profile).GetCompletionAsync()
            : service.GetCompletionAsync(prompt, profile);

    private static void AssertUnchanged(AnthropicService service, RecordingTransport transport,
        ChatBlock originalChat, Message[] originalMessages)
    {
        Assert.IsEmpty(transport.Requests, "Local validation must precede even the internal summary HTTP request.");
        Assert.AreSame(originalChat, service.ActivateChat);
        CollectionAssert.AreEqual(originalMessages, service.ActivateChat.Messages.ToArray());
        Assert.AreEqual(OriginalSummary, service.ConversationPolicy!.CurrentSummary);
    }

    public sealed class Answer
    {
        public string? Value { get; set; }
    }

    private sealed class ProfileTrackingService(string model, HttpClient http)
        : AnthropicService("offline-key", model, http)
    {
        public List<AIRequestPurpose> ProfileApplications { get; } = [];
        public List<AIRequestPurpose> ProviderProfileApplications { get; } = [];

        protected override Action ApplyRequestProfile(AIRequestProfile profile)
        {
            ProfileApplications.Add(profile.Purpose);
            return base.ApplyRequestProfile(profile);
        }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            ProviderProfileApplications.Add(profile.Purpose);
            return base.ApplyProviderSpecificRequestProfile(profile);
        }
    }

    private sealed class RecordingTransport : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Requests.Add(body);
            var summarizing = body["messages"]!.ToJsonString().Contains("[New messages to incorporate]", StringComparison.Ordinal);
            var text = summarizing ? "UPDATED_SUMMARY" : "{\"Value\":\"answer\"}";
            var response = JsonSerializer.Serialize(new
            {
                id = "offline", type = "message", role = "assistant",
                content = new[] { new { type = "text", text } }, stop_reason = "end_turn",
                usage = new { input_tokens = 1, output_tokens = 1 }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
