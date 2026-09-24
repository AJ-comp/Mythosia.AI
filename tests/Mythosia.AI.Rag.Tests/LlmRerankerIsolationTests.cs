using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Rag.Reranking;
using Mythosia.AI.Services.OpenAI;
using Mythosia.VectorDb;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class LlmRerankerIsolationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private const string AlphaDocument = "ALPHA_PRIVATE_REFUND_RULE_73691";
    private const string BetaDocument = "BETA_PRIVATE_REFUND_RULE_84026";
    private const string ExistingUser = "EXISTING_PRIVATE_USER_MESSAGE_59234";
    private const string ExistingAssistant = "EXISTING_PRIVATE_ASSISTANT_MESSAGE_96172";

    [TestMethod]
    public async Task SequentialEvaluations_SendOnlyTheirOwnDocuments_AndPreserveRanking()
    {
        using var handler = new ScoringHandler((_, _) => Task.FromResult(ScoreResponse("2,9")));
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        var originalChat = service.ActivateChat;
        var reranker = new LlmReranker(service);
        var alpha = Candidates("alpha", AlphaDocument);
        var beta = Candidates("beta", BetaDocument);

        var first = await reranker.RerankAsync("Alpha refund?", alpha);
        var second = await reranker.RerankAsync("Beta refund?", beta);

        AssertRanking(first, alpha);
        AssertRanking(second, beta);
        var requests = handler.Requests.ToArray();
        Assert.AreEqual(2, requests.Length);
        AssertIsolatedRequest(requests[0], AlphaDocument, BetaDocument);
        AssertIsolatedRequest(requests[1], BetaDocument, AlphaDocument);
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        Assert.IsFalse(service.StatelessMode, "Internal scoring must not change the caller's service defaults.");
    }

    [TestMethod]
    public async Task ExistingConversation_IsNeitherSentNorChanged_AndStillWorksAfterReranking()
    {
        using var handler = new ScoringHandler((_, _) => Task.FromResult(ScoreResponse("2,9")));
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        var originalChat = service.ActivateChat;
        var history = SeedHistory(service);

        await new LlmReranker(service).RerankAsync("Alpha refund?", Candidates("alpha", AlphaDocument));

        AssertIsolatedRequest(handler.Requests.Single(), AlphaDocument, ExistingUser, ExistingAssistant);
        Assert.AreSame(originalChat, service.ActivateChat);
        AssertHistoryPreserved(service, history);
        Assert.IsFalse(service.StatelessMode);

        // The caller can keep using its ordinary conversation after this internal scoring request.
        await service.GetCompletionAsync("Continue our existing conversation.");
        var continuation = handler.Requests.Last().GetRawText();
        StringAssert.Contains(continuation, ExistingUser);
        StringAssert.Contains(continuation, ExistingAssistant);
        Assert.IsFalse(continuation.Contains(AlphaDocument, StringComparison.Ordinal));
        Assert.AreEqual(4, service.ActivateChat.Messages.Count);
        Assert.AreSame(history[0], service.ActivateChat.Messages[0]);
        Assert.AreSame(history[1], service.ActivateChat.Messages[1]);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(100)]
    public async Task ConversationSummary_IsNotSentOrRecomputed_EvenWhenSummaryThresholdIsExceeded(int triggerCount)
    {
        const string summary = "EXISTING_PRIVATE_CONVERSATION_SUMMARY_72013";
        using var handler = new ScoringHandler((_, _) => Task.FromResult(ScoreResponse("2,9")));
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        var originalChat = service.ActivateChat;
        var history = SeedHistory(service);
        var policy = SummaryConversationPolicy.ByMessage((uint)triggerCount, keepRecentCount: 0);
        policy.CurrentSummary = summary;
        service.ConversationPolicy = policy;

        var alpha = Candidates("alpha", AlphaDocument);
        AssertRanking(await new LlmReranker(service).RerankAsync("Alpha refund?", alpha), alpha);

        Assert.AreEqual(1, handler.Requests.Count, "Reranking must not start a summary request for the caller's conversation.");
        AssertIsolatedRequest(handler.Requests.Single(), AlphaDocument, summary, ExistingUser, ExistingAssistant);
        Assert.AreSame(originalChat, service.ActivateChat);
        AssertHistoryPreserved(service, history);
        Assert.AreSame(policy, service.ConversationPolicy);
        Assert.AreEqual(summary, policy.CurrentSummary);
        Assert.AreEqual((uint)triggerCount, policy.TriggerCount);
        Assert.AreEqual(0u, policy.KeepRecentCount);
        Assert.IsFalse(service.StatelessMode);
    }

    [TestMethod]
    public async Task FailedEvaluation_PreservesConversation_AndDoesNotContaminateNextEvaluation()
    {
        using var handler = new ScoringHandler((call, _) => Task.FromResult(call == 1
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"message":"Offline scoring rejection","type":"invalid_request_error"}}""",
                    Encoding.UTF8, "application/json")
            }
            : ScoreResponse("2,9")));
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        var originalChat = service.ActivateChat;
        var history = SeedHistory(service);
        var reranker = new LlmReranker(service);

        await Assert.ThrowsAsync<AIServiceException>(() =>
            reranker.RerankAsync("Alpha refund?", Candidates("alpha", AlphaDocument)));

        Assert.AreSame(originalChat, service.ActivateChat);
        AssertHistoryPreserved(service, history);
        Assert.IsFalse(service.StatelessMode);

        var beta = Candidates("beta", BetaDocument);
        AssertRanking(await reranker.RerankAsync("Beta refund?", beta), beta);
        var requests = handler.Requests.ToArray();
        Assert.AreEqual(2, requests.Length);
        AssertIsolatedRequest(requests[0], AlphaDocument, ExistingUser, ExistingAssistant);
        AssertIsolatedRequest(requests[1], BetaDocument, AlphaDocument, ExistingUser, ExistingAssistant);
        Assert.AreSame(originalChat, service.ActivateChat);
        AssertHistoryPreserved(service, history);
        Assert.IsFalse(service.StatelessMode);
    }

    [TestMethod]
    public async Task CancelledEvaluation_WaitsForTransportCleanup_AndDoesNotContaminateNextEvaluation()
    {
        var started = NewSignal();
        var cleanedUp = NewSignal();
        using var handler = new ScoringHandler(async (call, token) =>
        {
            if (call == 1)
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                finally
                {
                    cleanedUp.TrySetResult();
                }
            }
            return ScoreResponse("2,9");
        });
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        var originalChat = service.ActivateChat;
        var history = SeedHistory(service);
        var reranker = new LlmReranker(service);
        using var cancellation = new CancellationTokenSource();
        var cancelled = reranker.RerankAsync("Alpha refund?", Candidates("alpha", AlphaDocument), cancellation.Token);
        await started.Task.WaitAsync(TestTimeout);

        cancellation.Cancel();
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled.WaitAsync(TestTimeout));

        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.IsTrue(cleanedUp.Task.IsCompletedSuccessfully);
        Assert.AreSame(originalChat, service.ActivateChat);
        AssertHistoryPreserved(service, history);
        Assert.IsFalse(service.StatelessMode);

        var beta = Candidates("beta", BetaDocument);
        AssertRanking(await reranker.RerankAsync("Beta refund?", beta), beta);
        var requests = handler.Requests.ToArray();
        Assert.AreEqual(2, requests.Length);
        AssertIsolatedRequest(requests[0], AlphaDocument, ExistingUser, ExistingAssistant);
        AssertIsolatedRequest(requests[1], BetaDocument, AlphaDocument, ExistingUser, ExistingAssistant);
        Assert.AreSame(originalChat, service.ActivateChat);
        AssertHistoryPreserved(service, history);
        Assert.IsFalse(service.StatelessMode);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OverlappingEvaluations_SharingAService_KeepRequestsAndConversationIsolated(bool separateRerankers)
    {
        var firstEntered = NewSignal();
        var secondEntered = NewSignal();
        var releaseFirst = NewSignal();
        var releaseSecond = NewSignal();
        using var handler = new ScoringHandler(async (call, token) =>
        {
            if (call == 1)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task.WaitAsync(TestTimeout, token);
            }
            else
            {
                secondEntered.TrySetResult();
                await releaseSecond.Task.WaitAsync(TestTimeout, token);
            }
            return ScoreResponse("2,9");
        });
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        var originalChat = service.ActivateChat;
        var history = SeedHistory(service);
        var firstReranker = new LlmReranker(service);
        var secondReranker = separateRerankers ? new LlmReranker(service) : firstReranker;
        var alpha = Candidates("alpha", AlphaDocument);
        var beta = Candidates("beta", BetaDocument);

        var first = firstReranker.RerankAsync("Alpha refund?", alpha);
        await firstEntered.Task.WaitAsync(TestTimeout);
        var second = secondReranker.RerankAsync("Beta refund?", beta);
        try
        {
            // Complete in start order. This detects shared temporary-history restoration races,
            // while also permitting an implementation that serializes access to the shared service.
            releaseFirst.TrySetResult();
            AssertRanking(await first.WaitAsync(TestTimeout), alpha);
            await secondEntered.Task.WaitAsync(TestTimeout);
            releaseSecond.TrySetResult();
            AssertRanking(await second.WaitAsync(TestTimeout), beta);
        }
        finally
        {
            releaseFirst.TrySetResult();
            releaseSecond.TrySetResult();
        }

        var requests = handler.Requests.ToArray();
        Assert.AreEqual(2, requests.Length);
        AssertIsolatedRequest(requests[0], AlphaDocument, BetaDocument, ExistingUser, ExistingAssistant);
        AssertIsolatedRequest(requests[1], BetaDocument, AlphaDocument, ExistingUser, ExistingAssistant);
        Assert.AreSame(originalChat, service.ActivateChat);
        AssertHistoryPreserved(service, history);
        Assert.IsFalse(service.StatelessMode);
    }

    [TestMethod]
    public async Task CancelledWaitingEvaluation_DoesNotSendItsDocumentsOrCancelActiveEvaluation()
    {
        const string cancelledDocument = "CANCELLED_WAITING_PRIVATE_DOCUMENT_31279";
        var firstEntered = NewSignal();
        var releaseFirst = NewSignal();
        using var handler = new ScoringHandler(async (call, token) =>
        {
            if (call == 1)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task.WaitAsync(TestTimeout, token);
            }
            return ScoreResponse("2,9");
        });
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        var originalChat = service.ActivateChat;
        var history = SeedHistory(service);
        var firstReranker = new LlmReranker(service);
        var secondReranker = new LlmReranker(service);
        var alpha = Candidates("alpha", AlphaDocument);
        var first = firstReranker.RerankAsync("Alpha refund?", alpha);
        await firstEntered.Task.WaitAsync(TestTimeout);

        try
        {
            using var cancellation = new CancellationTokenSource();
            var waiting = secondReranker.RerankAsync("Cancelled query?",
                Candidates("cancelled", cancelledDocument), cancellation.Token);
            cancellation.Cancel();
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => waiting.WaitAsync(TestTimeout));

            Assert.AreEqual(cancellation.Token, exception.CancellationToken);
            Assert.AreEqual(1, handler.Requests.Count, "A cancelled waiting evaluation must not reach the transport.");
            Assert.IsFalse(first.IsCompleted, "Cancelling the waiting evaluation must not stop the active one.");
            releaseFirst.TrySetResult();
            AssertRanking(await first.WaitAsync(TestTimeout), alpha);
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        var beta = Candidates("beta", BetaDocument);
        AssertRanking(await secondReranker.RerankAsync("Beta refund?", beta).WaitAsync(TestTimeout), beta);
        var requests = handler.Requests.ToArray();
        Assert.AreEqual(2, requests.Length);
        AssertIsolatedRequest(requests[0], AlphaDocument, cancelledDocument, ExistingUser, ExistingAssistant);
        AssertIsolatedRequest(requests[1], BetaDocument, AlphaDocument, cancelledDocument, ExistingUser, ExistingAssistant);
        Assert.AreSame(originalChat, service.ActivateChat);
        AssertHistoryPreserved(service, history);
        Assert.IsFalse(service.StatelessMode);
    }

    private static OpenAIService CreateService(HttpClient client)
        => new("offline-reranker-test", AIModels.OpenAI.Gpt4o, client);

    private static Message[] SeedHistory(OpenAIService service)
    {
        var messages = new[]
        {
            new Message(ActorRole.User, ExistingUser),
            new Message(ActorRole.Assistant, ExistingAssistant)
        };
        foreach (var message in messages) service.ActivateChat.Messages.Add(message);
        return messages;
    }

    private static void AssertHistoryPreserved(OpenAIService service, Message[] history)
    {
        Assert.AreEqual(history.Length, service.ActivateChat.Messages.Count);
        Assert.AreSame(history[0], service.ActivateChat.Messages[0]);
        Assert.AreSame(history[1], service.ActivateChat.Messages[1]);
        Assert.AreEqual(ExistingUser, service.ActivateChat.Messages[0].Content);
        Assert.AreEqual(ExistingAssistant, service.ActivateChat.Messages[1].Content);
        Assert.AreEqual(ActorRole.User, service.ActivateChat.Messages[0].Role);
        Assert.AreEqual(ActorRole.Assistant, service.ActivateChat.Messages[1].Role);
    }

    private static VectorSearchResult[] Candidates(string tenant, string marker)
        => new[]
        {
            new VectorSearchResult(new VectorRecord(tenant + "-first", new float[] { 1, 0 }, marker + " first document"), 0.95),
            new VectorSearchResult(new VectorRecord(tenant + "-second", new float[] { 0, 1 }, marker + " second document"), 0.5)
        };

    private static void AssertRanking(IReadOnlyList<VectorSearchResult> ranked, IReadOnlyList<VectorSearchResult> original)
    {
        Assert.AreEqual(2, ranked.Count);
        Assert.AreSame(original[1].Record, ranked[0].Record);
        Assert.AreSame(original[0].Record, ranked[1].Record);
        Assert.AreEqual(0.9, ranked[0].Score, 0.000001);
        Assert.AreEqual(0.2, ranked[1].Score, 0.000001);
    }

    private static void AssertIsolatedRequest(JsonElement request, string included, params string[] excluded)
    {
        var payload = request.GetRawText();
        StringAssert.Contains(payload, included);
        foreach (var marker in excluded)
            Assert.IsFalse(payload.Contains(marker, StringComparison.Ordinal), "An unrelated conversation or evaluation leaked into the scoring request.");
        var messages = request.GetProperty("messages").EnumerateArray().ToArray();
        Assert.AreEqual(1, messages.Count(message => message.GetProperty("role").GetString() == "user"));
        Assert.AreEqual(0, messages.Count(message => message.GetProperty("role").GetString() == "assistant"));
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static HttpResponseMessage ScoreResponse(string scores)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { role = "assistant", content = scores }, finish_reason = "stop" } }
            }), Encoding.UTF8, "application/json")
        };

    private sealed class ScoringHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int _calls;
        public ConcurrentQueue<JsonElement> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Enqueue(document.RootElement.Clone());
            return await respond(Interlocked.Increment(ref _calls), cancellationToken);
        }
    }
}
