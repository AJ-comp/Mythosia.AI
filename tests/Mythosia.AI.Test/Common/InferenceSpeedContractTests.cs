using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class InferenceSpeedContractTests
{
    [TestMethod]
    public async Task BuilderBranches_ReplayTheirOwnSpeed_WithoutConsumingPendingServiceOptions()
    {
        var service = new ProbeService();
        var basis = service.CreateRequest("hello");
        var fast = basis.WithSpeed(InferenceSpeed.Fast);
        var standard = basis.WithSpeed(InferenceSpeed.Standard);
        service.WithSpeed(InferenceSpeed.Fast);
        await fast.GetCompletionAsync();
        await standard.GetCompletionAsync();
        await basis.GetCompletionAsync();
        await fast.GetCompletionAsync();
        await service.GetCompletionAsync("pending");
        await service.GetCompletionAsync("plain");
        CollectionAssert.AreEqual(new[] { InferenceSpeed.Fast, InferenceSpeed.Standard, InferenceSpeed.ProviderDefault,
            InferenceSpeed.Fast, InferenceSpeed.Fast, InferenceSpeed.ProviderDefault }, service.Seen.ToArray());
    }

    [TestMethod]
    public async Task InvalidAndUnsupportedValues_DoNotTouchHistoryOrTransport()
    {
        var service = new ProbeService { RejectSpeed = true };
        var request = service.CreateRequest("hello");
        Assert.Throws<ArgumentOutOfRangeException>(() => request.WithSpeed((InferenceSpeed)33));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.WithSpeed((InferenceSpeed)33));
        await Assert.ThrowsAsync<NotSupportedException>(() => request.WithSpeed(InferenceSpeed.Fast).GetCompletionAsync());
        await Assert.ThrowsAsync<NotSupportedException>(async () => { await using var run = await request.WithSpeed(InferenceSpeed.Standard).StartRunAsync(); });
        Assert.AreEqual(0, service.Seen.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        service.WithSpeed(InferenceSpeed.Fast);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.GetCompletionAsync("bad"));
        await service.GetCompletionAsync("next");
        Assert.AreEqual(InferenceSpeed.ProviderDefault, service.Seen.Single());
    }

    [TestMethod]
    public async Task CapabilitiesInspectWithoutConsumingOptions_AndCopyDoesNotMutateUnknownSingleton()
    {
        var service = new ProbeService();
        var builder = service.CreateRequest("inspect");
        service.WithSpeed(InferenceSpeed.Fast);
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast));
        Assert.AreEqual(CapabilitySupport.Supported, builder.GetCapabilities().StandardSpeed);
        Assert.AreEqual(CapabilitySupport.Unknown, AIModelCapabilities.Unknown.FastSpeed);
        Assert.AreEqual(CapabilitySupport.Unsupported, service.GetCapabilities().GetSpeedSupport((InferenceSpeed)77));
        Assert.AreEqual(0, service.Seen.Count);
        await service.GetCompletionAsync("consume");
        Assert.AreEqual(InferenceSpeed.Fast, service.Seen.Single());
    }

    [TestMethod]
    public async Task AuxiliaryProfilesSuppressSpeed_AndPreserveLatestUserObservations()
    {
        var service = new ProbeService();
        await service.CreateRequest("user").WithSpeed(InferenceSpeed.Fast).GetCompletionAsync();
        var user = service.LastProcessing;
        await service.CreateRequest("internal").WithSpeed(InferenceSpeed.Fast)
            .WithProfile(RequestProfiles.QueryRewrite).GetCompletionAsync();
        CollectionAssert.AreEqual(new[] { InferenceSpeed.Fast, InferenceSpeed.ProviderDefault }, service.Seen.ToArray());
        Assert.AreEqual(user[0].ResponseId, service.LastProcessing[0].ResponseId);
        Assert.AreEqual(InferenceSpeed.Fast, service.LastProcessing[0].RequestedSpeed);
    }

    [TestMethod]
    public async Task RunResultRetainsEveryAttempt_WhenUnobserved_AndSurvivesLaterRequests()
    {
        var service = new ProbeService { Attempts = 3 };
        await using var run = await service.CreateRequest("hello").WithSpeed(InferenceSpeed.Fast).StartRunAsync();
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("answer", result.Text);
        Assert.AreEqual(3, result.Processing.Count);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, result.Processing.Select(x => x.RequestIndex).ToArray());
        Assert.IsTrue(result.Processing[0].IsDowngraded);
        Assert.IsNull(result.Processing[1].AppliedSpeed);
        Assert.AreEqual("future-mode", result.Processing[1].RawAppliedMode);
        Assert.AreEqual(InferenceSpeed.Fast, result.Processing[2].AppliedSpeed);
        await service.GetCompletionAsync("later");
        Assert.AreEqual(InferenceSpeed.Fast, result.Processing[0].RequestedSpeed);
        Assert.AreEqual(InferenceSpeed.ProviderDefault, service.GetLastProcessing()[0].RequestedSpeed);
        Assert.Throws<NotSupportedException>(() => ((IList<AIProcessingInfo>)result.Processing).Clear());
    }

    [TestMethod]
    public void ResultCopiesProcessingInput_AndOriginalConstructorStillExists()
    {
        var input = new List<AIProcessingInfo> { new(1, InferenceSpeed.Fast, InferenceSpeed.Standard, "default") };
        var result = new AIRunResult("ok", null, null, "", null, null, 0, AIFinishReason.Unknown, null, input);
        input.Clear();
        Assert.AreEqual(1, result.Processing.Count);
        Assert.IsTrue(typeof(AIRunResult).GetConstructors().Any(c => c.GetParameters().Length == 9));
        Assert.AreEqual(0, new AIRunResult("old call").Processing.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AIProcessingInfo(0, InferenceSpeed.Fast));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AIProcessingInfo(1, InferenceSpeed.Fast, InferenceSpeed.ProviderDefault));
    }

    private sealed class ProbeService : AIService
    {
        public override string Provider => "Probe";
        public bool RejectSpeed { get; set; }
        public int Attempts { get; set; } = 1;
        public List<InferenceSpeed> Seen { get; } = new();
        public ProbeService() : base("offline", "https://localhost/", new HttpClient()) { }
        protected override CapabilitySupport ResolveSpeedSupport(InferenceSpeed speed)
            => RejectSpeed ? CapabilitySupport.Unsupported : CapabilitySupport.Supported;
        private void Observe()
        {
            Seen.Add(RequestSpeed);
            for (var i = 0; i < Attempts; i++)
            {
                var observation = BeginProcessingObservation();
                observation.Record("standard", InferenceSpeed.Standard, $"response-{Seen.Count}-{i}");
                if (i == 1) observation.Record("future-mode", null);
                if (i == 2) observation.Record("fast", InferenceSpeed.Fast);
                observation.Record(null, null); // Absent later metadata must not erase a known report.
            }
        }
        public override Task<string> GetCompletionAsync(Message message)
        {
            using var scope = BeginRequestFeaturesScope(message);
            Observe();
            return Task.FromResult("answer");
        }
        protected override async IAsyncEnumerable<StreamingContent> StreamCoreAsync(Message message, StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Observe();
            await Task.Yield();
            yield return new StreamingContent { Type = StreamingContentType.Text, Content = "answer" };
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
