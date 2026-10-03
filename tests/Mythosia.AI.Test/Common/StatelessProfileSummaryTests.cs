using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("SummaryPolicy")]
public class StatelessProfileSummaryTests
{
    private const string ParentSummary = "PRIVATE_PARENT_SUMMARY";
    private const string HelperPrompt = "INDEPENDENT_HELPER_REQUEST";

    [TestMethod]
    [DataRow(false, "direct")]
    [DataRow(false, "string")]
    [DataRow(false, "message")]
    [DataRow(false, "builder")]
    [DataRow(false, "stream-builder")]
    [DataRow(true, "direct")]
    [DataRow(true, "string")]
    [DataRow(true, "message")]
    [DataRow(true, "builder")]
    [DataRow(true, "stream-builder")]
    public async Task StatelessRequests_DoNotCompactParentConversation(bool anthropic, string path)
    {
        using var handler = new RecordingHandler(anthropic);
        using var http = new HttpClient(handler);
        var service = CreateService(anthropic, http);
        service.StatelessMode = path == "direct";
        var originalChat = service.ActivateChat;
        var originalMessages = originalChat.Messages.ToArray();
        var originalJson = JsonSerializer.Serialize(originalMessages);
        var originalPolicy = service.ConversationPolicy;

        var result = await SendHelperAsync(service, path);

        Assert.AreEqual("answer", result);
        Assert.AreEqual(1, handler.Requests.Count, "A stateless helper must not issue a summary request.");
        AssertHelperIsolation(handler.Requests[0]);
        Assert.AreSame(originalChat, service.ActivateChat);
        CollectionAssert.AreEqual(originalMessages, originalChat.Messages.ToArray());
        Assert.AreEqual(originalJson, JsonSerializer.Serialize(originalChat.Messages));
        Assert.AreSame(originalPolicy, service.ConversationPolicy);
        Assert.AreEqual(ParentSummary, originalPolicy!.CurrentSummary);
        Assert.AreEqual(path == "direct", service.StatelessMode);
        Assert.AreEqual(0.7f, service.Temperature);
        Assert.AreEqual(1024u, service.MaxTokens);
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public async Task StatefulRequests_CompactBeforeSending_RespectProfileOverride(
        bool anthropic, bool useBuilder, bool serviceStateless)
    {
        using var handler = new RecordingHandler(anthropic);
        using var http = new HttpClient(handler);
        var service = CreateService(anthropic, http);
        service.StatelessMode = serviceStateless;
        var originalChat = service.ActivateChat;
        var retained = originalChat.Messages.Skip(2).ToArray();
        var profile = new AIRequestProfile { Stateless = false, Temperature = 0.3f, MaxTokens = 512 };

        var result = useBuilder
            ? await service.CreateRequest("continue parent").WithProfile(profile).GetCompletionAsync()
            : await service.GetCompletionAsync("continue parent", profile);

        Assert.AreEqual("answer", result);
        Assert.AreEqual(2, handler.Requests.Count, "Expected one summary and one stateful completion.");
        StringAssert.Contains(handler.Requests[0], "[New messages to incorporate]");
        StringAssert.Contains(handler.Requests[0], "parent-0");
        StringAssert.Contains(handler.Requests[1], "continue parent");
        StringAssert.Contains(handler.Requests[1], "parent-2");
        Assert.IsFalse(handler.Requests[1].Contains("parent-0", StringComparison.Ordinal));
        using var request = JsonDocument.Parse(handler.Requests[1]);
        if (!anthropic)
            Assert.AreEqual(0.3f, request.RootElement.GetProperty("temperature").GetSingle());
        Assert.AreEqual(512u, request.RootElement.GetProperty("max_tokens").GetUInt32());
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreEqual(4, originalChat.Messages.Count);
        Assert.AreSame(retained[0], originalChat.Messages[0]);
        Assert.AreSame(retained[1], originalChat.Messages[1]);
        Assert.AreEqual("answer", service.ConversationPolicy!.CurrentSummary);
        Assert.AreEqual(serviceStateless, service.StatelessMode);
        Assert.AreEqual(0.7f, service.Temperature);
        Assert.AreEqual(1024u, service.MaxTokens);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ProfileWithoutStatelessOverride_UsesServiceSetting(bool anthropic, bool stateless)
    {
        using var handler = new RecordingHandler(anthropic);
        using var http = new HttpClient(handler);
        var service = CreateService(anthropic, http);
        service.StatelessMode = stateless;
        var before = JsonSerializer.Serialize(service.ActivateChat.Messages);

        await service.GetCompletionAsync(HelperPrompt, new AIRequestProfile { Temperature = 0.3f });

        Assert.AreEqual(stateless ? 1 : 2, handler.Requests.Count);
        Assert.AreEqual(stateless ? ParentSummary : "answer", service.ConversationPolicy!.CurrentSummary);
        if (stateless)
        {
            AssertHelperIsolation(handler.Requests[0]);
            Assert.AreEqual(before, JsonSerializer.Serialize(service.ActivateChat.Messages));
        }
        Assert.AreEqual(stateless, service.StatelessMode);
    }

    [TestMethod]
    [DataRow(false, "string", false)]
    [DataRow(false, "string", true)]
    [DataRow(false, "message", false)]
    [DataRow(false, "message", true)]
    [DataRow(false, "builder", false)]
    [DataRow(false, "builder", true)]
    [DataRow(true, "string", false)]
    [DataRow(true, "string", true)]
    [DataRow(true, "message", false)]
    [DataRow(true, "message", true)]
    [DataRow(true, "builder", false)]
    [DataRow(true, "builder", true)]
    public async Task FailedStatelessRequest_RestoresParentAndRequestSettings(
        bool anthropic, string path, bool cancel)
    {
        using var handler = new RecordingHandler(anthropic);
        using var http = new HttpClient(handler);
        using var cts = new CancellationTokenSource();
        var service = CreateService(anthropic, http);
        var originalChat = service.ActivateChat;
        var originalJson = JsonSerializer.Serialize(originalChat.Messages);
        handler.BeforeResponse = token =>
        {
            if (cancel)
            {
                cts.Cancel();
                token.ThrowIfCancellationRequested();
            }
            throw new InvalidOperationException("synthetic transport failure");
        };

        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(() => SendHelperAsync(service, path, cts.Token));
        else
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => SendHelperAsync(service, path));

        Assert.AreEqual(1, handler.Requests.Count);
        AssertHelperIsolation(handler.Requests[0]);
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreEqual(originalJson, JsonSerializer.Serialize(originalChat.Messages));
        Assert.AreEqual(ParentSummary, service.ConversationPolicy!.CurrentSummary);
        Assert.IsFalse(service.StatelessMode);

        handler.BeforeResponse = null;
        service.ConversationPolicy = null;
        await service.GetCompletionAsync("resume parent");

        Assert.AreEqual(2, handler.Requests.Count);
        StringAssert.Contains(handler.Requests[1], "parent-0");
        Assert.IsFalse(handler.Requests[1].Contains(HelperPrompt, StringComparison.Ordinal));
        Assert.AreEqual(6, originalChat.Messages.Count);
        using var resumed = JsonDocument.Parse(handler.Requests[1]);
        if (!anthropic)
            Assert.AreEqual(0.7f, resumed.RootElement.GetProperty("temperature").GetSingle());
        Assert.AreEqual(1024u, resumed.RootElement.GetProperty("max_tokens").GetUInt32());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedStatefulProfileSummary_PreservesHistoryAndRestoresStatelessDefault(bool cancel)
    {
        using var handler = new RecordingHandler(false);
        using var http = new HttpClient(handler);
        using var cts = new CancellationTokenSource();
        var service = CreateService(false, http);
        service.StatelessMode = true;
        var originalChat = service.ActivateChat;
        var originalJson = JsonSerializer.Serialize(originalChat.Messages);
        handler.BeforeResponse = token =>
        {
            if (cancel)
            {
                cts.Cancel();
                token.ThrowIfCancellationRequested();
            }
            throw new InvalidOperationException("synthetic summary failure");
        };

        Task<string> Send() => service.GetCompletionAsync("stateful turn", new AIRequestProfile { Stateless = false }, cancellationToken: cts.Token);
        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(Send);
        else
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(Send);

        Assert.AreEqual(1, handler.Requests.Count);
        StringAssert.Contains(handler.Requests[0], "[New messages to incorporate]");
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreEqual(originalJson, JsonSerializer.Serialize(originalChat.Messages));
        Assert.AreEqual(ParentSummary, service.ConversationPolicy!.CurrentSummary);
        Assert.IsTrue(service.StatelessMode);

        handler.BeforeResponse = null;
        await service.GetCompletionAsync(HelperPrompt);
        Assert.AreEqual(2, handler.Requests.Count);
        AssertHelperIsolation(handler.Requests[1]);
        Assert.AreEqual(originalJson, JsonSerializer.Serialize(originalChat.Messages));

        // A later stateful request still compacts: failure must also release the summary guard.
        await service.GetCompletionAsync("retry stateful turn", new AIRequestProfile { Stateless = false });
        Assert.AreEqual(4, handler.Requests.Count);
        Assert.AreEqual("answer", service.ConversationPolicy.CurrentSummary);
    }

    private static AIService CreateService(bool anthropic, HttpClient http)
    {
        AIService service = anthropic
            ? new AnthropicService("offline-key", AIModels.Anthropic.ClaudeSonnet5_5, http)
            : new OpenAIService("offline-key", AIModels.OpenAI.Gpt4o, http);
        service.MaxTokens = 1024;
        service.ConversationPolicy = new SummaryConversationPolicy
        {
            TriggerCount = 2, KeepRecentCount = 2, CurrentSummary = ParentSummary
        };
        for (var i = 0; i < 4; i++)
            service.ActivateChat.Messages.Add(new Message(i % 2 == 0 ? ActorRole.User : ActorRole.Assistant, $"parent-{i}"));
        return service;
    }

    private static async Task<string> SendHelperAsync(AIService service, string path, CancellationToken token = default)
    {
        switch (path)
        {
            case "direct":
                return await service.GetCompletionAsync(HelperPrompt, cancellationToken: token);
            case "string":
                return await service.GetCompletionAsync(HelperPrompt, RequestProfiles.QueryRewrite, cancellationToken: token);
            case "message":
                return await service.GetCompletionAsync(new Message(ActorRole.User, HelperPrompt), RequestProfiles.QueryRewrite, cancellationToken: token);
            case "builder":
                return await service.CreateRequest(HelperPrompt).WithProfile(RequestProfiles.QueryRewrite).GetCompletionAsync(token);
            case "stream-builder":
                var result = new StringBuilder();
                await foreach (var chunk in service.CreateRequest(HelperPrompt).WithProfile(RequestProfiles.QueryRewrite).StreamAsync(token))
                    result.Append(chunk);
                return result.ToString();
            default:
                throw new ArgumentOutOfRangeException(nameof(path));
        }
    }

    private static void AssertHelperIsolation(string body)
    {
        StringAssert.Contains(body, HelperPrompt);
        Assert.IsFalse(body.Contains("parent-", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains(ParentSummary, StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("[Conversation to summarize]", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("[New messages to incorporate]", StringComparison.Ordinal));
    }

    private sealed class RecordingHandler(bool anthropic) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        public Action<CancellationToken>? BeforeResponse { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add(body);
            BeforeResponse?.Invoke(cancellationToken);
            using var parsed = JsonDocument.Parse(body);
            var streaming = parsed.RootElement.TryGetProperty("stream", out var stream) && stream.GetBoolean();
            var json = anthropic
                ? """{"id":"reply","type":"message","role":"assistant","content":[{"type":"text","text":"answer"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}"""
                : """{"id":"reply","choices":[{"index":0,"message":{"role":"assistant","content":"answer"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""";
            var sse = anthropic
                ? "data: {\"type\":\"message_start\",\"message\":{\"id\":\"reply\",\"role\":\"assistant\",\"content\":[],\"usage\":{\"input_tokens\":1,\"output_tokens\":0}}}\n\n"
                    + "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n"
                    + "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"answer\"}}\n\n"
                    + "data: {\"type\":\"content_block_stop\",\"index\":0}\n\n"
                    + "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":1}}\n\n"
                    + "data: {\"type\":\"message_stop\"}\n\n"
                : "data: {\"id\":\"reply\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"answer\"},\"finish_reason\":null}]}\n\n"
                    + "data: {\"id\":\"reply\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n"
                    + "data: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(streaming ? sse : json, Encoding.UTF8, streaming ? "text/event-stream" : "application/json")
            };
        }
    }
}
