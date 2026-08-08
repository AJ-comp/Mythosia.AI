using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AdversarialFunctionLoopTests
{
    [TestMethod]
    public async Task Streaming_EveryRoundRequestsAnotherTool_EndsWithExplicitError()
    {
        var service = new EndlessToolRoundService
        {
            DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 2 }
        };
        service.Functions.Add(new FunctionDefinition
        {
            Name = "loop",
            Handler = _ => Task.FromResult("again")
        });
        var events = new List<StreamingContent>();

        await foreach (var content in service.StreamAsync("loop forever", StreamOptions.WithFunctions))
            events.Add(content);

        Assert.AreEqual(2, events.Count(content => content.Type == StreamingContentType.FunctionResult));
        Assert.IsFalse(events.Any(content => content.Type == StreamingContentType.Completion));
        var error = events.Single(content => content.Type == StreamingContentType.Error);
        Assert.AreEqual("max_rounds_exceeded", error.Metadata?["status"]);
        Assert.AreEqual(2, error.Metadata?["max_rounds"]);
    }

    [TestMethod]
    public async Task TextStreaming_EveryRoundRequestsAnotherTool_ThrowsInsteadOfEndingSilently()
    {
        var service = new EndlessToolRoundService
        {
            DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 1 }
        };

        var exception = await Assert.ThrowsExactlyAsync<Mythosia.AI.Exceptions.AIServiceException>(async () =>
        {
            await foreach (var _ in service.StreamAsync("loop forever"))
            {
            }
        });

        StringAssert.Contains(exception.Message, "Maximum function-calling rounds (1) exceeded.");
    }

    private sealed class EndlessToolRoundService : AIService
    {
        private int _round;

        public EndlessToolRoundService()
            : base("offline-key", "https://localhost/", new HttpClient())
        {
        }

        public override string Provider => nameof(AIProvider.OpenAI);

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options,
            bool useFunctions,
            FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            var call = new FunctionCall
            {
                Id = $"call-{_round}",
                Name = "loop",
                Index = 0
            };
            _round++;
            yield return new StreamingContent
            {
                Type = StreamingContentType.FunctionResult,
                FunctionResult = new FunctionCallResult
                {
                    Call = call,
                    Content = "again"
                },
                FunctionCallBatchId = $"batch-{_round}"
            };
        }

        public override Task<string> GetCompletionAsync(Message message) => Task.FromResult(string.Empty);
        protected override HttpRequestMessage CreateMessageRequest() => new(HttpMethod.Post, "https://localhost/");
        protected override string ExtractResponseContent(string responseContent) => responseContent;
        protected override string StreamParseJson(string jsonData) => jsonData;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => Task.CompletedTask;
        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
            => (response, new FunctionCallBatch());
    }
}
