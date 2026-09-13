using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AIRequestBuilderContractTests
{
    [TestMethod]
    public async Task InvalidFluentValuesLeaveOriginalBuilderDefaultsAndTransportUntouched()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new ProbeOpenAIService(client) { Temperature = 0.4f, TopP = 0.7f };
        var basis = service.CreateRequest("original");
        Action[] invalid =
        [
            () => basis.WithTemperature(float.NaN),
            () => basis.WithTemperature(float.PositiveInfinity),
            () => basis.WithTemperature(-0.1f),
            () => basis.WithTemperature(2.1f),
            () => basis.WithTopP(-0.1f),
            () => basis.WithTopP(1.1f),
            () => basis.WithMaxTokens(0),
            () => basis.WithFrequencyPenalty(-2.1f),
            () => basis.WithPresencePenalty(2.1f),
            () => basis.WithReasoning((ReasoningLevel)999),
            () => basis.WithReasoning(ReasoningLevel.Auto, (CachePreservation)999),
            () => basis.WithFunctionExecution((FunctionExecutionMode)999),
            () => basis.WithFunctionExecution(FunctionExecutionMode.Parallel, 0),
            () => basis.WithMaxRounds(0),
            () => basis.WithTimeout(0),
            () => basis.WithPolicy(new FunctionCallingPolicy { MaxRounds = -1 }),
            () => basis.WithPolicy(new FunctionCallingPolicy { MaxConcurrency = -1 }),
            () => basis.WithPolicy(new FunctionCallingPolicy { TimeoutSeconds = -1 }),
            () => basis.WithPolicy(new FunctionCallingPolicy { ExecutionMode = (FunctionExecutionMode)999 }),
            () => basis.WithProfile(new AIRequestProfile { Purpose = (AIRequestPurpose)999 }),
            () => basis.WithSystemMessage(null!),
            () => basis.WithContext(null!),
            () => basis.WithPolicy(null!),
            () => basis.WithFileSearch(),
            () => basis.WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { " " } })
        ];

        foreach (var action in invalid)
            Assert.Throws<ArgumentException>(action);

        Assert.AreEqual(0, handler.Bodies.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        Assert.AreEqual(0.4f, service.Temperature);
        Assert.AreEqual(0.7f, service.TopP);
        Assert.AreEqual(20, service.DefaultPolicy.MaxRounds);
        Assert.AreEqual("answer", await basis.GetCompletionAsync());
        Assert.AreEqual(0.4f, handler.Bodies.Single().GetProperty("temperature").GetSingle());
        Assert.AreEqual("original", handler.Bodies.Single().GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [TestMethod]
    public async Task UnsupportedFeatureFailureDoesNotContaminateSiblingOrLaterServiceRequest()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new ProbeOpenAIService(client) { Temperature = 0.4f };
        var basis = service.CreateRequest("original");
        var unsupported = basis.WithReasoning(ReasoningLevel.High);

        await Assert.ThrowsAsync<NotSupportedException>(() => unsupported.GetCompletionAsync());
        Assert.AreEqual(0, handler.Bodies.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);

        Assert.AreEqual("answer", await basis.WithTemperature(0.2f).GetCompletionAsync());
        Assert.AreEqual("answer", await service.GetCompletionAsync("later"));
        Assert.AreEqual(0.2f, handler.Bodies[0].GetProperty("temperature").GetSingle());
        Assert.AreEqual(0.4f, handler.Bodies[1].GetProperty("temperature").GetSingle());
        Assert.IsTrue(handler.Bodies.All(body => !body.TryGetProperty("reasoning", out _)));
    }

    [TestMethod]
    public async Task TransportFailureDoesNotLeakRequestSettings_AndSameBuilderCanRetry()
    {
        using var handler = new CaptureHandler { FailFirst = true };
        using var client = new HttpClient(handler);
        var service = new ProbeOpenAIService(client) { Temperature = 0.6f, SystemMessage = "default" };
        var basis = service.CreateRequest("original").WithStatelessMode();
        var specific = basis.WithTemperature(0.2f).WithSystemMessage("request only");

        await Assert.ThrowsAsync<AIServiceException>(() => specific.GetCompletionAsync());
        Assert.AreEqual(0.6f, service.Temperature);
        Assert.AreEqual("default", service.SystemMessage);
        Assert.IsFalse(service.StatelessMode);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);

        Assert.AreEqual("answer", await basis.GetCompletionAsync());
        Assert.AreEqual("answer", await specific.GetCompletionAsync());

        CollectionAssert.AreEqual(new[] { 0.2f, 0.6f, 0.2f },
            handler.Bodies.Select(body => body.GetProperty("temperature").GetSingle()).ToArray());
        CollectionAssert.AreEqual(new[] { "request only", "default", "request only" },
            handler.Bodies.Select(body => body.GetProperty("messages")[0].GetProperty("content").GetString()).ToArray());
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task CreateRequestCopiesImageBytesContentAndNestedMetadata_ForEveryExecution()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new ProbeOpenAIService(client);
        var image = new ImageContent(new byte[] { 1, 2, 3 }, "image/png") { IsHighDetail = true };
        var text = new TextContent("describe this");
        var trace = new Dictionary<string, object> { ["id"] = "original" };
        var input = new Message(ActorRole.User, new List<MessageContent> { text, image })
        { Metadata = new Dictionary<string, object> { ["trace"] = trace } };
        var request = service.CreateRequest(input).WithStatelessMode();

        image.Data![0] = 9;
        image.MimeType = "image/jpeg";
        image.IsHighDetail = false;
        text.Text = "changed";
        trace["id"] = "changed";
        input.Contents.Clear();

        await request.GetCompletionAsync();
        var firstInput = service.ObservedMessages[0].Single();
        Assert.AreEqual("original", ((Dictionary<string, object>)firstInput.Metadata!["trace"])["id"]);
        Assert.AreEqual("describe this", ((TextContent)firstInput.Contents[0]).Text);
        var firstImage = (ImageContent)firstInput.Contents[1];
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, firstImage.Data);
        Assert.AreEqual("image/png", firstImage.MimeType);
        Assert.IsTrue(firstImage.IsHighDetail);

        // A provider's execution copy must not become the reusable builder's input.
        firstImage.Data![0] = 8;
        ((Dictionary<string, object>)firstInput.Metadata["trace"])["id"] = "provider changed";
        await request.GetCompletionAsync();

        var secondInput = service.ObservedMessages[1].Single();
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ((ImageContent)secondInput.Contents[1]).Data);
        Assert.AreEqual("original", ((Dictionary<string, object>)secondInput.Metadata!["trace"])["id"]);
        foreach (var body in handler.Bodies)
        {
            var parts = body.GetProperty("messages")[0].GetProperty("content");
            Assert.AreEqual("describe this", parts[0].GetProperty("text").GetString());
            Assert.AreEqual("data:image/png;base64,AQID", parts[1].GetProperty("image_url").GetProperty("url").GetString());
            Assert.AreEqual("high", parts[1].GetProperty("image_url").GetProperty("detail").GetString());
        }
    }

    [TestMethod]
    public async Task WithContextCopiesNestedOverrideAdditionalMessagesAndImagePayload()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new ProbeOpenAIService(client) { SystemMessage = "base system" };
        var image = new ImageContent(new byte[] { 4, 5, 6 }, "image/png");
        var overridden = new Message(ActorRole.User, new List<MessageContent> { new TextContent("replacement"), image });
        var additional = new Message(ActorRole.User, "additional original")
        { Metadata = new Dictionary<string, object> { ["nested"] = new Dictionary<string, object> { ["value"] = "original" } } };
        var extras = new List<Message> { additional };
        var context = new AIRequestContext
        { SystemMessagePrefix = "prefix original", SystemMessageSuffix = "suffix original", RequestMessageOverride = overridden, AdditionalMessages = extras };
        var request = service.CreateRequest("original query").WithContext(context).WithStatelessMode();

        image.Data![1] = 9;
        ((TextContent)overridden.Contents[0]).Text = "changed replacement";
        additional.Content = "changed additional";
        ((Dictionary<string, object>)additional.Metadata!["nested"])["value"] = "changed";
        extras.Clear();
        context.RequestMessageOverride = new Message(ActorRole.User, "other replacement");
        context.SystemMessagePrefix = "changed prefix";
        context.SystemMessageSuffix = "changed suffix";

        await request.GetCompletionAsync();

        var observed = service.ObservedMessages.Single();
        Assert.AreEqual(2, observed.Count);
        Assert.AreEqual("replacement", ((TextContent)observed[0].Contents[0]).Text);
        CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, ((ImageContent)observed[0].Contents[1]).Data);
        Assert.AreEqual("additional original", observed[1].Content);
        Assert.AreEqual("original", ((Dictionary<string, object>)observed[1].Metadata!["nested"])["value"]);
        var body = handler.Bodies.Single();
        var system = body.GetProperty("messages")[0].GetProperty("content").GetString()!;
        StringAssert.Contains(system, "prefix original");
        StringAssert.Contains(system, "base system");
        StringAssert.Contains(system, "suffix original");
        Assert.IsFalse(system.Contains("changed"));
        Assert.AreEqual("data:image/png;base64,BAUG", body.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.AreEqual("base system", service.SystemMessage);
    }

    [TestMethod]
    public async Task PreCancelledStartRunLeavesHistoryAndTransportUntouched_AndBuilderCanRunAgain()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new ProbeOpenAIService(client) { Temperature = 0.6f };
        var request = service.CreateRequest("run input").WithTemperature(0.2f);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => request.StartRunAsync(cancellationToken: cancellation.Token));

        Assert.AreEqual(0, handler.Bodies.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        Assert.AreEqual(0.6f, service.Temperature);
        await using var run = await request.StartRunAsync();
        Assert.AreEqual("answer", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.AreEqual(1, handler.Bodies.Count);
        Assert.AreEqual(0.2f, handler.Bodies[0].GetProperty("temperature").GetSingle());
    }

    [TestMethod]
    public async Task WithPolicyCopiesOriginalPolicy_AndRunKeepsConfiguredRoundsAndConcurrency()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new ProbeOpenAIService(client);
        var policy = new FunctionCallingPolicy
        { MaxRounds = 3, TimeoutSeconds = 30, ExecutionMode = FunctionExecutionMode.Parallel, MaxConcurrency = 2 };
        var request = service.CreateRequest("run input").WithPolicy(policy);
        policy.MaxRounds = 99;
        policy.TimeoutSeconds = 1;
        policy.ExecutionMode = FunctionExecutionMode.Sequential;
        policy.MaxConcurrency = 50;
        service.DefaultPolicy.MaxRounds = 17;

        await using var run = await request.StartRunAsync();
        Assert.AreEqual("answer", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);

        var observed = service.Settings.Single();
        Assert.AreEqual(3, observed.Policy.MaxRounds);
        Assert.AreEqual(30, observed.Policy.TimeoutSeconds);
        Assert.AreEqual(FunctionExecutionMode.Parallel, observed.Policy.ExecutionMode);
        Assert.AreEqual(2, observed.Policy.MaxConcurrency);
        Assert.AreEqual(17, observed.DefaultMaxRounds);
        Assert.AreEqual(17, service.DefaultPolicy.MaxRounds);
    }

    [TestMethod]
    public async Task LegacyMessageChainUsesRequestPolicyAndStatelessMode_WithoutMutatingDefaults()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new ProbeOpenAIService(client) { SystemMessage = "default", Temperature = 0.6f };
        var policy = new FunctionCallingPolicy { MaxRounds = 3, TimeoutSeconds = 40 };
        var chain = service.BeginMessage().AddText("chain input").WithPolicy(policy);
        policy.MaxRounds = 99;

        Assert.AreEqual("answer", await chain.SendOnceAsync());
        var stream = new StringBuilder();
        await chain.StreamOnceAsync(chunk => stream.Append(chunk));

        Assert.AreEqual("answer", stream.ToString());
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        Assert.IsFalse(service.StatelessMode);
        Assert.AreEqual(20, service.DefaultPolicy.MaxRounds);
        foreach (var observed in service.Settings)
        {
            Assert.AreEqual(3, observed.Policy.MaxRounds);
            Assert.AreEqual(40, observed.Policy.TimeoutSeconds);
            Assert.IsTrue(observed.Stateless);
            Assert.IsFalse(observed.DefaultStateless);
            Assert.AreEqual(20, observed.DefaultMaxRounds);
        }
        Assert.AreEqual(2, handler.Bodies.Count);
    }

    private sealed record ObservedSettings(FunctionCallingPolicy Policy, bool Stateless, int DefaultMaxRounds, bool DefaultStateless);

    private sealed class ProbeOpenAIService(HttpClient client) : OpenAIService("test-key", "gpt-4o", client)
    {
        public List<ObservedSettings> Settings { get; } = new();
        public List<List<Message>> ObservedMessages { get; } = new();

        protected override HttpRequestMessage CreateMessageRequest()
        {
            Settings.Add(new ObservedSettings(GetExecutionPolicy(), RequestStatelessMode, DefaultPolicy.MaxRounds, StatelessMode));
            ObservedMessages.Add(GetLatestMessages().ToList());
            return base.CreateMessageRequest();
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private const string Answer = """{"choices":[{"message":{"role":"assistant","content":"answer"},"finish_reason":"stop"}]}""";
        private const string StreamAnswer = "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"answer\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        public bool FailFirst { get; init; }
        public List<JsonElement> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = document.RootElement.Clone();
            Bodies.Add(body);
            if (FailFirst && Bodies.Count == 1)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                { Content = new StringContent("{\"error\":{\"message\":\"temporary failure\"}}", Encoding.UTF8, "application/json") };
            var stream = body.TryGetProperty("stream", out var streamValue) && streamValue.GetBoolean();
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(stream ? StreamAnswer : Answer, Encoding.UTF8, stream ? "text/event-stream" : "application/json") };
        }
    }
}
