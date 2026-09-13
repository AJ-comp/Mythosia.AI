using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Rag;
using Mythosia.AI.Services.DeepSeek;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class DeepSeekCurrentContractTests
{
    [TestMethod]
    [DataRow(ExecutionMode.Completion)]
    [DataRow(ExecutionMode.Callback)]
    [DataRow(ExecutionMode.Stream)]
    [DataRow(ExecutionMode.Run)]
    public async Task DefaultRequest_UsesFlashWithThinkingExplicitlyDisabled(ExecutionMode mode)
    {
        using var fixture = new Fixture();
        Assert.IsFalse(fixture.Service.ThinkingEnabled);
        Assert.AreEqual("final answer", await Execute(fixture.Service, "question", mode));
        var request = fixture.Requests.Single();
        Assert.AreEqual(AIModels.DeepSeek.Flash, request["model"]!.GetValue<string>());
        AssertThinking(request, enabled: false);
        Assert.AreEqual(8000u, request["max_tokens"]!.GetValue<uint>());
        Assert.IsFalse(request.ContainsKey("tools"));
        Assert.IsFalse(request.ContainsKey("tool_choice"));
        Assert.AreEqual("final answer", fixture.Service.ActivateChat.Messages.Last().Content);
    }

    [TestMethod]
    [DataRow(ExecutionMode.Completion)]
    [DataRow(ExecutionMode.Callback)]
    [DataRow(ExecutionMode.Stream)]
    [DataRow(ExecutionMode.Run)]
    public async Task ReasonerHelper_EnablesFlashThinkingAcrossTurnsAndCanBeDisabled(ExecutionMode mode)
    {
        using var fixture = new Fixture();
        Assert.AreSame(fixture.Service, fixture.Service.UseReasonerModel());
        Assert.IsTrue(fixture.Service.ThinkingEnabled);
        Assert.AreEqual(AIModels.DeepSeek.Flash, fixture.Service.Model);

        Assert.AreEqual("final answer", await Execute(fixture.Service, "first", mode));
        Assert.AreEqual("final answer", await Execute(fixture.Service, "second", mode));
        AssertThinking(fixture.Requests[0], enabled: true, effort: "high");
        AssertThinking(fixture.Requests[1], enabled: true, effort: "high");
        var priorAnswer = fixture.Requests[1]["messages"]!.AsArray().OfType<JsonObject>()
            .Single(message => message["role"]!.GetValue<string>() == "assistant");
        Assert.AreEqual("final answer", priorAnswer["content"]!.GetValue<string>());
        Assert.IsFalse(priorAnswer.ContainsKey("reasoning_content"),
            "Without tools, DeepSeek does not require replaying reasoning content.");
        Assert.IsFalse(fixture.Requests[1].ContainsKey("tools"));

        fixture.Service.ThinkingEnabled = false;
        await Execute(fixture.Service, "ordinary", mode);
        AssertThinking(fixture.Requests[2], enabled: false);
    }

    [TestMethod]
    [DataRow(AIRequestPurpose.Default, false)]
    [DataRow(AIRequestPurpose.Default, true)]
    [DataRow(AIRequestPurpose.QueryRewrite, false)]
    [DataRow(AIRequestPurpose.QueryRewrite, true)]
    [DataRow(AIRequestPurpose.Summarization, false)]
    [DataRow(AIRequestPurpose.Summarization, true)]
    public async Task DisableReasoningProfile_RestoresSettingsAfterSuccessOrFailure(AIRequestPurpose purpose, bool fail)
    {
        using var fixture = new Fixture();
        fixture.Service.UseReasonerModel();
        fixture.Handler.BeforeResponse = index =>
        {
            if (fail && index == 0) throw new HttpRequestException("Synthetic transport failure.");
        };
        var profile = new AIRequestProfile
        {
            Purpose = purpose,
            DisableReasoning = true,
            Stateless = true,
            MaxTokens = 128
        };
        if (fail)
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => fixture.Service.GetCompletionAsync("internal task", profile));
        else
            await fixture.Service.GetCompletionAsync("internal task", profile);

        AssertThinking(fixture.Requests[0], enabled: false);
        Assert.AreEqual(128u, fixture.Requests[0]["max_tokens"]!.GetValue<uint>());
        Assert.IsTrue(fixture.Service.ThinkingEnabled);
        Assert.IsFalse(fixture.Service.StatelessMode);
        Assert.AreEqual(8000u, fixture.Service.MaxTokens);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);

        await fixture.Service.GetCompletionAsync("normal task");
        AssertThinking(fixture.Requests[1], enabled: true, effort: "high");
        Assert.AreEqual(8000u, fixture.Requests[1]["max_tokens"]!.GetValue<uint>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RagQueryRewrite_DisablesThinkingOnlyForInternalRequest(bool useRun)
    {
        using var fixture = new Fixture();
        fixture.Handler.Answer = index => index == 0 ? "shipping\nKEYWORDS: shipping" : "final answer";
        fixture.Service.UseReasonerModel();
        var rag = fixture.Service.WithRag(builder => builder
            .AddText("shipping takes three days", id: "source")
            .UseLocalEmbedding(64).WithQueryRewriter());
        if (useRun)
        {
            await using var run = await rag.StartRunAsync("shipping");
            Assert.AreEqual("final answer", (await run.Result).Text);
        }
        else Assert.AreEqual("final answer", await rag.GetCompletionAsync("shipping"));

        Assert.AreEqual(2, fixture.Requests.Count);
        AssertThinking(fixture.Requests[0], enabled: false);
        AssertThinking(fixture.Requests[1], enabled: true, effort: "high");
        Assert.IsTrue(fixture.Service.ThinkingEnabled);
        await fixture.Service.GetCompletionAsync("plain follow-up");
        AssertThinking(fixture.Requests[2], enabled: true, effort: "high");
    }

    [TestMethod]
    public async Task TypedRepair_ReusesCapturedThinkingWhileNextRequestUsesNewSetting()
    {
        using var fixture = new Fixture();
        fixture.Service.ThinkingEnabled = true;
        fixture.Handler.Answer = index => index == 0 ? "not-json" : "{\"Value\":42}";
        fixture.Handler.BeforeResponse = index =>
        {
            if (index == 0) fixture.Service.ThinkingEnabled = false;
        };

        var result = await fixture.Service.GetCompletionAsync<TypedAnswer>("return a typed value");
        Assert.AreEqual(42, result.Value);
        Assert.AreEqual(2, fixture.Requests.Count);
        AssertThinking(fixture.Requests[0], enabled: true);
        AssertThinking(fixture.Requests[1], enabled: true);
        await fixture.Service.GetCompletionAsync("next logical request");
        AssertThinking(fixture.Requests[2], enabled: false);
    }

    [TestMethod]
    public async Task Run_CapturesThinkingBeforeAsynchronousStartup()
    {
        using var fixture = new Fixture();
        fixture.Service.ThinkingEnabled = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.BeforeRunSession = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var startup = fixture.Service.StartRunAsync("first");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Service.ThinkingEnabled = false;
        }
        finally { release.TrySetResult(); }
        await using var run = await startup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("final answer", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        AssertThinking(fixture.Requests[0], enabled: true);
        await fixture.Service.GetCompletionAsync("next");
        AssertThinking(fixture.Requests[1], enabled: false);
    }

    [TestMethod]
    public async Task ReasoningStream_SeparatesProviderReasoningFromAnswerText()
    {
        using var fixture = new Fixture();
        fixture.Service.UseReasonerModel();
        var events = new List<StreamingContent>();
        await foreach (var item in fixture.Service.StreamAsync("question", StreamOptions.FullOptions)) events.Add(item);
        Assert.AreEqual("final answer", string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        Assert.AreEqual("provider reasoning", string.Concat(events.Where(item => item.Type == StreamingContentType.Reasoning).Select(item => item.Content)));
        Assert.AreEqual("final answer", fixture.Service.ActivateChat.Messages.Last().Content);
    }

    [TestMethod]
    public async Task HostedSearch_RemainsUnsupportedBeforeHistoryOrTransport()
    {
        using var fixture = new Fixture();
        fixture.Service.WithWebSearch();
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("search"));
        Assert.AreEqual(0, fixture.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
        await fixture.Service.GetCompletionAsync("ordinary");
        AssertThinking(fixture.Requests.Single(), enabled: false);
    }

    private static void AssertThinking(JsonObject request, bool enabled, string? effort = null)
    {
        Assert.AreEqual(enabled ? "enabled" : "disabled", request["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual(effort, request["reasoning_effort"]?.GetValue<string>());
        if (effort == null) Assert.IsFalse(request.ContainsKey("reasoning_effort"), "Auto relies on the provider's default effort when thinking is enabled.");
    }

    private static async Task<string> Execute(DeepSeekService service, string prompt, ExecutionMode mode)
    {
        if (mode == ExecutionMode.Completion) return await service.GetCompletionAsync(prompt);
        if (mode == ExecutionMode.Run)
        {
            await using var run = await service.StartRunAsync(prompt);
            return (await run.Result).Text;
        }
        var text = new StringBuilder();
        if (mode == ExecutionMode.Callback)
            await service.StreamCompletionAsync(prompt, chunk => { text.Append(chunk); return Task.CompletedTask; });
        else
            await foreach (var chunk in service.StreamAsync(prompt)) text.Append(chunk);
        return text.ToString();
    }

    public enum ExecutionMode { Completion, Callback, Stream, Run }
    public sealed class TypedAnswer { public int Value { get; set; } }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _client;
        public RecordingHandler Handler { get; } = new();
        public List<JsonObject> Requests => Handler.Requests;
        public Probe Service { get; }
        public Fixture()
        {
            _client = new HttpClient(Handler);
            Service = new Probe(_client);
        }
        public void Dispose() => _client.Dispose();
    }

    private sealed class Probe(HttpClient client) : DeepSeekService("offline-test-key", client)
    {
        public Func<CancellationToken, Task>? BeforeRunSession { get; set; }
        protected override async Task<RunSession> CreateRunSessionAsync(Message message, StreamOptions executionOptions,
            AIRequestContext? context, CancellationToken cancellationToken)
        {
            if (BeforeRunSession != null) await BeforeRunSession(cancellationToken);
            return await base.CreateRunSessionAsync(message, executionOptions, context, cancellationToken);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = new();
        public Action<int>? BeforeResponse { get; set; }
        public Func<int, string> Answer { get; set; } = _ => "final answer";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual("/chat/completions", request.RequestUri!.AbsolutePath);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var index = Requests.Count;
            Requests.Add(body);
            BeforeResponse?.Invoke(index);
            var answer = Answer(index);
            if (body["stream"]?.GetValue<bool>() == true)
            {
                var chunks = new[]
                {
                    "{\"choices\":[{\"delta\":{\"reasoning_content\":\"provider reasoning\"},\"finish_reason\":null}]}",
                    JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = answer }, finish_reason = (string?)null } } }),
                    "{\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}"
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(string.Concat(chunks.Select(chunk => $"data: {chunk}\n\n")) + "data: [DONE]\n\n", Encoding.UTF8, "text/event-stream")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { role = "assistant", reasoning_content = "provider reasoning", content = answer }, finish_reason = "stop" } }
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
