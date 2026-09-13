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
public class AdversarialThirdStructuredRetryTests
{
    public static IEnumerable<object[]> Cases => new[] { false, true }.SelectMany(stream =>
        new[] { false, true }.SelectMany(policy => new[] { false, true }.Select(invalid => new object[] { stream, policy, invalid })));

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task ExtremeRetryBudgetIsValidatedBeforeProviderWork(bool stream, bool perCallPolicy, bool invalid)
    {
        var service = new RetryService();
        var budget = invalid ? int.MaxValue : int.MaxValue - 1;
        if (!perCallPolicy) service.StructuredOutputMaxRetries = budget;

        async Task<Value> Execute()
        {
            if (stream)
            {
                var builder = service.BeginStream("value");
                if (perCallPolicy) builder.WithStructuredOutput(new StructuredOutputPolicy { MaxRepairAttempts = budget });
                return await builder.As<Value>().Result.WaitAsync(TimeSpan.FromSeconds(3));
            }
            if (perCallPolicy) service.WithStructuredOutputPolicy(new StructuredOutputPolicy { MaxRepairAttempts = budget });
            return await service.GetCompletionAsync<Value>("value").WaitAsync(TimeSpan.FromSeconds(3));
        }

        if (invalid)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Execute());
            Assert.AreEqual(0, service.Calls, "An unrepresentable attempt count must not start a provider request.");
        }
        else
        {
            Assert.AreEqual(42, (await Execute()).Number);
            Assert.AreEqual(1, service.Calls, "A large valid budget must still accept the first valid response.");
        }
    }

    public sealed class Value { public int Number { get; set; } }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RejectedRetryBudgetConsumesItsOneCallFeatures(bool stream)
    {
        var service = new RetryService { StructuredOutputMaxRetries = int.MaxValue };
        service.ConfigureRequestFeatures(new AIRequestFeatures
        {
            Reasoning = new ReasoningOptions { Level = ReasoningLevel.High }
        });

        async Task ExecuteRejected()
        {
            if (stream) await service.BeginStream("invalid").As<Value>().Result;
            else await service.GetCompletionAsync<Value>("invalid");
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(ExecuteRejected);
        Assert.AreEqual(0, service.Calls);
        service.StructuredOutputMaxRetries = 0;
        await service.GetCompletionAsync<Value>("unrelated later request");
        Assert.IsFalse(service.LastCallHadReasoning,
            "Failed request options must not leak into an unrelated later call.");
    }

    private sealed class RetryService() : AIService("offline", "https://offline.invalid/", new HttpClient())
    {
        public int Calls { get; private set; }
        public bool LastCallHadReasoning { get; private set; }
        public override string Provider => "RetryProbe";
        protected override void ValidateRequestFeatures(AIRequestFeatures features) { }
        public override Task<string> GetCompletionAsync(Message message)
        {
            Calls++;
            LastCallHadReasoning = CurrentRequestFeatures.Reasoning != null;
            return Task.FromResult("{\"Number\":42}");
        }
        protected override async IAsyncEnumerable<StreamingContent> StreamCoreAsync(Message message, StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            Calls++;
            yield return new StreamingContent { Type = StreamingContentType.Text, Content = "{\"Number\":42}" };
            yield return new StreamingContent { Type = StreamingContentType.Completion };
        }
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
