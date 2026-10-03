using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Perplexity;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// A request has one effective configuration: provider extensions run once, that
/// configuration is validated before side effects, and its lifetime ends on every exit.
/// These tests exercise public entry points rather than private preparation helpers.
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class RequestPreparationContractTests
{
    private const string OriginalSummary = "ORIGINAL_SUMMARY";
    private const string Prompt = "request input";
    private const string AnswerText = "{\"Value\":\"answer\"}";

    public enum ProfilePath { String, Message, Builder, BuilderStream, BuilderRun }
    public enum BarePath { String, Message, Builder, Stream, BuilderStream, Run, BuilderRun, Structured }

    [TestMethod]
    public async Task RunWorker_KeepsEffectiveSettingsAfterTheCallingProfileScopeRestores()
    {
        using var transport = new ClaudeTransport();
        using var http = new HttpClient(transport);
        var service = new RestoringProfileService(http) { Temperature = 0.8f };
        var enteredContext = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseContext = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.WithSystemMessageProvider(async token =>
        {
            enteredContext.TrySetResult();
            await releaseContext.Task.WaitAsync(token);
            return new AIRequestContext { SystemMessageSuffix = "fixture context" };
        });

        await using var run = await service.CreateRequest(Prompt)
            .WithProfile(new AIRequestProfile { DisableReasoning = true }).StartRunAsync();
        try
        {
            await enteredContext.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, service.RestoreCount,
                "The producer is deliberately paused until StartRunAsync has returned and restored its caller's scope.");
            Assert.IsEmpty(transport.Requests);
        }
        finally { releaseContext.TrySetResult(); }

        Assert.AreEqual(AnswerText, (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.HasCount(1, transport.Requests);
        Assert.AreEqual(0.37f, transport.Requests[0]["temperature"]!.GetValue<float>(),
            "Restoring the caller's temporary settings must not mutate the running worker's validated snapshot.");
        Assert.AreEqual(0.8f, service.Temperature);
        Assert.AreEqual(1, service.RestoreCount);
    }

    public static IEnumerable<object[]> ProfileCases()
    {
        foreach (var model in new[] { AIModels.Anthropic.ClaudeSonnet4_6, AIModels.Anthropic.ClaudeSonnet5_5 })
        foreach (var path in Enum.GetValues<ProfilePath>())
        foreach (var summary in new[] { false, true })
            yield return new object[] { model, path, summary };
    }

    [TestMethod]
    [DynamicData(nameof(ProfileCases))]
    public async Task ProviderProfileExtension_DeterminesValidatedConfiguration_AndRunsOnce(
        string model, ProfilePath path, bool summary)
    {
        using var transport = new ClaudeTransport();
        using var http = new HttpClient(transport);
        var service = new ProfileService(model, http);
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High)
            .WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        if (summary) Seed(service);
        var profile = new AIRequestProfile { Stateless = false, DisableReasoning = true };

        var result = await Send(service, path, profile);

        Assert.AreEqual(AnswerText, result);
        var summarizes = summary && path != ProfilePath.Message;
        CollectionAssert.AreEqual(summarizes
            ? new[] { AIRequestPurpose.Default, AIRequestPurpose.Summarization }
            : new[] { AIRequestPurpose.Default }, service.Applied.ToArray());
        CollectionAssert.AreEqual(service.Applied.ToArray(), service.ProviderApplied.ToArray());
        Assert.AreEqual(service.Applied.Count, service.RestoreCount, "Each execution hook must be restored once.");
        Assert.HasCount(summarizes ? 2 : 1, transport.Requests);
        var completion = transport.Requests.Single(request =>
            !request["messages"]!.ToJsonString().Contains("[New messages to incorporate]", StringComparison.Ordinal));
        Assert.IsNull(completion["thinking"]?["block_binding"],
            "The extension's effective configuration, not a private approximation of the profile, must be sent.");
        Assert.AreEqual(ClaudeThinkingPrefixMismatchBehavior.Error, service.ThinkingPrefixMismatchBehavior);
        Assert.AreEqual(ClaudeReasoningEffort.High, service.AdaptiveThinkingEffort);
        Assert.IsFalse(service.StatelessMode);
    }

    public static IEnumerable<object[]> ProfileExitCases()
    {
        foreach (var path in Enum.GetValues<ProfilePath>())
        foreach (var outcome in new[] { "success", "failure", "cancel", "validation" })
            yield return new object[] { path, outcome };
    }

    [TestMethod]
    [DynamicData(nameof(ProfileExitCases))]
    public async Task AuxiliaryProfile_IsAppliedAndRestoredOnce_OnEveryExit(ProfilePath path, string outcome)
    {
        using var cancellation = new CancellationTokenSource();
        using var transport = new ClaudeTransport();
        using var http = new HttpClient(transport);
        var service = new ProfileService(AIModels.Anthropic.ClaudeSonnet5_5, http)
        {
            InjectInvalidEffort = outcome == "validation"
        };
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High)
            .WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        Seed(service);
        var chat = service.ActivateChat;
        var original = chat.Messages.ToArray();
        var history = JsonSerializer.Serialize(original);
        var profile = RequestProfiles.QueryRewrite;
        if (outcome == "validation") profile.DisableReasoning = false;
        transport.BeforeResponse = token =>
        {
            if (outcome == "failure") throw new HttpRequestException("offline fixture failure");
            if (outcome == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
        };

        if (outcome == "success") Assert.AreEqual(AnswerText, await Send(service, path, profile));
        else if (outcome == "failure")
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => Send(service, path, profile));
        else if (outcome == "cancel")
            await Assert.ThrowsAsync<OperationCanceledException>(() => Send(service, path, profile, cancellation.Token));
        else
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => Send(service, path, profile));

        CollectionAssert.AreEqual(new[] { AIRequestPurpose.QueryRewrite }, service.Applied.ToArray());
        CollectionAssert.AreEqual(service.Applied.ToArray(), service.ProviderApplied.ToArray());
        Assert.AreEqual(1, service.RestoreCount);
        Assert.HasCount(outcome == "validation" ? 0 : 1, transport.Requests);
        Assert.AreSame(chat, service.ActivateChat);
        CollectionAssert.AreEqual(original, chat.Messages.ToArray());
        Assert.AreEqual(history, JsonSerializer.Serialize(chat.Messages));
        Assert.AreEqual(OriginalSummary, service.ConversationPolicy!.CurrentSummary);
        Assert.AreEqual(ClaudeThinkingPrefixMismatchBehavior.Error, service.ThinkingPrefixMismatchBehavior);
        Assert.AreEqual(ClaudeReasoningEffort.High, service.AdaptiveThinkingEffort);
        Assert.IsFalse(service.StatelessMode);
    }

    public static IEnumerable<object[]> InvalidBudgetCases()
        => Enum.GetValues<BarePath>().Select(path => new object[] { path });

    [TestMethod]
    [DynamicData(nameof(InvalidBudgetCases))]
    public async Task InvalidManualBudget_IsRejectedBeforeSummaryOrInputHistory(BarePath path)
    {
        using var transport = new ClaudeTransport();
        using var http = new HttpClient(transport);
        var service = new AnthropicService("offline-key", AIModels.Anthropic.ClaudeHaiku4_5_251001, http)
        {
            ThinkingBudget = 64000
        };
        Seed(service);
        var chat = service.ActivateChat;
        var original = chat.Messages.ToArray();
        var history = JsonSerializer.Serialize(original);

        var error = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => SendBare(service, path));

        Assert.AreEqual(nameof(AnthropicService.ThinkingBudget), error.ParamName);
        Assert.IsEmpty(transport.Requests, "An invalid request must not invoke even an automatic summary.");
        Assert.AreSame(chat, service.ActivateChat);
        CollectionAssert.AreEqual(original, chat.Messages.ToArray());
        Assert.AreEqual(history, JsonSerializer.Serialize(chat.Messages));
        Assert.AreEqual(OriginalSummary, service.ConversationPolicy!.CurrentSummary);

        service.ThinkingBudget = 1024;
        await SendBare(service, path);
        var summarizes = path != BarePath.Message;
        Assert.HasCount(summarizes ? 2 : 1, transport.Requests, "A valid retry must preserve the entry point's summary behavior.");
        Assert.AreEqual(summarizes ? "UPDATED_SUMMARY" : OriginalSummary, service.ConversationPolicy.CurrentSummary);
    }

    public static IEnumerable<object[]> InvalidNativeCases()
    {
        foreach (var invalid in new[] { "openai-effort", "openai-verbosity", "perplexity-model" })
        foreach (var path in Enum.GetValues<ProfilePath>())
            yield return new object[] { invalid, path };
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    public async Task InternalSummary_CannotBeReplacedByTheParentsDynamicContext(string model)
    {
        using var transport = new ClaudeTransport();
        using var http = new HttpClient(transport);
        var service = new AnthropicService("offline-key", model, http);
        Seed(service);
        var callbacks = 0;
        service.WithSystemMessageProvider(() =>
        {
            callbacks++;
            return new AIRequestContext
            {
                RequestMessageOverride = new Message(ActorRole.User, "PARENT_CONTEXT_ONLY")
            };
        });

        await service.GetCompletionAsync(Prompt);

        Assert.AreEqual(1, callbacks, "An internal summary must not rebuild the parent's dynamic context.");
        Assert.HasCount(2, transport.Requests);
        StringAssert.Contains(transport.Requests[0].ToJsonString(), "original-0");
        Assert.IsFalse(transport.Requests[0].ToJsonString().Contains("PARENT_CONTEXT_ONLY", StringComparison.Ordinal));
        StringAssert.Contains(transport.Requests[1].ToJsonString(), "PARENT_CONTEXT_ONLY");
        Assert.AreEqual("UPDATED_SUMMARY", service.ConversationPolicy!.CurrentSummary);
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    public async Task AutomaticSummary_DoesNotInheritTheParentsStructuredOutputContract(string model)
    {
        using var transport = new ClaudeTransport();
        using var http = new HttpClient(transport);
        var service = new AnthropicService("offline-key", model, http);
        Seed(service);

        var result = await service.GetCompletionAsync<Answer>(Prompt);

        Assert.AreEqual("answer", result.Value);
        Assert.HasCount(2, transport.Requests);
        Assert.IsFalse(transport.Requests[0].ToJsonString().Contains("[STRUCTURED OUTPUT]", StringComparison.Ordinal),
            "The auxiliary summary produces prose; it must not receive the parent's JSON schema.");
        StringAssert.Contains(transport.Requests[1].ToJsonString(), "[STRUCTURED OUTPUT]");
        Assert.AreEqual("UPDATED_SUMMARY", service.ConversationPolicy!.CurrentSummary);

        service.ConversationPolicy = null;
        await service.GetCompletionAsync("ordinary follow-up");
        // New schema instructions are temporary; a preserved historical instruction may
        // remain attached to its original turn, so inspect only the current input onward.
        var messages = transport.Requests[^1]["messages"]!.AsArray();
        var currentInput = messages.Select((message, index) => (message, index))
            .Last(item => item.message!.ToJsonString().Contains("ordinary follow-up", StringComparison.Ordinal)).index;
        Assert.IsFalse(messages.Skip(currentInput).Any(message =>
            message!.ToJsonString().Contains("[STRUCTURED OUTPUT]", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DynamicData(nameof(InvalidNativeCases))]
    public async Task AuxiliaryRequests_StillValidateNativeProviderSettings(string invalid, ProfilePath path)
    {
        using var transport = new ClaudeTransport();
        using var http = new HttpClient(transport);
        var openai = new NoConnectOpenAI(http);
        AIService service;
        if (invalid == "perplexity-model") service = new PerplexityService("offline-key", "sonar", http);
        else
        {
            service = openai;
            if (invalid == "openai-effort") openai.Gpt6ReasoningEffort = Gpt6Reasoning.None;
            else openai.Gpt6Verbosity = (Verbosity)987;
        }
        Seed(service);
        var chat = service.ActivateChat;
        var original = chat.Messages.ToArray();
        var profile = RequestProfiles.QueryRewrite;
        if (invalid == "openai-effort") profile.DisableReasoning = false;

        if (invalid == "openai-verbosity")
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => Send(service, path, profile));
        else
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => Send(service, path, profile));

        Assert.AreEqual(0, openai.Connections, "Invalid native settings must be rejected before opening a Run socket.");
        Assert.IsEmpty(transport.Requests);
        Assert.AreSame(chat, service.ActivateChat);
        CollectionAssert.AreEqual(original, chat.Messages.ToArray());
        Assert.AreEqual(OriginalSummary, service.ConversationPolicy!.CurrentSummary);
    }

    private static void Seed(AIService service)
    {
        service.ConversationPolicy = new SummaryConversationPolicy
        {
            TriggerCount = 2, KeepRecentCount = 2, CurrentSummary = OriginalSummary
        };
        for (var i = 0; i < 4; i++)
            service.ActivateChat.Messages.Add(new Message(i % 2 == 0 ? ActorRole.User : ActorRole.Assistant, $"original-{i}"));
    }

    private static async Task<string> Send(AIService service, ProfilePath path, AIRequestProfile profile, CancellationToken token = default)
    {
        if (path == ProfilePath.String) return await service.GetCompletionAsync(Prompt, profile, cancellationToken: token);
        if (path == ProfilePath.Message)
            return await service.GetCompletionAsync(new Message(ActorRole.User, Prompt), profile, cancellationToken: token);
        var request = service.CreateRequest(Prompt).WithProfile(profile);
        if (path == ProfilePath.Builder) return await request.GetCompletionAsync(token);
        if (path == ProfilePath.BuilderStream)
        {
            var text = new StringBuilder();
            await foreach (var chunk in request.StreamAsync(token)) text.Append(chunk);
            return text.ToString();
        }
        await using var run = await request.StartRunAsync(cancellationToken: token);
        return (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text;
    }

    private static async Task SendBare(AIService service, BarePath path)
    {
        switch (path)
        {
            case BarePath.String: await service.GetCompletionAsync(Prompt); break;
            case BarePath.Message: await service.GetCompletionAsync(new Message(ActorRole.User, Prompt), profile: null); break;
            case BarePath.Builder: await service.CreateRequest(Prompt).GetCompletionAsync(); break;
            case BarePath.Stream: await foreach (var _ in service.StreamAsync(Prompt)) { } break;
            case BarePath.BuilderStream: await foreach (var _ in service.CreateRequest(Prompt).StreamAsync()) { } break;
            case BarePath.Structured: await service.GetCompletionAsync<Answer>(Prompt); break;
            default:
                await using (var run = path == BarePath.Run
                    ? await service.StartRunAsync(Prompt)
                    : await service.CreateRequest(Prompt).StartRunAsync())
                    await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
                break;
        }
    }

    public sealed class Answer { public string? Value { get; set; } }

    private sealed class ProfileService(string model, HttpClient http) : AnthropicService("offline-key", model, http)
    {
        public List<AIRequestPurpose> Applied { get; } = [];
        public List<AIRequestPurpose> ProviderApplied { get; } = [];
        public int RestoreCount { get; private set; }
        public bool InjectInvalidEffort { get; init; }

        protected override Action ApplyRequestProfile(AIRequestProfile profile)
        {
            Applied.Add(profile.Purpose);
            return base.ApplyRequestProfile(profile);
        }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            ProviderApplied.Add(profile.Purpose);
            // A real extension can normalize inherited settings before the built-in profile.
            if (profile.DisableReasoning == true)
                SetExecutionSetting<ClaudeThinkingPrefixMismatchBehavior?>(nameof(ThinkingPrefixMismatchBehavior), null);
            var restore = base.ApplyProviderSpecificRequestProfile(profile);
            if (InjectInvalidEffort)
                SetExecutionSetting(nameof(AdaptiveThinkingEffort), (ClaudeReasoningEffort)987);
            return () => { RestoreCount++; restore(); };
        }
    }

    private sealed class NoConnectOpenAI(HttpClient http) : OpenAIService("offline-key", AIModels.OpenAI.Gpt6Astra, http)
    {
        public int Connections { get; private set; }
        protected override Task<WebSocket> ConnectRunWebSocketAsync(CancellationToken cancellationToken)
        {
            Connections++;
            throw new HttpRequestException("Offline fixture: a rejected request must not open a socket.");
        }
    }

    private sealed class RestoringProfileService(HttpClient http)
        : AnthropicService("offline-key", AIModels.Anthropic.ClaudeSonnet4_6, http)
    {
        public int RestoreCount { get; private set; }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            var restoreProvider = base.ApplyProviderSpecificRequestProfile(profile);
            var originalTemperature = RequestTemperature;
            SetExecutionSetting(nameof(Temperature), 0.37f);
            return () =>
            {
                SetExecutionSetting(nameof(Temperature), originalTemperature);
                RestoreCount++;
                restoreProvider();
            };
        }
    }

    private sealed class ClaudeTransport : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = [];
        public Action<CancellationToken>? BeforeResponse { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Requests.Add(body);
            BeforeResponse?.Invoke(cancellationToken);
            var summarizing = body["messages"]!.ToJsonString().Contains("[New messages to incorporate]", StringComparison.Ordinal);
            var text = summarizing ? "UPDATED_SUMMARY" : AnswerText;
            var response = new JsonObject
            {
                ["id"] = "offline", ["type"] = "message", ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                ["stop_reason"] = "end_turn", ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 }
            };
            var streaming = body["stream"]?.GetValue<bool>() == true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(streaming ? ToSse(response, text) : response.ToJsonString(), Encoding.UTF8,
                    streaming ? "text/event-stream" : "application/json")
            };
        }
    }

    private static string ToSse(JsonObject response, string text)
    {
        var start = (JsonObject)response.DeepClone();
        start["content"] = new JsonArray();
        var frames = new JsonObject[]
        {
            new() { ["type"] = "message_start", ["message"] = start },
            new() { ["type"] = "content_block_start", ["index"] = 0,
                ["content_block"] = new JsonObject { ["type"] = "text", ["text"] = "" } },
            new() { ["type"] = "content_block_delta", ["index"] = 0,
                ["delta"] = new JsonObject { ["type"] = "text_delta", ["text"] = text } },
            new() { ["type"] = "content_block_stop", ["index"] = 0 },
            new() { ["type"] = "message_delta", ["delta"] = new JsonObject { ["stop_reason"] = "end_turn" } },
            new() { ["type"] = "message_stop" }
        };
        return string.Concat(frames.Select(frame => "data: " + frame.ToJsonString() + "\n\n"));
    }
}
