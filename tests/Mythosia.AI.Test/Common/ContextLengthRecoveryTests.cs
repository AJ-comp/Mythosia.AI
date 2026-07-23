using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// 컨텍스트 초과 반응형 복구 테스트 (API 키·네트워크 불필요).
///
/// 검증 대상: 서버가 "컨텍스트 넘쳤다" 고 거절하면 코어가 대화를 압축한 뒤 한 번 재전송하고,
/// 압축해도 전송량이 줄지 않는 경우에는 재시도하지 않고 원래 예외를 그대로 올린다는 것.
/// 압축은 메시지를 되돌릴 수 없게 지우므로, "줄지 않으면 재시도 금지" 가 이 기능의 안전선이다.
/// </summary>
[TestClass]
public class ContextLengthRecoveryTests
{
    #region Scripted Mock

    /// <summary>
    /// 호출마다 스크립트대로 응답하거나 예외를 던지는 AIService.
    /// 문자열이면 그 값을 반환하고, Exception 이면 그것을 던진다.
    /// </summary>
    private class ScriptedAIService : AIService
    {
        private readonly Queue<object> _script = new();

        public int CallCount { get; private set; }
        public List<int> MessageCountAtCall { get; } = new();
        public List<bool> WasStatelessOnCall { get; } = new();
        public List<uint> MaxRoundsAtCall { get; } = new();

        /// <summary>호출마다 실제로 전송됐을 메시지들. 요청 컨텍스트 적용 결과가 여기서 보인다.</summary>
        public List<List<string>> SentMessagesAtCall { get; } = new();

        public ScriptedAIService(params object[] script)
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
            foreach (var item in script)
                _script.Enqueue(item);
        }

        public override string Provider => nameof(AIProvider.OpenAI);

        public override Task<string> GetCompletionAsync(Message message)
        {
            CallCount++;
            MessageCountAtCall.Add(ActivateChat.Messages.Count);
            WasStatelessOnCall.Add(StatelessMode);

            // 공급자는 진입 즉시 CurrentPolicy 를 소비한다. 재시도 때 재주입되는지 보려고 기록해 둔다.
            var policy = CurrentPolicy ?? DefaultPolicy;
            MaxRoundsAtCall.Add((uint)policy.MaxRounds);
            CurrentPolicy = null;

            // StatelessMode 에서 공급자는 ChatBlock 을 통째로 갈아끼운다(OpenAIService.cs:88-93).
            // 요약 요청이 호출자 이력을 건드리지 않는 것도, 요약 요청에 실리는 메시지가 프롬프트 하나뿐인 것도
            // 이 교체 덕분이라 목도 똑같이 해야 한다.
            ChatBlock? callerChat = null;
            if (StatelessMode)
            {
                callerChat = ActivateChat;
                ActivateChat = new ChatBlock { SystemMessage = ActivateChat.SystemMessage };
            }

            try
            {
                // 실제 공급자는 HTTP 를 치기 '전에' 사용자 메시지를 이력에 넣고, 400 이 나도 되돌리지 않는다
                // (OpenAIService.cs:98 등 7개 공급자 전부). 되감기(TruncateMessagesTo)가 관측되려면 목도 그래야 한다.
                ActivateChat.Messages.Add(message);
                SentMessagesAtCall.Add(GetLatestMessages().Select(m => m.GetDisplayText()).ToList());

                if (_script.Count == 0)
                    throw new AIServiceException("No more scripted responses");

                var next = _script.Dequeue();

                // 도구를 실행한 뒤 실패한 라운드. 공급자가 남기는 흔적을 그대로 만든다.
                if (next is ToolRanThenFailed toolRun)
                {
                    ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "call send_mail"));
                    ActivateChat.Messages.Add(new Message(ActorRole.Function, "mail sent"));
                    throw toolRun.Error;
                }

                if (next is Exception ex) throw ex;

