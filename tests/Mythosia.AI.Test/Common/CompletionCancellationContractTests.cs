using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class CompletionCancellationContractTests
{
    [TestMethod]
    [DataRow("string")]
    [DataRow("message")]
    [DataRow("interface-string")]
    [DataRow("interface-message")]
    [DataRow("builder")]
    [DataRow("message-chain")]
    [DataRow("one-off-message-chain")]
    [DataRow("one-off-string")]
    [DataRow("one-off-message")]
    [DataRow("structured")]
    public async Task PreCancelled_DoesNotInvokeContextSummaryOrProvider_OrChangeHistory(string entryPoint)
    {
        var service = new CompletionProbeService();
        SeedHistory(service);
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        var history = service.ActivateChat.Messages.ToArray();
        var contexts = 0;
        service.WithSystemMessageProvider(() =>
        {
            contexts++;
            return new AIRequestContext { SystemMessagePrefix = "dynamic" };
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Task SendAsync() => entryPoint switch
        {
            "string" => service.GetCompletionAsync("cancelled", cancellationToken: cancellation.Token),
            "message" => service.GetCompletionAsync(new Message(ActorRole.User, "cancelled"), cancellationToken: cancellation.Token),
            "interface-string" => ((IAIService)service).GetCompletionAsync("cancelled", cancellationToken: cancellation.Token),
            "interface-message" => ((IAIService)service).GetCompletionAsync(new Message(ActorRole.User, "cancelled"), cancellationToken: cancellation.Token),
            "builder" => service.CreateRequest("cancelled").GetCompletionAsync(cancellation.Token),
            "message-chain" => service.BeginMessage().AddText("cancelled").SendAsync(cancellation.Token),
            "one-off-message-chain" => service.BeginMessage().AddText("cancelled").SendOnceAsync(cancellation.Token),
            "one-off-string" => service.AskOnceAsync("cancelled", cancellation.Token),
            "one-off-message" => service.AskOnceAsync(new Message(ActorRole.User, "cancelled"), cancellation.Token),
            "structured" => service.GetCompletionAsync<Answer>("cancelled", cancellationToken: cancellation.Token),
            _ => throw new AssertFailedException($"Unknown entry point: {entryPoint}")
        };

        await Assert.ThrowsAsync<OperationCanceledException>(SendAsync);

        Assert.AreEqual(0, contexts);
        Assert.AreEqual(0, service.Calls.Count);
        CollectionAssert.AreEqual(history, service.ActivateChat.Messages.ToArray());
        Assert.IsNull(service.ConversationPolicy.CurrentSummary);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OneOffCancellation_PreservesConversationAndAllowsNextOneOffRequest(bool messageInput)
    {
        var service = new CompletionProbeService();
        SeedHistory(service);
        var originalChat = service.ActivateChat;
        var history = originalChat.Messages.ToArray();
        var started = Signal();
        service.Response = (_, _, token) => WaitForCancellationAsync(started, token);
        using var cancellation = new CancellationTokenSource();
        var request = messageInput
            ? service.AskOnceAsync(new Message(ActorRole.User, "one off"), cancellation.Token)
            : service.AskOnceAsync("one off", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.AreSame(originalChat, service.ActivateChat);
        CollectionAssert.AreEqual(history, originalChat.Messages.ToArray());
        Assert.IsFalse(service.StatelessMode);
        Assert.AreEqual(cancellation.Token, service.Calls.Single().Token);
        Assert.IsTrue(service.Calls.Single().Stateless);
        service.Response = (_, _, _) => Task.FromResult("next answer");
        Assert.AreEqual("next answer", await service.AskOnceAsync("next one off"));
        CollectionAssert.AreEqual(history, originalChat.Messages.ToArray());
        Assert.IsFalse(service.Calls[1].Token.CanBeCanceled);
    }

    [TestMethod]
    public async Task Cancellation_DuringDynamicContext_CancelsProviderAndSkipsModelRequest()
    {
        var service = new CompletionProbeService();
        var started = Signal();
        var contextCleanedUp = false;
        CancellationToken receivedToken = default;
        service.WithSystemMessageProvider(async token =>
        {
            receivedToken = token;
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return new AIRequestContext();
            }
            finally { contextCleanedUp = true; }
        });
        using var cancellation = new CancellationTokenSource();
        var request = service.GetCompletionAsync("context lookup", cancellationToken: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.AreEqual(cancellation.Token, receivedToken);
        Assert.IsTrue(contextCleanedUp);
        Assert.AreEqual(0, service.Calls.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        service.WithSystemMessageProvider(() => null);
        Assert.AreEqual("answer", await service.GetCompletionAsync("next"));
    }

    [TestMethod]
    public async Task CancelledBuilder_RestoresSettingsContextAndToken_AndCanBeReused()
    {
        var service = new CompletionProbeService { Temperature = 0.7f, SystemMessage = "default system" };
        var originalChat = service.ActivateChat;
        var started = Signal();
        service.Response = (call, _, token) => call == 1
            ? WaitForCancellationAsync(started, token)
            : Task.FromResult("answer");
        var builder = service.CreateRequest("request only")
            .WithTemperature(0.2f)
            .WithSystemMessage("request system")
            .WithContext(new AIRequestContext { SystemMessagePrefix = "request prefix" })
            .WithStatelessMode();
        using var cancellation = new CancellationTokenSource();
        var request = builder.GetCompletionAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreEqual(0, originalChat.Messages.Count);
        Assert.AreEqual(0.7f, service.Temperature);
        Assert.AreEqual("default system", service.SystemMessage);
        Assert.IsFalse(service.StatelessMode);
        Assert.AreEqual("answer", await service.GetCompletionAsync("ordinary next request"));
        Assert.AreEqual("answer", await builder.GetCompletionAsync());

        CollectionAssert.AreEqual(new[] { 0.2f, 0.7f, 0.2f }, service.Calls.Select(c => c.Temperature).ToArray());
        CollectionAssert.AreEqual(new[] { true, false, true }, service.Calls.Select(c => c.Stateless).ToArray());
        Assert.AreEqual(cancellation.Token, service.Calls[0].Token);
        Assert.IsFalse(service.Calls[1].Token.CanBeCanceled);
        Assert.IsFalse(service.Calls[2].Token.CanBeCanceled);
        StringAssert.Contains(service.Calls[0].SystemMessage, "request prefix");
        Assert.AreEqual("default system", service.Calls[1].SystemMessage);
        StringAssert.Contains(service.Calls[2].SystemMessage, "request prefix");
        Assert.AreEqual(2, originalChat.Messages.Count, "Only the ordinary next request belongs in the shared history.");
    }

    [TestMethod]
    public async Task AutomaticSummaryCancellation_PreservesOriginalHistoryAndSkipsUserRequest()
    {
        var service = new CompletionProbeService();
        SeedHistory(service);
        var history = service.ActivateChat.Messages.ToArray();
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        var started = Signal();
        service.Response = (_, _, token) => WaitForCancellationAsync(started, token);
        using var cancellation = new CancellationTokenSource();
        var request = service.GetCompletionAsync("new question", cancellationToken: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.AreEqual(1, service.Calls.Count);
        Assert.IsTrue(service.Calls[0].Stateless);
        Assert.AreEqual(cancellation.Token, service.Calls[0].Token);
        CollectionAssert.AreEqual(history, service.ActivateChat.Messages.ToArray());
        Assert.IsNull(service.ConversationPolicy.CurrentSummary);

        service.Response = (_, _, _) => Task.FromResult("summary and answer");
        Assert.AreEqual("summary and answer", await service.GetCompletionAsync("next question"));
        Assert.AreEqual(3, service.Calls.Count, "Cancellation must release the summary guard so the next request can summarize and then send.");
        Assert.IsTrue(service.Calls[1].Stateless);
        Assert.IsFalse(service.Calls[2].Stateless);
    }

    [TestMethod]
    public async Task ContextRecoverySummaryCancellation_ReportsCancellationAndDoesNotRetryOrCompactHistory()
    {
        var service = new CompletionProbeService();
        SeedHistory(service);
        var history = service.ActivateChat.Messages.ToArray();
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        var overflow = Overflow();
        var summaryStarted = Signal();
        service.Response = (call, _, token) => call == 1
            ? Task.FromException<string>(overflow)
            : WaitForCancellationAsync(summaryStarted, token);
        using var cancellation = new CancellationTokenSource();

        // The Message overload enters server-driven recovery without an automatic summary first.
        var request = service.GetCompletionAsync(new Message(ActorRole.User, "overflowing question"),
            cancellationToken: cancellation.Token);
        await summaryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(2, service.Calls.Count);
        Assert.IsFalse(service.Calls[0].Stateless);
        Assert.IsTrue(service.Calls[1].Stateless);
        Assert.AreEqual(cancellation.Token, service.Calls[1].Token);
        CollectionAssert.AreEqual(history, service.ActivateChat.Messages.ToArray());
        Assert.IsNull(service.ConversationPolicy.CurrentSummary);
    }

    [TestMethod]
    public async Task ContextRecovery_UnrelatedSummaryCancellationPreservesOriginalContextError()
    {
        var service = new CompletionProbeService();
        SeedHistory(service);
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        var overflow = Overflow();
        using var unrelatedCancellation = new CancellationTokenSource();
        unrelatedCancellation.Cancel();
        service.Response = (call, _, _) => Task.FromException<string>(call == 1
            ? overflow
            : new OperationCanceledException("Independent summary operation cancelled.", unrelatedCancellation.Token));
        using var callerCancellation = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<ContextLengthExceededException>(() => service.GetCompletionAsync(
            new Message(ActorRole.User, "overflowing question"), cancellationToken: callerCancellation.Token));

        Assert.AreSame(overflow, exception);
        Assert.AreEqual("compaction-threw", exception.RecoverySkipReason);
        Assert.IsFalse(callerCancellation.IsCancellationRequested);
        Assert.AreEqual(2, service.Calls.Count);
    }

    [TestMethod]
    public async Task StructuredCancellation_AfterInvalidResponse_DoesNotStartRepair()
    {
        var service = new CompletionProbeService();
        using var cancellation = new CancellationTokenSource();
        service.Response = (_, _, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult("not json");
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetCompletionAsync<Answer>(
            "structured question", cancellationToken: cancellation.Token));

        Assert.AreEqual(1, service.Calls.Count);
        Assert.IsNotNull(service.Calls[0].StructuredSchema);
        service.Response = (_, _, _) => Task.FromResult("ordinary answer");
        Assert.AreEqual("ordinary answer", await service.GetCompletionAsync("ordinary next request"));
        Assert.IsNull(service.Calls[1].StructuredSchema);
        Assert.IsFalse(service.Calls[1].Token.CanBeCanceled);
    }

    [TestMethod]
    public async Task StructuredCancellation_DuringRepair_CancelsRepairAndSkipsRemainingAttempts()
    {
        var service = new CompletionProbeService { StructuredOutputMaxRetries = 4 };
        var repairStarted = Signal();
        service.Response = (call, _, token) => call == 1
            ? Task.FromResult("not json")
            : WaitForCancellationAsync(repairStarted, token);
        using var cancellation = new CancellationTokenSource();
        var request = service.GetCompletionAsync<Answer>("structured question", cancellationToken: cancellation.Token);
        await repairStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.AreEqual(2, service.Calls.Count);
        StringAssert.Contains(service.Calls[1].Prompt, "STRUCTURED OUTPUT CORRECTION");
        Assert.IsTrue(service.Calls.All(c => c.Token == cancellation.Token));
        Assert.IsTrue(service.Calls.All(c => c.StructuredSchema != null));
    }

    [TestMethod]
    public async Task UnrelatedProviderCancellation_IsNotReplacedWithCallerCancellation()
    {
        var service = new CompletionProbeService();
        using var callerCancellation = new CancellationTokenSource();
        using var unrelatedCancellation = new CancellationTokenSource();
        unrelatedCancellation.Cancel();
        var original = new OperationCanceledException("Independent provider cancellation.", unrelatedCancellation.Token);
        service.Response = (_, _, _) => Task.FromException<string>(original);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetCompletionAsync(
            "question", cancellationToken: callerCancellation.Token));

        Assert.AreSame(original, exception);
        Assert.AreEqual(unrelatedCancellation.Token, exception.CancellationToken);
        Assert.IsFalse(callerCancellation.IsCancellationRequested);
        Assert.AreEqual(1, service.Calls.Count);
    }

    public sealed class Answer
    {
        public int Value { get; set; }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<string> WaitForCancellationAsync(TaskCompletionSource started, CancellationToken token)
    {
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, token);
        throw new AssertFailedException("The cancellation barrier must not complete normally.");
    }

    private static ContextLengthExceededException Overflow()
        => new("Context length exceeded", "Test context overflow", statusCode: 400);

    private static void SeedHistory(AIService service)
    {
        for (var turn = 0; turn < 4; turn++)
        {
            service.ActivateChat.Messages.Add(new Message(ActorRole.User, $"old user {turn}"));
            service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, $"old answer {turn}"));
        }
    }

    private sealed record CallSnapshot(CancellationToken Token, float Temperature, bool Stateless,
        string SystemMessage, string Prompt, string? StructuredSchema);

    /// <summary>
    /// Keeps the established provider override and observes the common execution token. The fake
    /// performs no transport; provider HTTP cancellation has separate wire-level contract tests.
    /// </summary>
    private sealed class CompletionProbeService : AIService
    {
        public override string Provider => "Test";
        public List<CallSnapshot> Calls { get; } = new();
        public Func<int, Message, CancellationToken, Task<string>> Response { get; set; }
            = (_, _, _) => Task.FromResult("answer");

        public CompletionProbeService() : base("offline", "https://localhost/", new HttpClient())
        {
            AddNewChat();
        }

        public override async Task<string> GetCompletionAsync(Message message)
        {
            using var scope = BeginRequestSettingsScope();
            var token = RequestCancellationToken;
            Calls.Add(new CallSnapshot(token, RequestTemperature, RequestStatelessMode,
                GetEffectiveSystemMessageWithRequestContext(), message.Content, RequestStructuredOutputSchemaJson));
            var callerChat = ActivateChat;
            if (RequestStatelessMode)
                ActivateChat = new ChatBlock { SystemMessage = RequestSystemMessage };
            try
            {
                ActivateChat.Messages.Add(message);
                var answer = await Response(Calls.Count, message, token);
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, answer));
                return answer;
            }
            finally { ActivateChat = callerChat; }
        }

        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => throw new NotSupportedException();
        protected override HttpRequestMessage CreateMessageRequest()
            => throw new AssertFailedException("These tests must not send HTTP requests.");
        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => throw new AssertFailedException("These tests must not send HTTP requests.");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
            => (response, new FunctionCallBatch());
        protected override string ExtractResponseContent(string responseContent) => responseContent;
        protected override string StreamParseJson(string jsonData) => jsonData;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
    }
}
