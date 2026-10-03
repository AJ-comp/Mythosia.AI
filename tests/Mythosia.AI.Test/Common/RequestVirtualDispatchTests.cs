using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class RequestVirtualDispatchTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CallbackAdapter_CancellationReachesTheProvider(bool runEntry)
    {
        using var http = new HttpClient();
        using var cancellation = new CancellationTokenSource();
        var service = new CallbackProvider(http);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.BeforeObservation = async _ =>
        {
            var token = service.ExecutingCancellationToken;
            arrived.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { stopped.TrySetResult(); }
        };
        async Task Send()
        {
            if (runEntry)
            {
                await using var run = await service.StartRunAsync("input", cancellationToken: cancellation.Token);
                await run.Result;
            }
            else await foreach (var _ in service.StreamAsync("input", cancellation.Token)) { }
        }
        var request = Send();
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsEmpty(service.Observed);
    }

    [TestMethod]
    [DataRow("completion")]
    [DataRow("stream")]
    [DataRow("run")]
    [DataRow("builder-run")]
    public async Task CallbackProvider_DefaultRoundAdapterRetainsPreparedSettingsAndFeatures(string entry)
    {
        using var http = new HttpClient();
        var service = new CallbackProvider(http) { MaxTokens = 3333 };
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = ["wanted.example"] });
        var context = new AIRequestContext { SystemMessageSuffix = "REQUEST_CONTEXT" };
        if (entry == "completion")
            await service.CreateRequest("input").WithMaxTokens(1111).WithContext(context).GetCompletionAsync();
        else if (entry == "stream")
        {
            await foreach (var _ in service.StreamAsync(new Message(ActorRole.User, "input"), context)) { }
        }
        else if (entry == "run")
        {
            await using var run = await service.StartRunAsync("input", context: context);
            Assert.AreEqual("ok", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        }
        else
        {
            await using var run = await service.CreateRequest("input").WithMaxTokens(1111).WithContext(context).StartRunAsync();
            Assert.AreEqual("ok", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        }
        var observed = Assert.ContainsSingle(service.Observed);
        Assert.AreEqual(entry is "completion" or "builder-run" ? 1111u : 3333u, observed.MaxTokens);
        CollectionAssert.AreEqual(new[] { "wanted.example" }, observed.Domains!);
        StringAssert.Contains(observed.System, "REQUEST_CONTEXT");
        Assert.AreEqual(3333u, service.MaxTokens);
    }

    [TestMethod]
    [DataRow("string")]
    [DataRow("message")]
    [DataRow("builder")]
    public async Task AsyncMessageOverride_CanReplaceInputWithoutLosingCapturedConfiguration(string entry)
    {
        using var http = new HttpClient();
        var service = new TransformingProvider(http) { MaxTokens = 3333 };
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = ["wanted.example"] });
        var context = new AIRequestContext { RequestMessageOverride = new Message(ActorRole.User, "OVERRIDE") };
        if (entry == "string") await service.GetCompletionAsync("input", context: context);
        else if (entry == "message") await service.GetCompletionAsync(new Message(ActorRole.User, "input"), context: context);
        else await service.CreateRequest("input").WithMaxTokens(1111).WithContext(context).GetCompletionAsync();

        var observed = Assert.ContainsSingle(service.Observed);
        Assert.AreEqual("PREFIX input", observed.Input.Content);
        Assert.AreEqual(entry == "builder" ? 1111u : 3333u, observed.MaxTokens);
        CollectionAssert.AreEqual(new[] { "wanted.example" }, observed.Domains!);
        CollectionAssert.AreEqual(new[] { "OVERRIDE" }, observed.Wire);
        Assert.AreNotSame(service.LastReplacement, observed.Input);
        Assert.AreEqual(3333u, service.MaxTokens);
        await service.GetCompletionAsync("next");
        Assert.IsNull(service.Observed[1].Domains);
    }

    [TestMethod]
    public async Task MessageReplacement_OwnsItsPayloadBeforeProviderWorkAwaits()
    {
        using var http = new HttpClient();
        var service = new TransformingProvider(http) { ReplacementBytes = [1, 2, 3] };
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.BeforeObservation = async _ => { arrived.TrySetResult(); await release.Task; };
        var completion = service.CreateRequest("input").GetCompletionAsync();
        try
        {
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.LastReplacement!.Content = "MUTATED";
            service.ReplacementBytes![0] = 99;
            ((ImageContent)service.LastReplacement.Contents[1]).Data![1] = 88;
        }
        finally { release.TrySetResult(); }
        await completion;
        var retained = Assert.ContainsSingle(service.Observed).Input;
        Assert.AreEqual("PREFIX input", retained.Content);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ((ImageContent)retained.Contents[1]).Data!);
    }

    [TestMethod]
    [DataRow("string")]
    [DataRow("explicit-message")]
    [DataRow("builder")]
    public async Task OverrideHelper_HasIndependentSettings_AndDoesNotConsumeOuterHandoff(string helperEntry)
    {
        using var http = new HttpClient();
        var service = new TransformingProvider(http)
        {
            MaxTokens = 3333, RunHelper = true, HelperEntry = helperEntry,
            DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 9 }
        };
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = ["outer.example"] });
        await service.CreateRequest("outer").WithMaxTokens(1111).WithMaxRounds(2).GetCompletionAsync();

        Assert.HasCount(2, service.Observed);
        var child = service.Observed[0];
        Assert.AreEqual(3333u, child.MaxTokens);
        Assert.AreEqual(9, child.MaxRounds);
        Assert.IsNull(child.Domains);
        var parent = service.Observed[1];
        Assert.AreEqual("PREFIX outer", parent.Input.Content);
        Assert.AreEqual(1111u, parent.MaxTokens);
        Assert.AreEqual(2, parent.MaxRounds);
        CollectionAssert.AreEqual(new[] { "outer.example" }, parent.Domains!);
        Assert.AreEqual(3333u, service.MaxTokens);
        Assert.AreEqual(9, service.DefaultPolicy.MaxRounds);
    }

    [TestMethod]
    [DataRow("validation")]
    [DataRow("failure")]
    [DataRow("cancel")]
    public async Task TransformedRequest_FailureRestoresStateAndDoesNotLeakConsumedOptions(string outcome)
    {
        using var http = new HttpClient();
        using var cancellation = new CancellationTokenSource();
        var service = new TransformingProvider(http) { MaxTokens = 3333, RejectReplacement = outcome == "validation" };
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = ["failed.example"] });
        service.BeforeObservation = _ =>
        {
            if (outcome == "failure") throw new HttpRequestException("Synthetic provider failure");
            if (outcome == "cancel") { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            return Task.CompletedTask;
        };
        var request = service.CreateRequest("input").WithMaxTokens(1111);
        if (outcome == "validation")
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => request.GetCompletionAsync(cancellation.Token));
        else if (outcome == "failure")
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => request.GetCompletionAsync(cancellation.Token));
        else
            await Assert.ThrowsAsync<OperationCanceledException>(() => request.GetCompletionAsync(cancellation.Token));
        Assert.IsEmpty(service.Observed);
        if (outcome == "validation") Assert.IsEmpty(service.ActivateChat.Messages);
        Assert.AreEqual(3333u, service.MaxTokens);
        service.RejectReplacement = false;
        service.BeforeObservation = null;
        await service.GetCompletionAsync("next");
        var followup = Assert.ContainsSingle(service.Observed);
        Assert.AreEqual(3333u, followup.MaxTokens);
        Assert.IsNull(followup.Domains);
    }

    [TestMethod]
    public async Task TransformingStructuredRepair_PreservesOriginalContextAnchorAndSchema()
    {
        using var http = new HttpClient();
        var service = new TransformingProvider(http) { StructuredOutputMaxRetries = 1 };
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = ["structured.example"] });
        service.WithSystemMessageProvider(() => new AIRequestContext
        {
            RequestMessageOverride = new Message(ActorRole.User, "ORIGINAL_OVERRIDE")
        });
        service.Response = count => count == 1 ? "invalid-json" : "{\"Value\":7}";

        var result = await service.GetCompletionAsync<Answer>("input");

        Assert.AreEqual(7, result.Value);
        Assert.HasCount(2, service.Observed);
        foreach (var observed in service.Observed)
        {
            CollectionAssert.AreEqual(new[] { "structured.example" }, observed.Domains!);
            Assert.IsTrue(observed.HasSchema);
            Assert.AreEqual("ORIGINAL_OVERRIDE", observed.Wire[0]);
        }
        Assert.HasCount(2, service.Observed[1].Wire);
        StringAssert.Contains(service.Observed[1].Wire[1], "[STRUCTURED OUTPUT CORRECTION]");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TransformingStreamingOverrideRetainsCapturedSettingsAndInputContext(bool runEntry)
    {
        using var http = new HttpClient();
        var service = new TransformingStreamProvider(http) { MaxTokens = 3333 };
        var request = service.CreateRequest("input").WithMaxTokens(1111)
            .WithWebSearch(new WebSearchOptions { AllowedDomains = ["stream.example"] })
            .WithContext(new AIRequestContext { RequestMessageOverride = new Message(ActorRole.User, "STREAM_OVERRIDE") });
        if (runEntry)
        {
            await using var run = await request.StartRunAsync();
            Assert.AreEqual("ok", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        }
        else
        {
            var text = new System.Text.StringBuilder();
            await foreach (var chunk in request.StreamAsync()) text.Append(chunk);
            Assert.AreEqual("ok", text.ToString());
        }
        var observed = Assert.ContainsSingle(service.Observed);
        Assert.AreEqual("STREAM_PREFIX input", observed.Input.Content);
        Assert.AreEqual(1111u, observed.MaxTokens);
        CollectionAssert.AreEqual(new[] { "stream.example" }, observed.Domains!);
        CollectionAssert.AreEqual(new[] { "STREAM_OVERRIDE" }, observed.Wire);
    }

    public sealed class Answer { public int Value { get; set; } }
    private sealed record Observation(Message Input, uint MaxTokens, int MaxRounds, string[]? Domains,
        string[] Wire, string System, bool HasSchema);

    private class CallbackProvider(HttpClient http) : AIService("offline", "https://offline.invalid/", http)
    {
        public override string Provider => "SyntheticCallback";
        public CancellationToken ExecutingCancellationToken => RequestCancellationToken;
        public List<Observation> Observed { get; } = [];
        public Func<Message, Task>? BeforeObservation { get; set; }
        public Func<int, string> Response { get; set; } = _ => "ok";
        protected override void ValidateRequestFeatures(AIRequestFeatures features) { }
        protected async Task<string> ObserveAsync(Message message)
        {
            if (BeforeObservation != null) await BeforeObservation(message);
            RequestCancellationToken.ThrowIfCancellationRequested();
            Observed.Add(new Observation(message, RequestMaxTokens, GetExecutionPolicy().MaxRounds,
                CurrentRequestFeatures.WebSearch?.AllowedDomains?.ToArray(),
                GetLatestMessages().Select(item => item.Content).ToArray(),
                GetEffectiveSystemMessageWithRequestContext(), RequestStructuredOutputSchemaJson != null));
            return Response(Observed.Count);
        }
        public override async Task<string> GetCompletionAsync(Message message)
        {
            using var settings = BeginRequestSettingsScope();
            using var features = BeginRequestFeaturesScope(message);
            message = ResolveRequestMessage(message);
            ActivateChat.Messages.Add(message);
            RecordRequestInput(message);
            return await ObserveAsync(message);
        }
        public override async Task StreamCompletionAsync(Message message, Func<string, Task> received)
        {
            using var settings = BeginRequestSettingsScope();
            using var features = BeginRequestFeaturesScope(message);
            message = ResolveRequestMessage(message);
            await received(await ObserveAsync(message));
        }
        protected override HttpRequestMessage CreateMessageRequest() => throw new AssertFailedException("No HTTP expected.");
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new AssertFailedException("No HTTP expected.");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => throw new AssertFailedException("No HTTP expected.");
        protected override string ExtractResponseContent(string response) => throw new AssertFailedException("No HTTP expected.");
        protected override string StreamParseJson(string json) => throw new AssertFailedException("No HTTP expected.");
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
    }

    private sealed class TransformingProvider(HttpClient http) : CallbackProvider(http)
    {
        public Message? LastReplacement { get; private set; }
        public byte[]? ReplacementBytes { get; init; }
        public bool RunHelper { get; init; }
        public string HelperEntry { get; init; } = "string";
        public bool RejectReplacement { get; set; }
        private bool _helperEntered;
        public override async Task<string> GetCompletionAsync(Message message, AIRequestProfile? profile = null,
            AIRequestContext? context = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (RunHelper && !_helperEntered)
            {
                _helperEntered = true;
                if (HelperEntry == "explicit-message")
                {
                    using var independent = BeginIndependentRequestScope();
                    await base.GetCompletionAsync(new Message(ActorRole.User, "helper"), cancellationToken: cancellationToken);
                }
                else if (HelperEntry == "builder") await CreateRequest("helper").GetCompletionAsync(cancellationToken);
                else await base.GetCompletionAsync("helper", cancellationToken: cancellationToken);
            }
            var text = RejectReplacement ? "REJECTED_REPLACEMENT" : "PREFIX " + message.Content;
            LastReplacement = ReplacementBytes == null
                ? new Message(ActorRole.User, text)
                : new Message(ActorRole.User, [new TextContent(text), new ImageContent(ReplacementBytes, "image/png")]);
            return await base.GetCompletionAsync(LastReplacement, profile, context, cancellationToken);
        }
        protected override void ValidateProviderRequestOptions(object? options, Message message)
        {
            if (message.Content == "REJECTED_REPLACEMENT")
                throw new ArgumentException("Synthetic replacement input validation failure.");
        }
    }

    private sealed class TransformingStreamProvider(HttpClient http) : CallbackProvider(http)
    {
        public override async IAsyncEnumerable<StreamingContent> StreamAsync(Message message, StreamOptions options,
            AIRequestContext? context = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var replacement = new Message(ActorRole.User, "STREAM_PREFIX " + message.Content);
            await foreach (var item in base.StreamAsync(replacement, options, context, cancellationToken)) yield return item;
        }
    }
}