                var response = (string)next;
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, response));
                return Task.FromResult(response);
            }
            finally
            {
                if (callerChat != null) ActivateChat = callerChat;
            }
        }

        protected override HttpRequestMessage CreateMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override string ExtractResponseContent(string responseContent) => responseContent;

        protected override string StreamParseJson(string jsonData) => jsonData;

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
            => (response, null!);

        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);

        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);

        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(Array.Empty<byte>());

        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(string.Empty);

        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => Task.CompletedTask;
    }

    /// <summary>도구를 한 번 실행하고 나서 컨텍스트 초과로 거절당한 호출.</summary>
    private sealed class ToolRanThenFailed
    {
        public ToolRanThenFailed(Exception error) => Error = error;
        public Exception Error { get; }
    }

    private static ContextLengthExceededException Overflow(string detail = "prompt too large")
        => new(
            "API request failed (400): Bad Request",
            detail,
            statusCode: 400,
            maxContextTokens: 50000,
            requestedTokens: 50001);

    /// <summary>실제로 잘라낼 게 남도록 대화를 채운다.</summary>
    private static void SeedHistory(AIService service, int turns)
    {
        for (int i = 0; i < turns; i++)
        {
            service.ActivateChat.Messages.Add(new Message(ActorRole.User, $"user {i}"));
            service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, $"assistant {i}"));
        }
    }

    /// <summary>도구 라운드가 남기는 흔적. 라운드마다 assistant 호출 + function 결과 2개가 붙는다.</summary>
    private static void SeedAgentRounds(AIService service, int rounds)
    {
        for (int i = 0; i < rounds; i++)
        {
            service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, $"call {i}"));
            service.ActivateChat.Messages.Add(new Message(ActorRole.Function, $"result {i}"));
        }
    }

    #endregion

    #region 복구 성공 경로

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task ContextExceeded_CompactsAndRetriesOnce()
    {
        // 1) 원 요청 거절 → 2) 요약 호출 → 3) 재시도 성공
        var service = new ScriptedAIService(Overflow(), "summary text", "final answer");
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedHistory(service, 5);

        var result = await service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null);

        Assert.AreEqual("final answer", result);
        Assert.AreEqual(3, service.CallCount, "원요청 + 요약 + 재시도 = 3회여야 한다");
        Assert.IsTrue(service.WasStatelessOnCall[1], "요약 요청은 stateless 로 나가야 한다");
        Assert.IsTrue(
            service.MessageCountAtCall[2] < service.MessageCountAtCall[0],
            "재시도는 압축된(더 짧은) 대화로 나가야 한다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task Retry_DoesNotDuplicateUserMessage()
    {
        // 실패한 시도도 사용자 메시지를 이력에 남긴다(공급자가 전송 전에 넣고 400 이 나도 안 지운다).
        // 되감지 않으면 재시도가 같은 질문을 한 번 더 얹어 요청이 오히려 커진다.
        var service = new ScriptedAIService(Overflow(), "summary text", "final answer");
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedHistory(service, 5);

        await service.GetCompletionAsync(new Message(ActorRole.User, "unique-question"), null, null);

        var occurrences = service.ActivateChat.Messages.Count(m => m.Content == "unique-question");
        Assert.AreEqual(1, occurrences, "실패한 시도가 남긴 메시지는 되감겨야 한다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task MultipleRetries_RewindEveryFailedAttempt()
    {
        // 되감기 기준을 턴 시작에서 한 번만 잡으면, 압축이 이력을 줄인 뒤로는 그 기준이 현재 길이보다 커서
        // 되감기가 조용히 무효화된다. 그러면 두 번째 실패가 남긴 질문이 살아남고, 압축 클램프가 '마지막
        // User 는 자르지 않는다' 며 그걸 보호해, 세 번째 시도가 같은 질문을 또 얹는다.
        //
        // 기준을 시도마다 새로 잡으면 두 번째 시도도 깨끗이 되감기고, 남은 것은 keepRecent 개뿐이라
        // 두 번째 압축은 자를 게 없다고 정직하게 포기한다.
        var service = new ScriptedAIService(Overflow(), "summary 1", Overflow("second"));
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        service.ContextRecoveryMaxRetries = 2;
        SeedHistory(service, 5);

        var ex = await Assert.ThrowsExactlyAsync<ContextLengthExceededException>(
            () => service.GetCompletionAsync(new Message(ActorRole.User, "unique-question"), null, null));

        Assert.AreEqual("nothing-to-cut", ex.RecoverySkipReason);
        Assert.AreEqual(3, service.CallCount, "원요청 + 요약 + 재시도 = 3회에서 멈춰야 한다");
        Assert.AreEqual(0, service.ActivateChat.Messages.Count(m => m.Content == "unique-question"),
            "실패한 시도가 남긴 메시지는 하나도 남으면 안 된다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task CutPointOnFunctionResult_KeepsItsOriginatingCall()
    {
        // 자르는 지점이 function 결과에 떨어지면, 그 결과를 만든 assistant 호출이 잘려나간다.
        // 남는 건 주인 없는 tool 결과 — chat/completions 규격 위반이라 서버가 400 으로 거절하고,
        // 복구는 그 400 을 또 컨텍스트 초과로 보고 헛돌게 된다.
        //
        // 클램프가 마지막 User 로 당기는 것만으로는 못 막는다. keepFromIndex 가 이미 그 앞에 있으면
        // 클램프는 움직이지 않기 때문 — 즉 지난 턴의 도구 흔적 위에 떨어질 수 있다.
        var service = new ScriptedAIService(Overflow(), "summary text", "final answer");
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 5, keepRecentCount: 4);

        var m = service.ActivateChat.Messages;
        m.Add(new Message(ActorRole.User, "old question"));
        m.Add(new Message(ActorRole.Assistant, "call get_weather"));  // [1] 이게 잘리면
        m.Add(new Message(ActorRole.Function, "sunny"));              // [2] 얘가 고아가 된다
        m.Add(new Message(ActorRole.Assistant, "it is sunny"));
        m.Add(new Message(ActorRole.User, "recent question"));
        m.Add(new Message(ActorRole.Assistant, "recent answer"));

        await service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null);

        var remaining = service.ActivateChat.Messages;
        Assert.AreNotEqual(ActorRole.Function, remaining[0].Role,
            "주인 없는 function 결과가 대화 맨 앞에 남으면 안 된다");

        for (int i = 0; i < remaining.Count; i++)
        {
            if (remaining[i].Role != ActorRole.Function) continue;
            Assert.IsTrue(i > 0 && remaining[i - 1].Role == ActorRole.Assistant,
                $"[{i}] function 결과 앞에는 그것을 호출한 assistant 턴이 있어야 한다");
        }
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task ToolAlreadyRan_DoesNotRetry()
    {
        // 재시도는 공급자의 라운드 루프를 0번부터 다시 돌린다. 이미 실행된 도구가 부작용을 가진 것이면
        // (메일 발송·레코드 생성) 두 번 실행된다. 어느 도구가 안전한지 여기서는 알 수 없으므로 멈춘다.
        var service = new ScriptedAIService(new ToolRanThenFailed(Overflow()), "summary text", "final answer");
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedHistory(service, 5);

        var ex = await Assert.ThrowsExactlyAsync<ContextLengthExceededException>(
            () => service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null));

        Assert.AreEqual("tool-side-effects", ex.RecoverySkipReason);
        Assert.AreEqual(1, service.CallCount, "도구를 다시 실행시킬 재시도를 하면 안 된다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task ForcedCompaction_IgnoresCallerRequestContext()
    {
        // RagEnabledService 처럼 RequestMessageOverride 를 실어 보내는 호출자가 있다. 그 오버라이드는
        // 모든 요청의 마지막 메시지를 갈아치우는데, 요약 요청은 메시지가 프롬프트 하나뿐이다.
        // 컨텍스트가 살아 있으면 요약 대신 호출자의 질문이 나가고, 그 답이 '요약'으로 저장된다.
        var service = new ScriptedAIService(Overflow(), "summary text", "final answer");
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedHistory(service, 5);

        var context = new AIRequestContext
        {
            RequestMessageOverride = new Message(ActorRole.User, "RAG-REWRITTEN-QUERY")
        };

        await service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, context);

        var summarySent = service.SentMessagesAtCall[1];
        Assert.AreEqual(1, summarySent.Count, "요약은 stateless — 프롬프트 하나만 나가야 한다");
        StringAssert.StartsWith(summarySent[0], "Please summarize",
            "요약 프롬프트가 호출자 오버라이드로 교체되면 저장되는 건 요약이 아니라 일반 답변이다");

        Assert.AreEqual("RAG-REWRITTEN-QUERY", service.SentMessagesAtCall[2][^1],
            "호출자 컨텍스트는 요약 뒤 복원돼 재시도에는 그대로 적용돼야 한다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task MessageChainSend_GoesThroughRecovery()
    {
        // 이력을 유지하는 fluent 전송. 오버로드 해석상 복구를 안 타는 1인자 오버로드로 붙기 쉽다.
        var service = new ScriptedAIService(Overflow(), "summary text", "final answer");
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedHistory(service, 5);

        var result = await service.BeginMessage().AddText("question").SendAsync();

        Assert.AreEqual("final answer", result);
        Assert.AreEqual(3, service.CallCount, "원요청 + 요약 + 재시도 = 3회");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task Retry_RestoresCurrentPolicy()
    {
        var service = new ScriptedAIService(Overflow(), "summary text", "final answer");
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedHistory(service, 5);
        service.CurrentPolicy = new FunctionCallingPolicy { MaxRounds = 7 };

        await service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null);

        Assert.AreEqual(7u, service.MaxRoundsAtCall[0], "원요청이 주입된 정책을 써야 한다");
        Assert.AreEqual(7u, service.MaxRoundsAtCall[2],
            "재시도도 같은 정책이어야 한다 — 재주입이 없으면 DefaultPolicy 로 조용히 바뀐다");
    }

    #endregion

    #region 복구 불가 → 원래 예외 전파

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task NoConversationPolicy_DoesNotRetry()
    {
        // 요약 정책이 없으면 압축할 저장소 자체가 없다. 재시도하면 같은 400 을 두 번 맞을 뿐이다.
        var service = new ScriptedAIService(Overflow());
        service.ConversationPolicy = null;
        SeedHistory(service, 5);

        var ex = await Assert.ThrowsExactlyAsync<ContextLengthExceededException>(
            () => service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null));

        Assert.AreEqual(1, service.CallCount);
        Assert.AreEqual("no-policy", ex.RecoverySkipReason);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task KeepRecentCoversEverything_DoesNotRetry()
    {
        // keepRecent 가 전체를 덮으면 지울 게 없다 → 재시도해도 같은 크기로 나간다.
        // (ByMessage 는 keepRecent < trigger 를 강제하므로 둘 다 대화 길이보다 크게 잡는다)
        var service = new ScriptedAIService(Overflow(), "summary text");
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 1000, keepRecentCount: 999);
        SeedHistory(service, 3);

        var ex = await Assert.ThrowsExactlyAsync<ContextLengthExceededException>(
            () => service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null));

        Assert.AreEqual(1, service.CallCount, "요약조차 시도하면 안 된다");
        Assert.AreEqual("nothing-to-cut", ex.RecoverySkipReason);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task WindowAlreadyClipsTheCut_SkipsBeforeSpendingAnything()
    {
        // 긴 에이전트 실행의 기본 형태: 질문 하나 뒤에 도구 라운드가 창(MaxMessageCount)을 가득 채운 상태.
        // 잘라낼 수 있는 건 그 앞의 옛 대화뿐인데 그건 이미 창 밖이라 요청에 실리지도 않는다.
        // 지워봐야 요청은 한 글자도 안 줄어들므로, 요약을 부르기도 전에 포기해야 한다.
        var service = new ScriptedAIService(Overflow());
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 20, keepRecentCount: 10);
#pragma warning disable CS0618 // MaxMessageCount — deprecated window kept until v7.0
        service.MaxMessageCount = 20;
#pragma warning restore CS0618

        SeedHistory(service, 20);                                              // 옛 대화 40개 (창 밖)
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "anchor"));
        SeedAgentRounds(service, 12);                                          // 도구 24개 (창 안, 자를 수 없음)
        var messagesBefore = service.ActivateChat.Messages.Count;

        var ex = await Assert.ThrowsExactlyAsync<ContextLengthExceededException>(
            () => service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null));

        Assert.AreEqual("window-clipped", ex.RecoverySkipReason);
        Assert.AreEqual(1, service.CallCount, "판정은 순수 산술이다 — 요약 요청을 보내면 안 된다");
        Assert.AreEqual(messagesBefore, service.ActivateChat.Messages.Count,
            "포기하는 경로가 이력을 지우면, 사용자는 대화를 잃고도 같은 에러를 받는다");
        Assert.IsNull(service.ConversationPolicy.CurrentSummary);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task RetryAlsoFails_PropagatesAndSummarizesOnlyOnce()
    {
        var service = new ScriptedAIService(Overflow(), "summary text", Overflow("second"));
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedHistory(service, 5);

        var ex = await Assert.ThrowsExactlyAsync<ContextLengthExceededException>(
            () => service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null));

        Assert.AreEqual(3, service.CallCount, "무한 재시도가 아니라 정확히 1회만 복구해야 한다");
        Assert.AreEqual(1, ex.RecoveryAttempts);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task SummaryRequestItselfOverflows_PropagatesOriginalError()
    {
        // 요약 요청이 또 넘치는 경우. 사용자에게 보고돼야 하는 건 '원래' 실패다.
        var service = new ScriptedAIService(Overflow("original"), Overflow("from-summary"));
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedHistory(service, 5);

        var ex = await Assert.ThrowsExactlyAsync<ContextLengthExceededException>(
            () => service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null));

        Assert.AreEqual("original", ex.ErrorDetails,
            "요약 쪽 예외로 원인이 바뀌면 진단이 불가능해진다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task ContextRecoveryDisabled_PropagatesImmediately()
    {
        var service = new ScriptedAIService(Overflow());
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        service.ContextRecoveryMaxRetries = 0;   // 킬스위치
        SeedHistory(service, 5);

        var ex = await Assert.ThrowsExactlyAsync<ContextLengthExceededException>(
            () => service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null));

        Assert.AreEqual(1, service.CallCount);
        Assert.AreEqual("recovery-disabled", ex.RecoverySkipReason,
            "예산 소진과 애초에 꺼둔 것은 원인이 다르다 — 둘 다 null 이면 구분할 수 없다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task RetriesExhausted_ReportsDistinctReason()
    {
        var service = new ScriptedAIService(Overflow(), "summary text", Overflow("second"));
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedHistory(service, 5);

        var ex = await Assert.ThrowsExactlyAsync<ContextLengthExceededException>(
            () => service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null));

        Assert.AreEqual("retries-exhausted", ex.RecoverySkipReason);
        Assert.AreEqual(1, ex.RecoveryAttempts);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task OtherAIServiceException_IsNotRetried()
    {
        // 컨텍스트 초과가 아닌 실패에 압축이 돌면 사용자 이력을 이유 없이 지우게 된다.
        var service = new ScriptedAIService(new AIServiceException("API request failed (400): Bad Request", "bad param"));
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedHistory(service, 5);

        await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync(new Message(ActorRole.User, "question"), null, null));

        Assert.AreEqual(1, service.CallCount);
    }

    #endregion
}
