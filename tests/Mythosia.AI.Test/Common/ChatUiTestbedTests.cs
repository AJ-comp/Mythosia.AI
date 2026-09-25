using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Microsoft.AspNetCore.Http;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Rag;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;
using Mythosia.VectorDb.Postgres;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class ChatUiTestbedTests
{
    [TestMethod]
    public void RuntimeInfo_ReportsLoadedPackageVersionsWithoutEnvironmentOrConnectionData()
    {
        var info = JsonSerializer.SerializeToElement(ChatUiTestbedInfo.Build());
        CollectionAssert.AreEquivalent(new[] { "name", "packages" }, info.EnumerateObject().Select(p => p.Name).ToArray());
        var assemblies = new[] { typeof(OpenAIService).Assembly, typeof(RagStore).Assembly, typeof(PostgresStore).Assembly };
        var packages = info.GetProperty("packages").EnumerateArray().ToArray();
        Assert.AreEqual(assemblies.Length, packages.Length);
        foreach (var assembly in assemblies)
        {
            var package = packages.Single(p => p.GetProperty("name").GetString() == assembly.GetName().Name);
            CollectionAssert.AreEquivalent(new[] { "name", "version" }, package.EnumerateObject().Select(p => p.Name).ToArray());
            var expected = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
            Assert.AreEqual(expected, package.GetProperty("version").GetString());
        }
    }

    [TestMethod]
    public void SpeedControls_DistinguishUnsupportedAndUnknownWithoutMakingAccountPromises()
    {
        using var client = new HttpClient(new StreamHandler());
        var sonnet = new AnthropicService("offline", AIModels.Anthropic.ClaudeSonnet5, client);
        var controls = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetModelControls(sonnet));
        Assert.AreEqual("ProviderDefault", controls.GetProperty("speed").GetProperty("selected").GetString());
        Assert.AreEqual("Unsupported", controls.GetProperty("speed").GetProperty("fast").GetString());
        Assert.Throws<ArgumentException>(() => ChatUiSettingsHelpers.ResolveSpeed(sonnet, "Fast", InferenceSpeed.ProviderDefault));
        Assert.AreEqual(InferenceSpeed.Standard,
            ChatUiSettingsHelpers.ResolveSpeed(sonnet, "Standard", InferenceSpeed.ProviderDefault));

        using var customClient = new HttpClient(new StreamHandler());
        var custom = new QwenService("http://localhost:8000/v1/", EndpointPlatform.Vllm, customClient);
        var unknown = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetModelControls(custom));
        Assert.AreEqual("Unknown", unknown.GetProperty("speed").GetProperty("fast").GetString());
        Assert.Throws<ArgumentException>(() => ChatUiSettingsHelpers.ResolveSpeed(custom, "Fast", InferenceSpeed.ProviderDefault));
        Assert.AreEqual(InferenceSpeed.ProviderDefault,
            ChatUiSettingsHelpers.ResolveSpeed(custom, "ProviderDefault", InferenceSpeed.ProviderDefault));
        Assert.Throws<ArgumentException>(() => ChatUiSettingsHelpers.ResolveSpeed(custom, "99", InferenceSpeed.ProviderDefault));
    }

    [TestMethod]
    public void CatalogueAndConnectedControls_ExposeTheProviderCapabilitySnapshot()
    {
        var models = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var entry = models.EnumerateArray().Single(g => g.GetProperty("provider").GetString() == "Anthropic")
            .GetProperty("models").EnumerateArray().Single(m => m.GetProperty("name").GetString() == "ClaudeOpus5_5");
        Assert.AreEqual("Supported", entry.GetProperty("speed").GetProperty("fast").GetString());
        Assert.AreEqual("Supported", entry.GetProperty("capabilities").GetProperty("functionCalling").GetString());
        var unknown = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetCapabilityControls(AIModelCapabilities.Unknown));
        Assert.IsTrue(unknown.EnumerateObject().All(item => item.Value.GetString() == "Unknown"));
    }

    [TestMethod]
    public void ConnectedControls_SeparateConfiguredBudgetFromTheModelMaximum()
    {
        using var client = new HttpClient(new StreamHandler());
        var service = new OpenAIService("offline", AIModels.OpenAI.Gpt6Astra, client) { MaxTokens = 16000 };
        var controls = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetModelControls(service));
        Assert.AreEqual(16000u, controls.GetProperty("currentMaxTokens").GetUInt32());
        Assert.AreEqual(128000u, controls.GetProperty("maxOutputTokens").GetUInt32());
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt5_1, "Gpt5_1")]
    [DataRow(AIModels.OpenAI.Gpt5_2, "Gpt5_2")]
    [DataRow(AIModels.OpenAI.Gpt5_3Codex, "Gpt5_3")]
    [DataRow(AIModels.OpenAI.Gpt5_4, "Gpt5_4")]
    [DataRow(AIModels.OpenAI.Gpt5_5, "Gpt5_5")]
    [DataRow(AIModels.OpenAI.Gpt5_6Sol, "Gpt5_6")]
    public void Export_PreservesTheSelectedGpt5FamilyReasoningOptions(string model, string family)
    {
        using var client = new HttpClient(new StreamHandler());
        var service = new OpenAIService("offline", model, client);
        var effort = typeof(OpenAIService).GetProperty(family + "ReasoningEffort")!;
        effort.SetValue(service, Enum.Parse(effort.PropertyType, "High"));
        typeof(OpenAIService).GetProperty(family + "ReasoningSummary")!.SetValue(service, ReasoningSummary.Detailed);
        typeof(OpenAIService).GetProperty(family + "Verbosity")!.SetValue(service, Verbosity.High);
        var code = ChatUiUtilityHelpers.GenerateCodeSnippet(service, "OpenAI", model, "hello");
        StringAssert.Contains(code, $"service.{family}ReasoningEffort = {family}Reasoning.High;");
        StringAssert.Contains(code, $"service.{family}ReasoningSummary = ReasoningSummary.Detailed;");
        StringAssert.Contains(code, $"service.{family}Verbosity = Verbosity.High;");
        if (family == "Gpt5_6")
            StringAssert.Contains(code, "service.Gpt5_6ReasoningMode = Gpt5_6ReasoningMode.Standard;");
        typeof(OpenAIService).GetProperty(family + "ReasoningSummary")!.SetValue(service, null);
        typeof(OpenAIService).GetProperty(family + "Verbosity")!.SetValue(service, null);
        var omitted = ChatUiUtilityHelpers.GenerateCodeSnippet(service, "OpenAI", model, "hello");
        StringAssert.Contains(omitted, $"service.{family}ReasoningSummary = null;");
        StringAssert.Contains(omitted, $"service.{family}Verbosity = null;");
    }

    [TestMethod]
    public async Task SseRelay_ReportsAPublishedProviderErrorOnce_PreservesPartialOutput_AndReleasesTheRun()
    {
        using var client = new HttpClient(new StreamHandler());
        var service = new RelayProbeService(client) { Outcome = "error" };
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;
        var run = await service.CreateRequest("hello").StartRunAsync();
        await ChatUiStreamRelay.WriteAsync(run, context);
        var events = ReadEvents(body);
        CollectionAssert.AreEqual(new[] { "text", "error" }, events.Select(item => item.GetProperty("type").GetString()).ToArray());
        Assert.AreEqual("partial answer", events[0].GetProperty("content").GetString());
        Assert.AreEqual("Provider rejected continuation.", events[1].GetProperty("content").GetString());
        Assert.IsFalse(Encoding.UTF8.GetString(body.ToArray()).Contains("[DONE]", StringComparison.Ordinal));
        Assert.IsTrue(run.Result.IsFaulted);
        Assert.AreEqual(1, service.CleanupCount);

        service.Outcome = "success";
        using var nextBody = new MemoryStream();
        context.Response.Body = nextBody;
        var nextRun = await service.CreateRequest("next request").StartRunAsync();
        await ChatUiStreamRelay.WriteAsync(nextRun, context);
        Assert.IsTrue(nextRun.Result.IsCompletedSuccessfully, "Error cleanup must release the next run.");
        StringAssert.Contains(Encoding.UTF8.GetString(nextBody.ToArray()), "data: [DONE]");
    }

    [TestMethod]
    public async Task SseRelay_ReportsProviderCancellationAsAnError_WhenTheClientDidNotCancel()
    {
        using var client = new HttpClient(new StreamHandler());
        var service = new RelayProbeService(client) { Outcome = "timeout" };
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;
        var run = await service.CreateRequest("hello").StartRunAsync();
        await ChatUiStreamRelay.WriteAsync(run, context);
        var events = ReadEvents(body);
        Assert.AreEqual(1, events.Count(item => item.GetProperty("type").GetString() == "error"));
        StringAssert.Contains(events.Last().GetProperty("content").GetString()!, "timed out");
        Assert.IsFalse(context.RequestAborted.IsCancellationRequested);
        Assert.IsFalse(Encoding.UTF8.GetString(body.ToArray()).Contains("[DONE]", StringComparison.Ordinal));
        Assert.AreEqual(1, service.CleanupCount);
    }

    [TestMethod]
    public async Task SseRelay_ClientCancellationPreservesPartialOutput_AndAwaitsCleanupWithoutSendingAnError()
    {
        using var client = new HttpClient(new StreamHandler());
        var service = new RelayProbeService(client) { Outcome = "wait" };
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        using var body = new OutputSignalStream();
        context.Response.Body = body;
        var run = await service.CreateRequest("hello").StartRunAsync(cancellationToken: cancellation.Token);
        var relay = ChatUiStreamRelay.WriteAsync(run, context);
        await body.OutputWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await relay.WaitAsync(TimeSpan.FromSeconds(5));
        var events = ReadEvents(body);
        Assert.AreEqual(1, events.Length);
        Assert.AreEqual("partial answer", events.Single().GetProperty("content").GetString());
        Assert.AreEqual("text", events.Single().GetProperty("type").GetString());
        Assert.IsTrue(run.Result.IsCanceled);
        Assert.AreEqual(1, service.CleanupCount);
    }

    private static JsonElement[] ReadEvents(MemoryStream body)
        => Encoding.UTF8.GetString(body.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("data: {", StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[6..])).ToArray();

    private sealed class RelayProbeService(HttpClient client) : XAIService("offline", AIModels.xAI.Grok4_7, client)
    {
        public string Outcome { get; set; } = "success";
        public int CleanupCount { get; private set; }
        protected override async IAsyncEnumerable<StreamingContent> StreamCoreAsync(Message message, StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                yield return new StreamingContent { Type = StreamingContentType.Text, Content = "partial answer" };
                await Task.Yield();
                if (Outcome == "error")
                {
                    yield return new StreamingContent { Type = StreamingContentType.Error, Content = "Provider rejected continuation." };
                    yield break;
                }
                if (Outcome == "timeout") throw new TaskCanceledException("Provider timed out.");
                if (Outcome == "wait") await Task.Delay(Timeout.Infinite, cancellationToken);
                yield return new StreamingContent { Type = StreamingContentType.Completion };
            }
            finally { CleanupCount++; }
        }
    }

    private sealed class OutputSignalStream : MemoryStream
    {
        public TaskCompletionSource OutputWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken);
            if (Encoding.UTF8.GetString(ToArray()).Contains("partial answer", StringComparison.Ordinal)) OutputWritten.TrySetResult();
        }
    }

    [TestMethod]
    public async Task ChatRequest_AppliesSpeedOnEveryRun_ThenOmitsDefault_AndPreservesRagContext()
    {
        var handler = new StreamHandler();
        using var client = new HttpClient(handler);
        var service = new XAIService("offline", AIModels.xAI.Grok4_7, client);
        var speeds = new[] { InferenceSpeed.Fast, InferenceSpeed.Fast, InferenceSpeed.Standard, InferenceSpeed.ProviderDefault };
        foreach (var speed in speeds)
        {
            var request = ChatUiSettingsHelpers.CreateChatRequest(service, new Message(ActorRole.User, "visible question"), speed,
                new AIRequestContext { RequestMessageOverride = new Message(ActorRole.User, "private retrieval context") });
            await using var run = await request.StartRunAsync(options: StreamOptions.TextOnlyOptions);
            var text = new StringBuilder();
            await foreach (var chunk in run.StreamAsync())
                if (chunk.Type == StreamingContentType.Text) text.Append(chunk.Content);
            var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual("answer", text.ToString());
            Assert.AreEqual(speed, result.Processing.Single().RequestedSpeed);
        }

        CollectionAssert.AreEqual(new string?[] { "priority", "priority", "default", null },
            handler.Requests.Select(body => body.TryGetProperty("service_tier", out var tier) ? tier.GetString() : null).ToArray());
        Assert.IsTrue(handler.Requests.All(body => body.GetRawText().Contains("private retrieval context", StringComparison.Ordinal)));
        Assert.IsTrue(service.ActivateChat.Messages.Where(message => message.Role == ActorRole.User)
            .All(message => message.Content == "visible question"));
    }

    [TestMethod]
    public void Export_UsesActualAlibabaEndpoint_RequestBuilder_AndCultureInvariantSettings()
    {
        using var client = new HttpClient(new StreamHandler());
        var service = new QwenService("http://localhost:8000/v1/", EndpointPlatform.Vllm, client)
        {
            Temperature = 0.25f, TopP = 0.75f, ModelIdOverride = "my-deployment", ThinkingMode = QwenThinking.On
        };
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var code = ChatUiUtilityHelpers.GenerateCodeSnippet(service, "Alibaba", "QwenMax", "Hello \"world\"",
                baseUrl: "http://user:password@localhost:8000/v1/?api_key=hidden", platform: "vllm");
            StringAssert.Contains(code, "new QwenService(\"http://localhost:8000/v1/\", EndpointPlatform.Vllm, httpClient)");
            StringAssert.Contains(code, "service.ModelIdOverride = \"my-deployment\"");
            StringAssert.Contains(code, "var request = service.CreateRequest(message);");
            StringAssert.Contains(code, "request.StartRunAsync(options: options, cancellationToken: cancellation.Token)");
            StringAssert.Contains(code, "request.GetCompletionAsync(cancellation.Token)");
            Assert.IsFalse(code.Contains("service.SendAsync", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("password", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("api_key", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("WithSpeed", StringComparison.Ordinal));
        }
        finally { CultureInfo.CurrentCulture = previous; }

        using var xaiClient = new HttpClient(new StreamHandler());
        var xai = new XAIService("do-not-export-key", AIModels.xAI.Grok4_7, xaiClient) { Temperature = 0.25f, TopP = 0.75f };
        var xaiCode = ChatUiUtilityHelpers.GenerateCodeSnippet(xai, "xAI", "Grok4_7", "Hi", InferenceSpeed.Fast);
        StringAssert.Contains(xaiCode, "request = request.WithSpeed(InferenceSpeed.Fast);");
        StringAssert.Contains(xaiCode, "service.Temperature = 0.25f;");
        Assert.IsFalse(xaiCode.Contains("do-not-export-key", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PresetUrlTool_CancellationReachesNetwork_AndDoesNotBecomeATimeoutResult()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);
        using var serviceClient = new HttpClient(new StreamHandler());
        var service = new XAIService("offline", serviceClient);
        ChatUiUtilityHelpers.RegisterPresetFunctions(service, client);
        var function = service.Functions.Single(f => f.Name == "get_url_content");
        CollectionAssert.AreEqual(new[] { "url" }, function.Parameters.Required.ToArray());
        Assert.AreEqual("integer", function.Parameters.Properties["max_length"].Type);
        using var cancellation = new CancellationTokenSource();
        var pending = function.HandlerWithCancellation!(new Dictionary<string, object> { ["url"] = "https://example.com" }, cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.IsTrue(handler.ObservedCancellation);
    }

    private sealed class StreamHandler : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(body.RootElement.Clone());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"id\":\"resp_stream\",\"service_tier\":\"default\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"answer\"},\"finish_reason\":null}]}\n\n" +
                    "data: {\"id\":\"resp_stream\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n",
                    Encoding.UTF8, "text/event-stream")
            };
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ObservedCancellation { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.SetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { ObservedCancellation = cancellationToken.IsCancellationRequested; throw; }
            throw new InvalidOperationException("Expected cancellation.");
        }
    }
}
