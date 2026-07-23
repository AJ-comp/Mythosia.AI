using System.Runtime.CompilerServices;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// 스트리밍 경로의 컨텍스트 초과 복구 테스트 (API 키·네트워크 불필요).
///
/// 복구는 턴 바깥이 아니라 <b>라운드 안</b>에 있다. 서버는 추론을 시작하기 전에 거절하므로
/// 초과 에러는 언제나 그 라운드의 첫 청크다 — 즉 그 라운드는 아직 아무것도 내보내지 않았고,
/// 앞 라운드가 만든 도구 결과를 버리지 않고 그 라운드만 다시 칠 수 있다.
///
/// 동시에, 지울 게 없으면 즉시 포기해야 한다. 지금 작업에 필요한 도구 결과까지 지우면
/// AI 가 답을 못 만들기 때문에, 억지 복구보다 정직한 실패가 맞다.
/// </summary>
[TestClass]
public class ContextLengthStreamingRecoveryTests
{
    #region Scripted streaming mock

    /// <summary>라운드별로 무엇을 내보낼지 지정하는 스크립트 한 줄.</summary>
    private enum RoundScript
    {
        /// <summary>도구를 한 번 호출한 것처럼 굴어 다음 라운드로 넘어간다.</summary>
        ToolCall,

        /// <summary>컨텍스트 초과 에러 청크를 첫 청크로 내보낸다.</summary>
        ContextOverflow,

        /// <summary>컨텍스트와 무관한 400 에러 청크를 내보낸다.</summary>
        PlainError,

        /// <summary>텍스트를 내보내고 턴을 끝낸다.</summary>
        FinalText,
    }

    private class ScriptedStreamingService : AIService
    {
        private readonly Queue<RoundScript> _rounds = new();

        public int RoundCallCount { get; private set; }
        public int SummaryCallCount { get; private set; }
        public List<int> MessageCountAtRound { get; } = new();

        /// <summary>
        /// 각 라운드가 시작될 때까지 누적된 요약 호출 수.
        /// 턴이 정상 종료될 때도 요약이 한 번 도므로(평소 요약), 총합만 보면 복구 압축과 구분되지 않는다.
        /// 라운드 시작 시점의 값이 늘어났다는 것은 <b>그 사이에 복구 압축이 돌았다</b>는 뜻이다.
        /// </summary>
        public List<int> SummaryCountAtRound { get; } = new();

        /// <summary>복구 때문에 돈 압축 횟수 (마지막 라운드 기준).</summary>
        public int RecoveryCompactionCount
            => SummaryCountAtRound.Count == 0 ? 0 : SummaryCountAtRound[^1];

        public ScriptedStreamingService(params RoundScript[] rounds)
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
            foreach (var r in rounds)
                _rounds.Enqueue(r);
        }

        public override string Provider => nameof(AIProvider.OpenAI);

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options,
            bool useFunctions,
            FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RoundCallCount++;
            MessageCountAtRound.Add(ActivateChat.Messages.Count);
            SummaryCountAtRound.Add(SummaryCallCount);
            await Task.Yield();

            var script = _rounds.Count > 0 ? _rounds.Dequeue() : RoundScript.FinalText;

