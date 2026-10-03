using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class ProviderTransformRetryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ContextRecovery_ProviderReplacementRetainsInitialRequestAttachments(bool transform)
    {
        using var fixture = new Fixture(transform);
        var service = fixture.Service;
        var originalMessages = service.ActivateChat.Messages.ToArray();
        service.WithTurnInstruction("REQUEST_INSTRUCTION");
        fixture.BeforeResponse = index =>
        {
            if (index == 3 && transform)
                fixture.Transforming!.LastReplacement!.Content = "CALLER_MUTATED_RETRY";
        };

        Assert.AreEqual("answer", await service.CreateRequest("input").WithMaxTokens(1111)
            .WithContext(Context()).GetCompletionAsync());

        Assert.HasCount(3, fixture.Requests, "One rejection, one summary and one retry are expected.");
        foreach (var index in new[] { 0, 2 })
        {
            var request = fixture.Requests[index];
            Assert.AreEqual(1111u, request["max_tokens"]!.GetValue<uint>());
            var messages = request["messages"]!.AsArray();
            Assert.AreEqual(1, Occurrences(messages, "INPUT_OVERRIDE"));
            Assert.AreEqual(1, Occurrences(messages, "ADDITIONAL_CONTEXT"));
            Assert.AreEqual(1, Occurrences(messages, "REQUEST_INSTRUCTION"));
        }
        var summary = fixture.Requests[1].ToJsonString();
        Assert.IsFalse(summary.Contains("INPUT_OVERRIDE", StringComparison.Ordinal));
        Assert.IsFalse(summary.Contains("ADDITIONAL_CONTEXT", StringComparison.Ordinal));
        Assert.IsFalse(summary.Contains("REQUEST_INSTRUCTION", StringComparison.Ordinal));
        Assert.HasCount(4, service.ActivateChat.Messages);
        Assert.AreSame(originalMessages[4], service.ActivateChat.Messages[0]);
        Assert.AreSame(originalMessages[5], service.ActivateChat.Messages[1]);
        Assert.AreEqual(transform ? "TRANSFORM 2 input" : "input", service.ActivateChat.Messages[2].Content);
        if (transform) Assert.AreNotSame(fixture.Transforming!.LastReplacement, service.ActivateChat.Messages[2]);

        await service.GetInputTokenCountAsync();
        var retained = fixture.Requests[^1]["messages"]!.AsArray();
        Assert.AreEqual(1, Occurrences(retained, "INPUT_OVERRIDE"));
        Assert.AreEqual(1, Occurrences(retained, "ADDITIONAL_CONTEXT"));
        Assert.AreEqual(1, Occurrences(retained, "REQUEST_INSTRUCTION"));
        Assert.IsFalse(retained.ToJsonString().Contains("CALLER_MUTATED_RETRY", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("failure")]
    [DataRow("cancel")]
    public async Task ContextRecovery_FailedSummaryDoesNotLeaveTransformedInputOrAttachments(string outcome)
    {
        using var fixture = new Fixture(transform: true);
        using var cancellation = new CancellationTokenSource();
        var service = fixture.Service;
        var before = service.ActivateChat.Messages.ToArray();
        service.WithTurnInstruction("REQUEST_INSTRUCTION");
        fixture.BeforeResponse = index =>
        {
            if (index != 2) return;
            if (outcome == "failure") throw new HttpRequestException("Synthetic summary failure.");
            cancellation.Cancel();
        };

        var request = service.CreateRequest("input").WithMaxTokens(1111).WithContext(Context());
        if (outcome == "failure")
        {
            var error = await Assert.ThrowsAsync<ContextLengthExceededException>(() => request.GetCompletionAsync(cancellation.Token));
            Assert.AreEqual("compaction-threw", error.RecoverySkipReason);
        }
        else
            await Assert.ThrowsAsync<OperationCanceledException>(() => request.GetCompletionAsync(cancellation.Token));

        Assert.HasCount(2, fixture.Requests);
        CollectionAssert.AreEqual(before, service.ActivateChat.Messages.ToArray());
        Assert.IsNull(service.ConversationPolicy!.CurrentSummary);
        fixture.BeforeResponse = null;
        service.ConversationPolicy = null;
        await service.GetCompletionAsync("unrelated next request");
        var next = fixture.Requests[^1];
        Assert.AreEqual(3333u, next["max_tokens"]!.GetValue<uint>());
        foreach (var attachment in new[] { "INPUT_OVERRIDE", "ADDITIONAL_CONTEXT", "REQUEST_INSTRUCTION" })
            Assert.IsFalse(next.ToJsonString().Contains(attachment, StringComparison.Ordinal));
    }

    private static AIRequestContext Context() => new()
    {
        RequestMessageOverride = new Message(ActorRole.User, "INPUT_OVERRIDE"),
        AdditionalMessages = new[] { new Message(ActorRole.User, "ADDITIONAL_CONTEXT") }
    };

    private static int Occurrences(JsonArray messages, string value) => messages.Count(message =>
        message?["content"] is JsonValue content && content.TryGetValue<string>(out var text) && text == value);

    private sealed class TransformingService(HttpClient http) : AnthropicService("offline", "claude-sonnet-5-5", http)
    {
        private int _attempt;
        public Message? LastReplacement { get; private set; }
        public override Task<string> GetCompletionAsync(Message message)
        {
            if (RequestStatelessMode) return base.GetCompletionAsync(message);
            LastReplacement = new Message(ActorRole.User, $"TRANSFORM {++_attempt} {message.Content}");
            return base.GetCompletionAsync(LastReplacement);
        }
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _http;
        public AnthropicService Service { get; }
        public TransformingService? Transforming => Service as TransformingService;
        public List<JsonObject> Requests { get; } = [];
        public Action<int>? BeforeResponse { get; set; }
        public Fixture(bool transform)
        {
            _http = new HttpClient(this, disposeHandler: false);
            Service = transform ? new TransformingService(_http)
                : new AnthropicService("offline", "claude-sonnet-5-5", _http);
            Service.MaxTokens = 3333;
            Service.ContextRecoveryMaxRetries = 1;
            Service.ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 100, KeepRecentCount = 2 };
            for (var index = 0; index < 6; index++)
                Service.ActivateChat.Messages.Add(new Message(index % 2 == 0 ? ActorRole.User : ActorRole.Assistant, "history " + index));
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
            BeforeResponse?.Invoke(Requests.Count);
            var first = Requests.Count == 1;
            var count = request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal);
            var response = first
                ? """{"type":"error","error":{"type":"invalid_request_error","message":"prompt is too long: 1000 tokens > 500 maximum"}}"""
                : count ? "{\"input_tokens\":7}"
                : """{"id":"answer","content":[{"type":"text","text":"answer"}],"stop_reason":"end_turn"}""";
            return new HttpResponseMessage(first ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }
}
