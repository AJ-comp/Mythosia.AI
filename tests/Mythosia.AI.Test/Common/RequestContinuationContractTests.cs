using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class RequestContinuationContractTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    public async Task CustomRunSession_CanEmitStatusBeforeDelegatingWithoutLosingCapturedSettings(int statusCount)
    {
        using var http = new HttpClient();
        var service = new ProbeService(http) { StatusCount = statusCount, MaxTokens = 3333 };
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "wanted.example" } });

        await using var run = await service.CreateRequest("input")
            .WithProfile(new AIRequestProfile { MaxTokens = 1111 }).StartRunAsync();
        Assert.AreEqual("ok", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);

        var sent = Assert.ContainsSingle(service.Observed);
        Assert.AreEqual(1111u, sent.MaxTokens);
        CollectionAssert.AreEqual(new[] { "wanted.example" }, sent.Domains!);
        Assert.AreEqual(3333u, service.MaxTokens);
        await service.GetCompletionAsync("next");
        Assert.AreEqual(3333u, service.Observed[1].MaxTokens);
        Assert.IsNull(service.Observed[1].Domains);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task CompatibilityAgent_OwnsItsRoundBudgetAndDoesNotMutateParent(bool nested, bool streaming)
    {
        using var http = new HttpClient();
        var service = new ProbeService(http) { DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 9 } };
        async Task ExecuteAgent()
        {
#pragma warning disable CS0618
            if (streaming)
            {
                await foreach (var _ in service.RunAgentStreamAsync("inner", maxSteps: 2)) { }
            }
            else Assert.AreEqual("ok", await service.RunAgentAsync("inner", maxSteps: 2));
#pragma warning restore CS0618
        }
        var entered = false;
        if (nested)
        {
            service.WithSystemMessageProvider(async _ =>
            {
                if (!entered) { entered = true; await ExecuteAgent(); }
                return null;
            });
            Assert.AreEqual("ok", await service.GetCompletionAsync("outer"));
        }
        else await ExecuteAgent();

        Assert.AreEqual(2, service.Observed[0].MaxRounds);
        if (nested) Assert.AreEqual(9, service.Observed[1].MaxRounds);
        Assert.AreEqual(9, service.DefaultPolicy.MaxRounds);
        await service.GetCompletionAsync("next");
        Assert.AreEqual(9, service.Observed[^1].MaxRounds);
    }

    private sealed record Observation(uint MaxTokens, int MaxRounds, string[]? Domains);

    [TestMethod]
    public async Task CompatibilityAgent_PreservesVirtualStringHookAndCapturesItsTransformedInputAndOptions()
    {
        using var http = new HttpClient();
        var service = new StringHookService(http) { DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 9 } };
        await service.GetCompletionAsync("direct");
#pragma warning disable CS0618
        await service.RunAgentAsync("agent", maxSteps: 2);
#pragma warning restore CS0618

        CollectionAssert.AreEqual(new[] { "direct", "agent" }, service.HookInputs.ToArray());
        CollectionAssert.AreEqual(new[] { "CUSTOM_PREFIX direct", "CUSTOM_PREFIX agent" }, service.CapturedInputs.ToArray());
        Assert.AreEqual(2, service.Observed[1].MaxRounds);
        CollectionAssert.AreEqual(new[] { "hook.example" }, service.Observed[1].Domains!);
        Assert.AreEqual(9, service.DefaultPolicy.MaxRounds);
    }

    private sealed class StringHookService(HttpClient http) : ProbeService(http)
    {
        public List<string> HookInputs { get; } = [];
        public List<string> CapturedInputs { get; } = [];
        public override Task<string> GetCompletionAsync(string prompt, AIRequestProfile? profile = null,
            AIRequestContext? context = null, CancellationToken cancellationToken = default)
        {
            HookInputs.Add(prompt);
            this.WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "hook.example" } });
            return base.GetCompletionAsync("CUSTOM_PREFIX " + prompt, profile, context, cancellationToken);
        }
        protected override object? CaptureProviderRequestOptions(Message message)
        {
            CapturedInputs.Add(message.Content);
            return base.CaptureProviderRequestOptions(message);
        }
    }

    private class ProbeService(HttpClient http) : AnthropicService("offline", AIModels.Anthropic.ClaudeSonnet5_5, http)
    {
        public int StatusCount { get; init; }
        public List<Observation> Observed { get; } = [];

        private void Observe(FunctionCallingPolicy policy) => Observed.Add(new Observation(
            RequestMaxTokens, policy.MaxRounds, CurrentRequestFeatures.WebSearch?.AllowedDomains?.ToArray()));

        protected override async Task<RunSession> CreateRunSessionAsync(Message message, StreamOptions options,
            AIRequestContext? context, CancellationToken token)
            => new StatusSession(await base.CreateRunSessionAsync(message, options, context, token), StatusCount);

        public override async Task<string> GetCompletionAsync(Message message)
        {
            using var scope = BeginRequestFeaturesScope(message);
            Observe(GetExecutionPolicy());
            await Task.Yield();
            return "ok";
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(StreamOptions options,
            bool useFunctions, FunctionCallingPolicy policy, [EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            Observe(policy);
            yield return new StreamingContent { Type = StreamingContentType.Text, Content = "ok" };
        }

        private sealed class StatusSession(RunSession inner, int statusCount) : RunSession
        {
            public override async IAsyncEnumerable<StreamingContent> StreamAsync([EnumeratorCancellation] CancellationToken token)
            {
                for (var index = 0; index < statusCount; index++)
                {
                    await Task.Yield();
                    yield return new StreamingContent { Type = StreamingContentType.Status, Content = "Preparing" };
                }
                await foreach (var item in inner.StreamAsync(token)) yield return item;
            }
            public override ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
