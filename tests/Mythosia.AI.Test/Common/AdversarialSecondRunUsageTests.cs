using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AdversarialSecondRunUsageTests
{
    public static IEnumerable<object[]> Cases => new[]
    {
        nameof(TokenUsage.InputTokens), nameof(TokenUsage.OutputTokens), nameof(TokenUsage.TotalTokens),
        nameof(TokenUsage.CachedInputTokens), nameof(TokenUsage.CacheCreationTokens), nameof(TokenUsage.ReasoningTokens)
    }.SelectMany(countName => new[] { false, true }.SelectMany(custom =>
        new[] { false, true }.Select(overflow => new object[] { countName, custom, overflow })));

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task AccumulatedUsageCannotWrapOrLeaveResultPending(string countName, bool customStream, bool overflow)
    {
        var service = new UsageService(countName, customStream) { Overflow = overflow };
        await using (var run = await service.StartRunAsync("boundary"))
        {
            if (overflow)
            {
                await Assert.ThrowsAsync<OverflowException>(() => run.Result.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.IsTrue(run.Result.IsFaulted, "Result must settle after failed final aggregation, including custom streams.");
            }
            else
            {
                var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.IsNotNull(result.Usage);
                Assert.AreEqual(int.MaxValue, Read(result.Usage, countName));
            }
        }
        Assert.AreEqual(customStream ? 1 : 2, service.DisposedEnumerators);

        service.Overflow = false;
        await using var next = await service.StartRunAsync("reuse after cleanup");
        Assert.AreEqual(int.MaxValue, Read((await next.Result.WaitAsync(TimeSpan.FromSeconds(3))).Usage!, countName));
    }

    private static int Read(TokenUsage usage, string countName) => (int)typeof(TokenUsage).GetProperty(countName)!.GetValue(usage)!;

    private sealed class UsageService(string countName, bool customStream)
        : AIService("offline", "https://offline.invalid/", new HttpClient())
    {
        private int _round;
        public bool Overflow { get; set; }
        public int DisposedEnumerators { get; private set; }
        public override string Provider => "UsageProbe";

        private TokenUsage Usage(int value)
        {
            var usage = new TokenUsage();
            typeof(TokenUsage).GetProperty(countName)!.SetValue(usage, value);
            return usage;
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(StreamOptions options,
            bool useFunctions, FunctionCallingPolicy policy, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var first = _round++ % 2 == 0;
            await Task.CompletedTask;
            try
            {
                if (first) yield return new StreamingContent { Type = StreamingContentType.FunctionResult };
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Completion,
                    Usage = Usage(first ? int.MaxValue : Overflow ? 1 : 0)
                };
            }
            finally { DisposedEnumerators++; }
        }

        protected override IAsyncEnumerable<StreamingContent> StreamCoreAsync(Message message, StreamOptions options,
            CancellationToken cancellationToken = default)
            => customStream ? CustomStream(cancellationToken) : base.StreamCoreAsync(message, options, cancellationToken);

        private async IAsyncEnumerable<StreamingContent> CustomStream([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            try
            {
                yield return new StreamingContent { Type = StreamingContentType.RoundUsage, RoundIndex = 1, Usage = Usage(int.MaxValue) };
                yield return new StreamingContent { Type = StreamingContentType.RoundUsage, RoundIndex = 2, Usage = Usage(Overflow ? 1 : 0) };
                yield return new StreamingContent { Type = StreamingContentType.Completion, RoundIndex = 2 };
            }
            finally { DisposedEnumerators++; }
        }

        public override Task<string> GetCompletionAsync(Message message) => throw new NotSupportedException();
        public override Task StreamCompletionAsync(Message message, Func<string, Task> callback) => throw new NotSupportedException();
        protected override HttpRequestMessage CreateMessageRequest() => throw new AssertFailedException("No HTTP expected.");
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new AssertFailedException("No HTTP expected.");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => (response, new());
        protected override string ExtractResponseContent(string response) => response;
        protected override string StreamParseJson(string response) => response;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
    }
}
