using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AIRunResultTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    [DataRow("unobserved")]
    [DataRow("text-only")]
    [DataRow("no-metadata")]
    [DataRow("overflow")]
    public async Task Result_RetainsDetailsRegardlessOfObservation(string observation)
    {
        var usage = Usage(10, 4, cached: 3, creation: 2, reasoning: 1);
        var citation = new AICitation { Provider = "Probe", Url = "https://example.com/source", Title = "Source", ResponseId = "response-1" };
        var textCount = observation == "overflow" ? 1500 : 1;
        var chunks = Enumerable.Range(0, textCount)
            .Select(_ => new StreamingContent { Type = StreamingContentType.Text, Content = "x" })
            .ToList();
        chunks.Add(new StreamingContent { Type = StreamingContentType.Citation, Citation = citation });
        chunks.Add(Completed(usage, "actual-model-version", AIFinishReason.MaxTokens, "length"));
        var service = new ResultProbeService(chunks.ToArray());
        var options = observation == "text-only"
            ? StreamOptions.TextOnlyOptions
            : new StreamOptions { IncludeMetadata = observation != "no-metadata" };

        await using var run = await service.StartRunAsync("question", options: options);
        var events = observation is "text-only" or "no-metadata" ? await CollectAsync(run) : null;
        var result = await run.Result.WaitAsync(TestTimeout);

        Assert.AreEqual(new string('x', textCount), result.Text);
        Assert.AreEqual("Probe", result.Provider);
        Assert.AreEqual("requested-alias", result.RequestedModel);
        Assert.AreEqual("actual-model-version", result.Model);
        Assert.AreEqual(1, result.RoundCount);
        Assert.AreEqual(AIFinishReason.MaxTokens, result.FinishReason);
        Assert.AreEqual("length", result.RawFinishReason);
        AssertUsage(result.Usage, 10, 4, 14, 3, 2, 1);
        Assert.AreEqual("Source", result.Citations.Single().Title);
        Assert.AreEqual("response-1", result.Citations.Single().ResponseId);
        Assert.AreSame(result, await run.Result);
        if (observation == "text-only")
            Assert.IsTrue(events!.All(item => item.Type == StreamingContentType.Text));
        if (observation == "no-metadata")
            Assert.IsTrue(events!.All(item => item.Metadata == null));
        if (observation == "overflow")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(run));
            Assert.IsTrue(run.Result.IsCompletedSuccessfully);
        }
    }

    [TestMethod]
    public async Task Result_UsesLastUsagePerRound_AndDoesNotAddAggregateAgain()
    {
        var call = new FunctionCall { Id = "lookup-1", Name = "lookup", Arguments = new Dictionary<string, object>() };
        var service = new ResultProbeService(
            new[]
            {
                new StreamingContent { Type = StreamingContentType.Text, Content = "checking ", Usage = Usage(1, 1) },
                new StreamingContent { Type = StreamingContentType.FunctionResult, FunctionResult = new FunctionCallResult { Call = call, Content = "tool result" } },
                Completed(Usage(10, 3, 2, 4, 1), "model-round-1", AIFinishReason.ToolCalls, "tool_calls")
            },
            new[]
            {
                new StreamingContent { Type = StreamingContentType.Text, Content = "answer", Usage = Usage(15, 1) },
                Completed(Usage(20, 5, 7, 2, 3), "model-round-2", AIFinishReason.Stop, "stop")
            });
        await using var run = await service.WithMaxRounds(2).StartRunAsync("question");
        var events = await CollectAsync(run);
        var result = await run.Result;

        Assert.AreEqual("checking answer", result.Text);
        Assert.AreEqual(2, result.RoundCount);
        Assert.AreEqual(2, service.RoundsStarted);
        Assert.AreEqual("model-round-2", result.Model);
        Assert.AreEqual(AIFinishReason.Stop, result.FinishReason);
        Assert.AreEqual("stop", result.RawFinishReason);
        AssertUsage(result.Usage, 30, 8, 38, 9, 6, 4);
        Assert.AreEqual(2, events.Count(item => item.Type == StreamingContentType.RoundUsage));
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Result_DistinguishesUnreportedUsageFromReportedZero(bool reported)
    {
        var service = new ResultProbeService(new[] { Completed(reported ? new TokenUsage() : null) });
        await using var run = await service.StartRunAsync("question");
        var result = await run.Result;

        if (reported) AssertUsage(result.Usage, 0, 0, 0, 0, 0, 0);
        else Assert.IsNull(result.Usage);
        Assert.AreEqual(string.Empty, result.Text);
        Assert.AreEqual(1, result.RoundCount);
        Assert.IsNull(result.Model, "A requested alias is not evidence of the actual server model.");
        Assert.AreEqual("requested-alias", result.RequestedModel);
        Assert.AreEqual(AIFinishReason.Unknown, result.FinishReason);
        Assert.IsNull(result.RawFinishReason);
    }

    [TestMethod]
    public async Task Result_UsesBuilderRequestedModelSnapshot()
    {
        var service = new ResultProbeService(new[] { Completed(model: "resolved-version") });
        var request = service.CreateRequest("question");
        service.ChangeModel("changed-service-default");
        await using var run = await request.StartRunAsync();
        var result = await run.Result;

        Assert.AreEqual("requested-alias", result.RequestedModel);
        Assert.AreEqual("resolved-version", result.Model);
        Assert.AreEqual("changed-service-default", service.Model);
    }

    [TestMethod]
    public async Task Result_CannotBeChangedThroughPublishedUsageOrCitations_OrNextRun()
    {
        var usage = Usage(9, 2, 1, 3, 1);
        var citation = new AICitation { Provider = "Probe", Title = "original", Url = "https://example.com/original" };
        var service = new ResultProbeService(new[]
        {
            new StreamingContent { Type = StreamingContentType.Citation, Citation = citation },
            Completed(usage)
        });
        await using var first = await service.StartRunAsync("first");
        var firstResult = await first.Result;
        var observed = await CollectAsync(first);
        foreach (var item in observed)
        {
            if (item.Usage != null) item.Usage.InputTokens = 900;
            if (item.Citation != null) item.Citation.Title = "observer mutation";
        }
        usage.InputTokens = 800;
        citation.Title = "provider mutation";
        firstResult.Usage!.InputTokens = 700;
        firstResult.Citations[0].Title = "caller mutation";
        first.Citations[0].Title = "run getter mutation";

        await using var next = await service.StartRunAsync("second");
        await next.Result;

        AssertUsage(firstResult.Usage, 9, 2, 11, 1, 3, 1);
        Assert.AreEqual("original", firstResult.Citations.Single().Title);
        Assert.AreEqual("https://example.com/original", firstResult.Citations.Single().Url);
    }

    [TestMethod]
    public void ResultConstructor_CopiesAllMutableFieldsOnInputAndAccess()
    {
        var usage = Usage(10, 4, 2, 3, 1);
        usage.TotalTokens = 91;
        var citation = new AICitation
        {
            Provider = "provider", Url = "https://example.com", FileId = "file", Title = "title", Text = "quoted",
            ResponseId = "response", OutputIndex = 1, ContentIndex = 2, StartIndex = 3, EndIndex = 4
        };
        var citations = new List<AICitation> { citation };
        var result = new AIRunResult("text", usage, citations, "provider", "requested", "actual", 2,
            AIFinishReason.Other, "custom-stop");
        usage.InputTokens = 999;
        citation.Title = "changed";
        citations.Clear();
        result.Usage!.CachedInputTokens = 999;
        result.Citations[0].FileId = "changed";

        AssertUsage(result.Usage, 10, 4, 91, 2, 3, 1);
        var saved = result.Citations.Single();
        Assert.AreEqual("provider", saved.Provider);
        Assert.AreEqual("https://example.com", saved.Url);
        Assert.AreEqual("file", saved.FileId);
        Assert.AreEqual("title", saved.Title);
        Assert.AreEqual("quoted", saved.Text);
        Assert.AreEqual("response", saved.ResponseId);
        Assert.AreEqual(1, saved.OutputIndex);
        Assert.AreEqual(2, saved.ContentIndex);
        Assert.AreEqual(3, saved.StartIndex);
        Assert.AreEqual(4, saved.EndIndex);
        Assert.AreEqual("text", result.Text);
        Assert.AreEqual("provider", result.Provider);
        Assert.AreEqual("requested", result.RequestedModel);
        Assert.AreEqual("actual", result.Model);
        Assert.AreEqual(2, result.RoundCount);
        Assert.AreEqual(AIFinishReason.Other, result.FinishReason);
        Assert.AreEqual("custom-stop", result.RawFinishReason);
    }

    [TestMethod]
    public async Task CustomStreamWithoutRoundMetadata_DoesNotInventRoundCount()
    {
        var service = new ResultProbeService(new[] { Completed() }) { OverrideWholeStream = true };
        await using var run = await service.StartRunAsync("question");
        var result = await run.Result;

        Assert.AreEqual(0, result.RoundCount);
        Assert.IsNull(result.Usage);
        Assert.IsNull(result.Model);
        Assert.AreEqual(AIFinishReason.Unknown, result.FinishReason);
    }

    [TestMethod]
    public async Task FinalRoundMissingDetails_DoesNotReusePreviousModelOrReason_AndKeepsReportedUsage()
    {
        var service = new ResultProbeService(
            new[]
            {
                new StreamingContent { Type = StreamingContentType.FunctionResult },
                Completed(Usage(10, 2), "previous-model", AIFinishReason.ToolCalls, "tool_calls")
            },
            new[] { Completed() });
        await using var run = await service.WithMaxRounds(2).StartRunAsync("question");
        var result = await run.Result;

        Assert.AreEqual(2, result.RoundCount);
        Assert.IsNull(result.Model);
        Assert.AreEqual(AIFinishReason.Unknown, result.FinishReason);
        Assert.IsNull(result.RawFinishReason);
        AssertUsage(result.Usage, 10, 2, 12, 0, 0, 0);
    }

    [TestMethod]
    public async Task CustomStream_FallsBackToLastUsageForEachRound_WhenCompletionOmitsUsage()
    {
        var completion = Completed();
        completion.RoundIndex = 3;
        var service = new ResultProbeService(new[]
        {
            new StreamingContent { Type = StreamingContentType.RoundUsage, RoundIndex = 1, Usage = Usage(1, 1) },
            new StreamingContent { Type = StreamingContentType.RoundUsage, RoundIndex = 1, Usage = Usage(10, 2, 3, 4, 1) },
            new StreamingContent { Type = StreamingContentType.RoundUsage, RoundIndex = 2, Usage = Usage(20, 3, 4, 1, 2) },
            completion
        }) { OverrideWholeStream = true };
        await using var run = await service.StartRunAsync("question");
        var result = await run.Result;

        Assert.AreEqual(3, result.RoundCount);
        AssertUsage(result.Usage, 30, 5, 35, 7, 5, 3);
    }

    [TestMethod]
    public async Task CustomStream_LastCompletionAggregateTakesPrecedenceOverRoundUsage_WithoutDoubleCounting()
    {
        var service = new ResultProbeService(new[]
        {
            new StreamingContent { Type = StreamingContentType.RoundUsage, RoundIndex = 1, Usage = Usage(10, 2) },
            new StreamingContent { Type = StreamingContentType.RoundUsage, RoundIndex = 2, Usage = Usage(20, 3) },
            Completed(Usage(30, 5)),
            Completed(Usage(31, 7, 8, 2, 3))
        }) { OverrideWholeStream = true };
        await using var run = await service.StartRunAsync("question");
        var result = await run.Result;

        Assert.AreEqual(2, result.RoundCount);
        AssertUsage(result.Usage, 31, 7, 38, 8, 2, 3);
    }

    [TestMethod]
    public async Task CustomStream_TrailingCompletionWithoutUsage_PreservesPreviouslyReportedAggregate()
    {
        var service = new ResultProbeService(new[]
        {
            Completed(Usage(10, 4, 2, 3, 1)),
            Completed(model: "resolved-model", reason: AIFinishReason.Stop, rawReason: "stop")
        }) { OverrideWholeStream = true };
        await using var run = await service.StartRunAsync("question");
        var result = await run.Result;

        AssertUsage(result.Usage, 10, 4, 14, 2, 3, 1);
        Assert.AreEqual("resolved-model", result.Model);
        Assert.AreEqual(AIFinishReason.Stop, result.FinishReason);
        Assert.AreEqual("stop", result.RawFinishReason);
    }

    [TestMethod]
    public async Task RoundExhaustion_RemainsAFailureWithoutASuccessfulResult()
    {
        var service = new ResultProbeService(new[]
        {
            new StreamingContent
            {
                Type = StreamingContentType.FunctionResult,
                FunctionResult = new FunctionCallResult
                {
                    Call = new FunctionCall { Id = "call", Name = "lookup", Arguments = new Dictionary<string, object>() },
                    Content = "result"
                }
            },
            Completed(Usage(10, 2), "actual", AIFinishReason.ToolCalls, "tool_calls")
        });
        await using var run = await service.WithMaxRounds(1).StartRunAsync("question");
        var failure = await Assert.ThrowsAsync<AIServiceException>(() => run.Result.WaitAsync(TestTimeout));

        StringAssert.Contains(failure.Message, "Maximum function-calling rounds (1)");
        Assert.IsTrue(run.Result.IsFaulted);
        Assert.AreEqual(1, service.RoundsStarted);
    }

    private static TokenUsage Usage(int input, int output, int cached = 0, int creation = 0, int reasoning = 0)
        => new()
        {
            InputTokens = input, OutputTokens = output, TotalTokens = input + output,
            CachedInputTokens = cached, CacheCreationTokens = creation, ReasoningTokens = reasoning
        };

    private static StreamingContent Completed(TokenUsage? usage = null, string? model = null,
        AIFinishReason reason = AIFinishReason.Unknown, string? rawReason = null)
        => new()
        {
            Type = StreamingContentType.Completion, Usage = usage,
            ResponseModel = model, FinishReason = reason, RawFinishReason = rawReason
        };

    private static void AssertUsage(TokenUsage? actual, int input, int output, int total, int cached, int creation, int reasoning)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(input, actual.InputTokens);
        Assert.AreEqual(output, actual.OutputTokens);
        Assert.AreEqual(total, actual.TotalTokens);
        Assert.AreEqual(cached, actual.CachedInputTokens);
        Assert.AreEqual(creation, actual.CacheCreationTokens);
        Assert.AreEqual(reasoning, actual.ReasoningTokens);
    }

    private static async Task<List<StreamingContent>> CollectAsync(AIRun run)
    {
        var events = new List<StreamingContent>();
        await foreach (var item in run.StreamAsync()) events.Add(item);
        return events;
    }

    private sealed class ResultProbeService : AIService
    {
        private readonly StreamingContent[][] _rounds;
        public int RoundsStarted { get; private set; }
        public bool OverrideWholeStream { get; set; }

        public ResultProbeService(params StreamingContent[][] rounds)
            : base("offline", "https://localhost/", new HttpClient())
        {
            _rounds = rounds;
            Model = "requested-alias";
            AddNewChat();
        }

        public override string Provider => "Probe";

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(StreamOptions options,
            bool useFunctions, FunctionCallingPolicy policy, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var index = Math.Min(RoundsStarted++, _rounds.Length - 1);
            await Task.CompletedTask;
            foreach (var item in _rounds[index])
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }

        protected override IAsyncEnumerable<StreamingContent> StreamCoreAsync(Message message,
            StreamOptions options, CancellationToken cancellationToken = default)
            => OverrideWholeStream
                ? StreamRoundAsync(options, false, FunctionCallingPolicy.Default, cancellationToken)
                : base.StreamCoreAsync(message, options, cancellationToken);

        public override Task<string> GetCompletionAsync(Message message) => throw new NotSupportedException();
        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync) => throw new NotSupportedException();
        protected override HttpRequestMessage CreateMessageRequest() => throw new AssertFailedException("No HTTP expected.");
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new AssertFailedException("No HTTP expected.");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => (response, new FunctionCallBatch());
        protected override string ExtractResponseContent(string responseContent) => responseContent;
        protected override string StreamParseJson(string jsonData) => jsonData;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
    }
}
