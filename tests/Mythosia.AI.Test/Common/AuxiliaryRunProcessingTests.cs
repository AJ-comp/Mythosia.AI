using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("InferenceSpeed")]
public class AuxiliaryRunProcessingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AuxiliaryRun_PreservesLastUserProcessing_AndPendingExternalSpeed(bool summarize)
    {
        var service = new ProbeService();
        await service.CreateRequest("user").WithSpeed(InferenceSpeed.Fast).GetCompletionAsync();
        var original = service.LastProcessing.Single();
        var request = service.CreateRequest("internal").WithSpeed(InferenceSpeed.Fast)
            .WithProfile(summarize ? RequestProfiles.Summarization : RequestProfiles.QueryRewrite);
        service.WithSpeed(InferenceSpeed.Standard);

        await using (var run = await request.StartRunAsync())
        {
            var result = await run.Result;
            Assert.AreEqual(InferenceSpeed.ProviderDefault, result.Processing.Single().RequestedSpeed);
            Assert.AreNotEqual(original.ResponseId, result.Processing.Single().ResponseId);
            Assert.AreEqual(original.ResponseId, service.LastProcessing.Single().ResponseId);
            Assert.AreEqual(InferenceSpeed.Fast, service.LastProcessing.Single().RequestedSpeed);
        }

        await service.GetCompletionAsync("next public call");
        CollectionAssert.AreEqual(new[] { InferenceSpeed.Fast, InferenceSpeed.ProviderDefault, InferenceSpeed.Standard }, service.Seen.ToArray());
        Assert.AreEqual(InferenceSpeed.Standard, service.LastProcessing.Single().RequestedSpeed);
        Assert.AreEqual(1, service.LastProcessing.Single().RequestIndex);
    }

    [TestMethod]
    public async Task FailedAuxiliaryRunStartup_DoesNotReplaceLastUserProcessing()
    {
        var service = new ProbeService();
        await service.WithSpeed(InferenceSpeed.Fast).GetCompletionAsync("user");
        var original = service.LastProcessing.Single();
        service.FailStartup = true;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await using var run = await service.CreateRequest("internal")
                .WithProfile(RequestProfiles.QueryRewrite).StartRunAsync();
        });
        Assert.AreEqual(original.ResponseId, service.LastProcessing.Single().ResponseId);
        Assert.AreEqual(InferenceSpeed.Fast, service.LastProcessing.Single().RequestedSpeed);

        service.FailStartup = false;
        await using var next = await service.CreateRequest("public run").WithSpeed(InferenceSpeed.Standard).StartRunAsync();
        var result = await next.Result;
        Assert.AreEqual(InferenceSpeed.Standard, result.Processing.Single().RequestedSpeed);
        Assert.AreEqual(result.Processing.Single().ResponseId, service.LastProcessing.Single().ResponseId);
    }

    [TestMethod]
    public async Task ExternalRunWithoutSpeed_PublishesFreshDefaultObservations()
    {
        var service = new ProbeService();
        await service.WithSpeed(InferenceSpeed.Fast).GetCompletionAsync("old user turn");
        var originalId = service.LastProcessing.Single().ResponseId;
        await using var run = await service.CreateRequest("new public run").StartRunAsync();
        var result = await run.Result;
        Assert.AreEqual(InferenceSpeed.ProviderDefault, result.Processing.Single().RequestedSpeed);
        Assert.AreNotEqual(originalId, service.LastProcessing.Single().ResponseId);
        Assert.AreEqual(result.Processing.Single().ResponseId, service.LastProcessing.Single().ResponseId);
    }

    private sealed class ProbeService : AIService
    {
        public override string Provider => "AuxiliaryRunProbe";
        public List<InferenceSpeed> Seen { get; } = new();
        public bool FailStartup { get; set; }
        public ProbeService() : base("offline", "https://localhost/", new HttpClient()) { }
        protected override CapabilitySupport ResolveSpeedSupport(InferenceSpeed speed) => CapabilitySupport.Supported;
        private void Observe()
        {
            Seen.Add(RequestSpeed);
            BeginProcessingObservation().Record("standard", InferenceSpeed.Standard, "response-" + Seen.Count);
        }
        public override Task<string> GetCompletionAsync(Message message)
        {
            using var scope = BeginRequestFeaturesScope(message);
            Observe();
            return Task.FromResult("answer");
        }
        protected override Task<RunSession> CreateRunSessionAsync(Message message, StreamOptions executionOptions,
            AIRequestContext? context, CancellationToken cancellationToken)
            => FailStartup ? throw new InvalidOperationException("startup failed")
                : base.CreateRunSessionAsync(message, executionOptions, context, cancellationToken);
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
