using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AIRunTests
{
    [TestMethod]
    public async Task StartRun_OneExecutionFeedsCallbackStreamAndResult()
    {
        var release = Signal();
        var service = new RunTestService { BlockUntil = release.Task, Chunks = new[] { "hello", " world" } };
        var callback = new List<string>();
        await using var run = await service.StartRunAsync("prompt", callback.Add);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(run.Result.IsCompleted);
        var outputTask = CollectAsync(run);
        release.SetResult();
        var events = await outputTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("hello world", (await run.Result).Text);
        Assert.AreEqual("hello world", string.Concat(callback));
        Assert.AreEqual("hello world", string.Concat(events.Where(e => e.Type == StreamingContentType.Text).Select(e => e.Content)));
        Assert.AreEqual(1, events.Count(e => e.Type == StreamingContentType.Completion));
        Assert.AreEqual(1, service.RoundCount);
        Assert.IsTrue(service.SessionDisposed);
    }

    [TestMethod]
    public async Task FinalOnly_OverflowDoesNotBlockResultOrCallback_ReaderFailsExplicitly()
    {
        var callbackCount = 0;
        var service = new RunTestService { Chunks = Enumerable.Repeat("x", 1500).ToArray() };
        await using var run = await service.StartRunAsync("prompt", _ => callbackCount++);
        Assert.AreEqual(1500, (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text.Length);
        Assert.AreEqual(1500, callbackCount);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(run));
        StringAssert.Contains(exception.Message, "1,024");
        Assert.IsTrue(run.Result.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task LateReader_ReplaysBufferedEvents_AndSecondReaderIsRejected()
    {
        var service = new RunTestService();
        await using var run = await service.StartRunAsync("prompt");
        Assert.AreEqual("answer", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        var events = await CollectAsync(run);
        Assert.AreEqual("answer", string.Concat(events.Where(e => e.Type == StreamingContentType.Text).Select(e => e.Content)));
        Assert.Throws<InvalidOperationException>(() => run.StreamAsync());
        Assert.AreEqual(1, service.RoundCount);
    }

    [TestMethod]
    public async Task ReaderCancellation_DoesNotCancelExecution()
    {
        var release = Signal();
        var service = new RunTestService { BlockUntil = release.Task };
        await using var run = await service.StartRunAsync("prompt");
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var observationCancellation = new CancellationTokenSource();
        var reader = run.StreamAsync(observationCancellation.Token).GetAsyncEnumerator();
        var read = reader.MoveNextAsync().AsTask();
        observationCancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await read);
        await reader.DisposeAsync();
        Assert.IsFalse(run.Result.IsCompleted);
        release.SetResult();
        Assert.AreEqual("answer", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
    }

    [TestMethod]
    public async Task CallbackFailure_FailsResultAndCleansUpBeforeReleasingService()
    {
        var cleanup = Signal();
        var service = new RunTestService { CleanupUntil = cleanup.Task };
        await using var run = await service.StartRunAsync("prompt", _ => throw new ApplicationException("display failed"));
        await service.CleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(run.Result.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => service.StartRunAsync("overlap"));
        cleanup.SetResult();
        var exception = await Assert.ThrowsAsync<ApplicationException>(async () => await run.Result);
        Assert.AreEqual("display failed", exception.Message);
        Assert.IsTrue(service.SessionDisposed);
        await using var next = await service.StartRunAsync("next");
        Assert.AreEqual("answer", (await next.Result).Text);
    }

    [TestMethod]
    public async Task Cancel_CancelsResultAndAllowsNextRunAfterCleanup()
    {
        var service = new RunTestService { BlockUntil = Signal().Task };
        await using var run = await service.StartRunAsync("prompt");
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        run.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(run.Result.IsCanceled);
        Assert.IsTrue(service.SessionDisposed);
        service.BlockUntil = Task.CompletedTask;
        await using var next = await service.StartRunAsync("next");
        Assert.AreEqual("answer", (await next.Result).Text);
    }

    [TestMethod]
    public async Task Dispose_CancelsExecutionAndWaitsForProviderCleanup()
    {
        var cleanup = Signal();
        var service = new RunTestService { BlockUntil = Signal().Task, CleanupUntil = cleanup.Task };
        var run = await service.StartRunAsync("prompt");
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposing = run.DisposeAsync().AsTask();
        await service.CleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(disposing.IsCompleted);
        Assert.IsFalse(run.Result.IsCompleted);
        cleanup.SetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(service.SessionDisposed);
        Assert.IsTrue(run.Result.IsCanceled);
        await run.DisposeAsync();
    }

    [TestMethod]
    public async Task UnsupportedSteering_DoesNotCancelOtherwiseValidRun()
    {
        var service = new RunTestService();
        await using var run = await service.StartRunAsync("prompt");
        Assert.IsFalse(run.CanSteer);
        await Assert.ThrowsAsync<NotSupportedException>(() => run.SteerAsync("new instruction"));
        Assert.AreEqual("answer", (await run.Result).Text);
    }

    [TestMethod]
    public async Task OverlappingRuns_AreRejectedBeforeSecondExecution()
    {
        var release = Signal();
        var service = new RunTestService { BlockUntil = release.Task };
        await using var run = await service.StartRunAsync("prompt");
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => service.StartRunAsync("overlap"));
        Assert.AreEqual(1, service.RoundCount);
        release.SetResult();
        await run.Result;
    }

    [TestMethod]
    public async Task TextOnlyObservation_StillExecutesRegisteredToolsAndMultipleRounds()
    {
        var toolCalls = 0;
        var service = new RunTestService { ToolRounds = 1 };
        service.WithFunction("lookup", "lookup", () => { toolCalls++; return "tool answer"; });
        await using var run = await service.WithMaxRounds(2).StartRunAsync("prompt", options: StreamOptions.TextOnlyOptions);
        var events = await CollectAsync(run);
        Assert.AreEqual("answer", (await run.Result).Text);
        Assert.AreEqual(1, toolCalls);
        Assert.AreEqual(2, service.RoundCount);
        Assert.IsTrue(service.ObservedUseFunctions);
        Assert.IsTrue(events.All(e => e.Type == StreamingContentType.Text));
    }

    [TestMethod]
    public async Task RoundLimitFailure_IsNotReturnedAsAnEmptySuccessfulResult()
    {
        var service = new RunTestService { ToolRounds = 2 };
        service.WithFunction("lookup", "lookup", () => "tool answer");
        await using var run = await service.WithMaxRounds(1).StartRunAsync("prompt");
        var exception = await Assert.ThrowsAsync<AIServiceException>(async () => await run.Result);
        StringAssert.Contains(exception.Message, "Maximum function-calling rounds (1)");
        Assert.AreEqual(1, service.RoundCount);
        Assert.IsTrue(service.SessionDisposed);
    }

    [TestMethod]
    public async Task PendingPolicy_IsSnapshottedBeforeAsynchronousSessionPreparation()
    {
        var preparation = Signal();
        var service = new RunTestService { PrepareUntil = preparation.Task };
        var policy = FunctionCallingPolicy.Default.Clone();
        policy.MaxRounds = 3;
        var starting = service.WithPolicy(policy).StartRunAsync("prompt");
        policy.MaxRounds = 99;
        preparation.SetResult();
        await using var run = await starting;
        Assert.AreEqual("answer", (await run.Result).Text);
        Assert.AreEqual(3, service.ObservedMaxRounds);
    }

    [TestMethod]
    public async Task CancelDuringSessionPreparation_ReleasesServiceWithoutStartingExecution()
    {
        var preparation = Signal();
        var service = new RunTestService { PrepareUntil = preparation.Task };
        using var cancellation = new CancellationTokenSource();
        var starting = service.StartRunAsync("prompt", cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await starting);
        Assert.AreEqual(0, service.RoundCount);
        service.PrepareUntil = Task.CompletedTask;
        await using var next = await service.StartRunAsync("next");
        Assert.AreEqual("answer", (await next.Result).Text);
    }

    [TestMethod]
    public async Task SessionPreparation_UsesResolvedRequestTimeoutAndReleasesService()
    {
        var service = new RunTestService { PrepareUntil = Signal().Task };
        var policy = FunctionCallingPolicy.Default.Clone();
        policy.TimeoutSeconds = 0;
        var starting = service.WithPolicy(policy).StartRunAsync("prompt");
        var exception = await Assert.ThrowsAsync<AIServiceException>(async () => await starting.WaitAsync(TimeSpan.FromSeconds(5)));
        StringAssert.Contains(exception.Message, "Request timeout after 0 seconds");
        Assert.AreEqual(0, service.RoundCount);
        service.PrepareUntil = Task.CompletedTask;
        await using var next = await service.StartRunAsync("next");
        Assert.AreEqual("answer", (await next.Result).Text);
    }

    [TestMethod]
    public async Task PolicyTimeout_CoversProviderThatOverridesStreamingCoreWithoutItsOwnTimeout()
    {
        var service = new RunTestService { OverrideStreamingCore = true, BlockUntil = Signal().Task };
        var observedText = new List<string>();
        await using var run = await service.WithTimeout(1).StartRunAsync("prompt", observedText.Add);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var exception = await Assert.ThrowsAsync<AIServiceException>(async () => await run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        StringAssert.Contains(exception.Message, "Request timeout after 1 seconds");
        CollectionAssert.AreEqual(new[] { "started" }, observedText);
        Assert.IsFalse(run.Result.IsCanceled, "Policy expiry is an execution failure, not caller cancellation.");
        Assert.IsTrue(service.SessionDisposed);
        service.OverrideStreamingCore = false;
        service.BlockUntil = Task.CompletedTask;
        await using var next = await service.StartRunAsync("next");
        Assert.AreEqual("answer", (await next.Result).Text);
    }

    [TestMethod]
    public async Task CallerCancellation_RemainsCanceledWithAnOverriddenStreamingCore()
    {
        var service = new RunTestService { OverrideStreamingCore = true, BlockUntil = Signal().Task };
        using var cancellation = new CancellationTokenSource();
        await using var run = await service.WithTimeout(10).StartRunAsync("prompt", cancellationToken: cancellation.Token);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(run.Result.IsCanceled);
        Assert.IsTrue(service.SessionDisposed);
    }

    [TestMethod]
    public async Task MessageAndContext_PreserveMultimodalInputAndMetadataAcrossPreparation()
    {
        var preparation = Signal();
        var service = new RunTestService { PrepareUntil = preparation.Task };
        var image = new ImageContent(new byte[] { 1, 2, 3 }, "image/png") { IsHighDetail = true };
        var textContent = new TextContent("describe");
        var audio = new AudioContent(new byte[] { 4, 5 }, "audio/wav") { Duration = TimeSpan.FromSeconds(2) };
        var custom = new CustomTextContent("custom");
        var message = new Message(ActorRole.User, new List<MessageContent> { textContent, image, audio, custom })
        {
            Content = "explicit scalar text",
            Metadata = new Dictionary<string, object> { ["trace"] = new Dictionary<string, object> { ["id"] = "original" } }
        };
        var additional = new Message(ActorRole.User, "additional context");
        var context = new AIRequestContext
        {
            SystemMessagePrefix = "prefix",
            SystemMessageSuffix = "suffix",
            AdditionalMessages = new[] { additional }
        };
        var starting = service.StartRunAsync(message, context: context);
        message.Content = "changed after start";
        textContent.Text = "changed text";
        image.Data![0] = 9;
        image.MimeType = "changed/type";
        image.IsHighDetail = false;
        audio.Data![0] = 9;
        audio.Duration = TimeSpan.FromSeconds(99);
        message.Contents.Clear();
        ((Dictionary<string, object>)message.Metadata["trace"])["id"] = "changed";
        additional.Content = "changed additional";
        context.SystemMessagePrefix = "changed prefix";
        preparation.SetResult();
        await using var run = await starting;
        await run.Result;
        var request = service.ObservedMessages[0];
        Assert.AreEqual("explicit scalar text", request.Content);
        Assert.AreEqual("original", ((Dictionary<string, object>)request.Metadata!["trace"])["id"]);
        Assert.AreEqual(4, request.Contents.Count);
        Assert.AreEqual("describe", ((TextContent)request.Contents[0]).Text);
        var capturedImage = (ImageContent)request.Contents[1];
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, capturedImage.Data);
        Assert.AreEqual("image/png", capturedImage.MimeType);
        Assert.IsTrue(capturedImage.IsHighDetail);
        CollectionAssert.AreEqual(new byte[] { 4, 5 }, ((AudioContent)request.Contents[2]).Data);
        Assert.AreEqual(TimeSpan.FromSeconds(2), ((AudioContent)request.Contents[2]).Duration);
        Assert.AreSame(custom, request.Contents[3]);
        Assert.AreEqual("additional context", service.ObservedMessages[1].Content);
        StringAssert.Contains(service.ObservedSystemMessage, "prefix");
        Assert.IsFalse(service.ObservedSystemMessage.Contains("changed prefix"));
        StringAssert.Contains(service.ObservedSystemMessage, "suffix");
    }

    [TestMethod]
    public async Task RequestOverride_RemainsAnchoredAcrossToolRoundsAndLaterUserInput()
    {
        var service = new RunTestService { ToolRounds = 1, LaterUserInput = "additional instruction" };
        service.WithFunction("lookup", "lookup", () => "required tool result");
        var context = new AIRequestContext
        {
            SystemMessagePrefix = "run prefix",
            RequestMessageOverride = new Message(ActorRole.User, "augmented original query")
        };
        await using var run = await service.WithMaxRounds(2).StartRunAsync("original query", context: context);
        await run.Result;
        Assert.AreEqual(2, service.RoundCount);
        Assert.AreEqual("augmented original query", service.ObservedMessages[0].Content);
        Assert.IsTrue(service.ObservedMessages.Any(message => message.FunctionCallResultBatch?.Results
            .Any(result => result.Content == "required tool result") == true));
        Assert.AreEqual("additional instruction", service.ObservedMessages.Last().Content);
        StringAssert.Contains(service.ObservedSystemMessage, "run prefix");
        Assert.AreEqual("original query", service.ActivateChat.Messages[0].Content);
    }

    [TestMethod]
    public async Task LegacyInterfaceImplementation_RemainsUsableWithoutRunCapability()
    {
        IAIService service = new LegacyOnlyService();
        Assert.AreEqual("legacy completion", await service.GetCompletionAsync("prompt"));
        Assert.Throws<NotSupportedException>(() => service.StartRunAsync("prompt"));
        Assert.AreEqual("legacy completion", await service.GetCompletionAsync(new Message(ActorRole.User, "next")));
    }

    [TestMethod]
    public async Task InterfaceReference_UsesOptionalRunCapability()
    {
        IAIService service = new RunTestService();
        await using var run = await service.StartRunAsync("prompt");
        Assert.AreEqual("answer", (await run.Result).Text);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<List<StreamingContent>> CollectAsync(AIRun run)
    {
        var result = new List<StreamingContent>();
        await foreach (var item in run.StreamAsync()) result.Add(item);
        return result;
    }

    private sealed class RunTestService : AIService
    {
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource CleanupStarted { get; } = Signal();
        public Task BlockUntil { get; set; } = Task.CompletedTask;
        public Task CleanupUntil { get; set; } = Task.CompletedTask;
        public Task PrepareUntil { get; set; } = Task.CompletedTask;
        public string[] Chunks { get; set; } = new[] { "answer" };
        public int ToolRounds { get; set; }
        public string? LaterUserInput { get; set; }
        public bool OverrideStreamingCore { get; set; }
        public int RoundCount { get; private set; }
        public int ObservedMaxRounds { get; private set; }
        public bool ObservedUseFunctions { get; private set; }
        public bool SessionDisposed { get; private set; }
        public List<Message> ObservedMessages { get; private set; } = new();
        public string ObservedSystemMessage { get; private set; } = string.Empty;

        public RunTestService() : base("fake-key", "https://localhost/", new HttpClient()) => AddNewChat();
        public override string Provider => nameof(AIProvider.OpenAI);

        protected override async Task<RunSession> CreateRunSessionAsync(Message message,
            StreamOptions executionOptions, AIRequestContext? context, CancellationToken cancellationToken)
        {
            await PrepareUntil.WaitAsync(cancellationToken);
            return new ObservedSession(await base.CreateRunSessionAsync(message, executionOptions, context, cancellationToken), this);
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(StreamOptions options,
            bool useFunctions, FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RoundCount++;
            ObservedMaxRounds = policy.MaxRounds;
            ObservedUseFunctions = useFunctions;
            ObservedMessages = GetLatestMessages().ToList();
            ObservedSystemMessage = GetEffectiveSystemMessage();
            Started.TrySetResult();
            await BlockUntil.WaitAsync(cancellationToken);
            if (RoundCount <= ToolRounds && useFunctions)
            {
                var call = new FunctionCall { Id = "call_" + RoundCount, Name = "lookup", Arguments = new Dictionary<string, object>() };
                yield return new StreamingContent { Type = StreamingContentType.FunctionCall, FunctionCall = call };
                var batches = await ProcessFunctionBatchForRoundAsync(string.Empty,
                    new FunctionCallBatch(new[] { call }), null, policy, cancellationToken);
                foreach (var result in batches.SelectMany(batch => batch.Results))
                    yield return new StreamingContent { Type = StreamingContentType.FunctionResult, FunctionResult = result };
                if (LaterUserInput != null)
                    ActivateChat.Messages.Add(new Message(ActorRole.User, LaterUserInput));
                yield break;
            }
            foreach (var chunk in Chunks)
                yield return new StreamingContent { Type = StreamingContentType.Text, Content = chunk };
        }

        protected override IAsyncEnumerable<StreamingContent> StreamCoreAsync(Message message,
            StreamOptions options, CancellationToken cancellationToken = default)
            => OverrideStreamingCore ? StreamWithoutProviderTimeout(cancellationToken)
                : base.StreamCoreAsync(message, options, cancellationToken);

        private async IAsyncEnumerable<StreamingContent> StreamWithoutProviderTimeout(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            yield return new StreamingContent { Type = StreamingContentType.Text, Content = "started" };
            await BlockUntil.WaitAsync(cancellationToken);
            yield return new StreamingContent { Type = StreamingContentType.Completion };
        }

        private sealed class ObservedSession : RunSession
        {
            private readonly RunSession _inner;
            private readonly RunTestService _owner;
            public ObservedSession(RunSession inner, RunTestService owner) { _inner = inner; _owner = owner; }
            public override IAsyncEnumerable<StreamingContent> StreamAsync(CancellationToken cancellationToken)
                => _inner.StreamAsync(cancellationToken);
            public override async ValueTask DisposeAsync()
            {
                _owner.CleanupStarted.TrySetResult();
                await _owner.CleanupUntil;
                await _inner.DisposeAsync();
                _owner.SessionDisposed = true;
            }
        }

        public override Task<string> GetCompletionAsync(Message message) => throw new NotSupportedException();
        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync) => throw new NotSupportedException();
        protected override HttpRequestMessage CreateMessageRequest() => new(HttpMethod.Post, "https://localhost/");
        protected override HttpRequestMessage CreateFunctionMessageRequest() => new(HttpMethod.Post, "https://localhost/");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => (response, new FunctionCallBatch());
        protected override string ExtractResponseContent(string responseContent) => responseContent;
        protected override string StreamParseJson(string jsonData) => jsonData;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
    }

    private sealed class CustomTextContent : TextContent
    {
        public CustomTextContent(string text) : base(text) { }
        public override string Type => "custom_text";
    }

    private sealed class LegacyOnlyService : IAIService
    {
        public string Model => "custom";
        public string Provider => "custom";
        public string SystemMessage { get; set; } = string.Empty;
        public bool StatelessMode { get; set; }
        public ChatBlock ActivateChat { get; } = new();
        public Task<string> GetCompletionAsync(string prompt, AIRequestProfile? profile = null, AIRequestContext? context = null, CancellationToken cancellationToken = default)
            => Task.FromResult("legacy completion");
        public Task<string> GetCompletionAsync(Message message, AIRequestProfile? profile = null, AIRequestContext? context = null, CancellationToken cancellationToken = default)
            => Task.FromResult("legacy completion");
        public IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamAsync(Message message, AIRequestContext? context = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<StreamingContent> StreamAsync(string prompt, StreamOptions options, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<StreamingContent> StreamAsync(Message message, StreamOptions options, AIRequestContext? context = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