            switch (script)
            {
                case RoundScript.ContextOverflow:
                    // 실제 공급자와 같은 모양: HTTP 실패는 이 라운드의 첫 청크로 나온다.
                    yield return new StreamingContent
                    {
                        Type = StreamingContentType.Error,
                        Content = "API error (400): context too long",
                        Metadata = AIHttpErrorFactory.BuildErrorMetadata(400, VllmOverflowBody),
                    };
                    yield break;

                case RoundScript.PlainError:
                    yield return new StreamingContent
                    {
                        Type = StreamingContentType.Error,
                        Content = "API error (400): bad parameter",
                        Metadata = AIHttpErrorFactory.BuildErrorMetadata(
                            400, """{"error":{"message":"Invalid value for 'temperature'"}}"""),
                    };
                    yield break;

                case RoundScript.ToolCall:
                    ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "calling tool"));
                    ActivateChat.Messages.Add(new Message(ActorRole.Function, "tool output"));
                    yield return new StreamingContent
                    {
                        Type = StreamingContentType.FunctionResult,
                        Content = "tool output",
                    };
                    yield break;

                default:
                    ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "final"));
                    yield return new StreamingContent
                    {
                        Type = StreamingContentType.Text,
                        Content = "final",
                    };
                    yield break;
            }
        }

        // 요약 요청은 스트리밍이 아니라 이쪽으로 나간다.
        public override Task<string> GetCompletionAsync(Message message)
        {
            SummaryCallCount++;
            return Task.FromResult("summary text");
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

    private const string VllmOverflowBody =
        """{"error":{"message":"This model's maximum context length is 50000 tokens. However, your prompt contains at least 50001 input tokens","type":"BadRequestError"}}""";

    private static async Task<List<StreamingContent>> DrainAsync(AIService service, string prompt)
    {
        var collected = new List<StreamingContent>();
        await foreach (var chunk in service.StreamAsync(
            new Message(ActorRole.User, prompt), StreamOptions.Default))
        {
            collected.Add(chunk);
        }
        return collected;
    }

    /// <summary>
    /// 에러 청크가 나온 시점의 요약 호출 수를 함께 잡는다. 턴이 끝나면 평소 요약이 한 번 더 돌기 때문에,
    /// 총합만 보면 "복구가 요약 비용을 썼는지" 를 알 수 없다.
    /// </summary>
    private static async Task<(List<StreamingContent> chunks, int summaryCallsAtError)> DrainWithErrorSnapshotAsync(
        ScriptedStreamingService service, string prompt)
    {
        var collected = new List<StreamingContent>();
        int atError = -1;

        await foreach (var chunk in service.StreamAsync(
            new Message(ActorRole.User, prompt), StreamOptions.Default))
        {
            if (chunk.Type == StreamingContentType.Error && atError < 0)
                atError = service.SummaryCallCount;
            collected.Add(chunk);
        }

        return (collected, atError);
    }

    private static void SeedOldHistory(AIService service, int turns)
    {
        for (int i = 0; i < turns; i++)
        {
            service.ActivateChat.Messages.Add(new Message(ActorRole.User, $"old question {i}"));
            service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, $"old answer {i}"));
        }
    }

    #endregion

    #region 라운드 중간 초과 → 그 라운드만 재시도

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task MidRoundOverflow_CompactsAndReplaysOnlyThatRound()
    {
        // 라운드1·2 는 도구 호출, 라운드3 이 초과 → 압축 후 라운드3만 다시 → 성공
        var service = new ScriptedStreamingService(
            RoundScript.ToolCall,
            RoundScript.ToolCall,
            RoundScript.ContextOverflow,
            RoundScript.FinalText);
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedOldHistory(service, 6);

        var chunks = await DrainAsync(service, "question");

        Assert.AreEqual(1, service.RecoveryCompactionCount, "복구 압축은 한 번만 돌아야 한다");
        Assert.AreEqual(4, service.RoundCallCount, "라운드3 만 다시 쳐서 총 4회여야 한다");
        Assert.IsFalse(
            chunks.Any(c => c.Type == StreamingContentType.Error),
            "복구에 성공했으면 에러 청크가 사용자에게 나가면 안 된다");
        Assert.IsTrue(
            chunks.Any(c => c.Type == StreamingContentType.Text && c.Content == "final"),
            "복구 후 정상 응답이 이어져야 한다");

        // 호출 횟수만으로는 '그 라운드만 재생' 과 '턴 전체 재시작' 이 구분되지 않는다. 결정적인 건
        // 재생 라운드가 무엇을 들고 시작하느냐다: 질문(1) + 라운드1·2 의 호출/결과 쌍(4) = 5.
        // 턴 전체를 다시 돌리는 구현이면 그 4개를 버리고 시작하므로 이 값이 1이 된다.
        // (턴이 끝난 뒤 평소 요약이 한 번 더 돌아 이력을 다시 줄이므로, 종료 후 메시지 수로는 볼 수 없다.)
        Assert.AreEqual(5, service.MessageCountAtRound[3],
            "재생 라운드는 앞 라운드 도구 결과를 그대로 들고 시작해야 한다");
        Assert.AreEqual(2, chunks.Count(c => c.Type == StreamingContentType.FunctionResult),
            "이미 내보낸 도구 결과가 사용자에게 두 번 나가면 안 된다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task MidRoundOverflow_KeepsEarlierRoundToolResults()
    {
        // 앞 라운드가 만든 도구 결과는 압축 대상이 아니다 — 지금 질문 뒤에 있기 때문.
        var service = new ScriptedStreamingService(
            RoundScript.ToolCall,
            RoundScript.ContextOverflow,
            RoundScript.FinalText);
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedOldHistory(service, 6);

        await DrainAsync(service, "question");

        Assert.IsTrue(
            service.ActivateChat.Messages.Any(m => m.Content == "tool output"),
            "앞 라운드 도구 결과가 살아 있어야 재실행 없이 이어갈 수 있다");
        Assert.IsFalse(
            service.ActivateChat.Messages.Any(m => m.Content == "old question 0"),
            "압축 대상은 지금 질문 이전의 옛날 대화다");
    }

    #endregion

    #region 자를 게 없으면 즉시 포기 (무한루프 방지)

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task NoOldHistory_MidRoundOverflow_GivesUpWithoutSummarizing()
    {
        // 옛날 대화가 없으면 지금 질문 뒤만 남는다 → 자를 게 없다 →
        // 요약 요청조차 보내지 않고 에러를 그대로 올린다.
        var service = new ScriptedStreamingService(
            RoundScript.ToolCall,
            RoundScript.ToolCall,
            RoundScript.ContextOverflow,
            RoundScript.FinalText);
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        // 옛날 대화 없음

        var (chunks, summaryCallsAtError) = await DrainWithErrorSnapshotAsync(service, "question");

        Assert.AreEqual(0, summaryCallsAtError,
            "지울 게 없다는 건 보내기 전에 알 수 있다 — 요약 비용을 쓰고 알아내면 안 된다");
        Assert.AreEqual(3, service.RoundCallCount, "재시도 없이 라운드3 에서 멈춰야 한다");
        Assert.IsTrue(
            chunks.Any(c => c.Type == StreamingContentType.Error),
            "복구가 불가능하면 오늘과 동일하게 에러를 그대로 보여준다");

        // 복구 여부와 무관하게 스트림은 같은 방식으로 끝나야 한다. 여기서 이터레이터를 끊어버리면
        // 종단 신호(사용량 확정)가 설정값에 따라 있다 없다 하게 된다.
        Assert.IsTrue(
            chunks.Any(c => c.Type == StreamingContentType.Completion),
            "복구를 포기해도 종단 Completion 청크는 나가야 한다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task RepeatedOverflow_IsBounded()
    {
        // 압축 후에도 계속 넘치는 경우. 라운드당 재시도는 1회로 묶여 있어야 한다.
        var service = new ScriptedStreamingService(
            RoundScript.ContextOverflow,
            RoundScript.ContextOverflow,
            RoundScript.ContextOverflow,
            RoundScript.ContextOverflow);
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedOldHistory(service, 6);

        var chunks = await DrainAsync(service, "question");

        Assert.AreEqual(2, service.RoundCallCount, "원래 + 재시도 1회 = 2회에서 멈춰야 한다");
        Assert.AreEqual(1, service.RecoveryCompactionCount);
        Assert.IsTrue(chunks.Any(c => c.Type == StreamingContentType.Error));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task OverflowInSeveralRounds_CompactsOnlyOnce()
    {
        // 재시도 예산은 라운드마다 새로 주어지므로, 산술적으로는 한 턴에서
        // MaxRounds × ContextRecoveryMaxRetries 번 압축이 돌 수 있다.
        //
        // 실제로는 한 번뿐이다. 자르는 지점이 항상 '지금 질문' 까지 당겨지므로 그 앞의 옛 대화는
        // 첫 압축에서 통째로 접힌다. 두 번째 초과에는 남은 게 없어 요약 비용 없이 즉시 포기한다.
        var service = new ScriptedStreamingService(
            RoundScript.ToolCall,
            RoundScript.ContextOverflow,   // 라운드1 초과 → 압축 → 재생
            RoundScript.ToolCall,
            RoundScript.ContextOverflow,   // 라운드2 초과 → 이번엔 자를 게 없음
            RoundScript.FinalText);
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        SeedOldHistory(service, 6);

        var (chunks, summaryCallsAtError) = await DrainWithErrorSnapshotAsync(service, "question");

        Assert.IsTrue(chunks.Any(c => c.Type == StreamingContentType.Error),
            "두 번째 초과는 복구할 수 없으므로 에러가 사용자에게 나가야 한다");
        Assert.AreEqual(1, summaryCallsAtError,
            "두 번째 초과는 접을 옛 대화가 없다 — 요약을 또 부르면 안 된다");
        Assert.AreEqual(4, service.RoundCallCount,
            "라운드0, 라운드1(초과), 라운드1(재생), 라운드2(초과) = 4회");
        Assert.IsTrue(chunks.Any(c => c.Type == StreamingContentType.Completion));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task RecoveryDisabled_YieldsErrorImmediately()
    {
        var service = new ScriptedStreamingService(
            RoundScript.ContextOverflow,
            RoundScript.FinalText);
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 4, keepRecentCount: 2);
        service.ContextRecoveryMaxRetries = 0;   // 킬스위치
        SeedOldHistory(service, 6);

        var (chunks, summaryCallsAtError) = await DrainWithErrorSnapshotAsync(service, "question");

        Assert.AreEqual(1, service.RoundCallCount);
        Assert.AreEqual(0, summaryCallsAtError, "킬스위치가 켜지면 복구 압축이 없어야 한다");
        Assert.IsTrue(chunks.Any(c => c.Type == StreamingContentType.Error));
        Assert.IsTrue(
            chunks.Any(c => c.Type == StreamingContentType.Completion),
            "킬스위치 경로와 포기 경로의 종단 계약이 갈리면 안 된다");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task NonContextError_IsNotRecovered()
    {
        // 컨텍스트 초과가 아닌 에러에 압축이 돌면 이유 없이 대화를 지우게 된다.
        // 실제로 스트리밍을 돌려서 확인해야 한다 — 팩토리만 따로 부르면 IsContextOverflowChunk 가
        // 검사를 안 해도 통과한다.
        var service = new ScriptedStreamingService(RoundScript.PlainError);
        // 트리거를 일부러 높게 잡아 턴 종료 후의 평소 요약을 배제한다. 그게 돌면 이력이 줄어드는 원인이
        // 둘이 되어 "복구가 지웠는가" 를 볼 수 없다. 강제 압축은 트리거를 보지 않으므로 검증에는 영향 없다.
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 1000, keepRecentCount: 2);
        SeedOldHistory(service, 6);
        var historyBefore = service.ActivateChat.Messages.Count;

        var (chunks, summaryCallsAtError) = await DrainWithErrorSnapshotAsync(service, "question");

        Assert.AreEqual(1, service.RoundCallCount, "무관한 400 에 재시도가 붙으면 안 된다");
        Assert.AreEqual(0, summaryCallsAtError, "무관한 400 에 압축이 돌면 안 된다");
        Assert.IsTrue(chunks.Any(c => c.Type == StreamingContentType.Error));
        Assert.IsTrue(
            service.ActivateChat.Messages.Count > historyBefore,
            "옛 대화가 삭제되면 안 된다 — 잘못 지운 이력은 되돌릴 수 없다");
    }

    #endregion
}
