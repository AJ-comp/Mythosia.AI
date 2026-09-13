using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class RunResultProviderMetadataTests
{
    private const string ReportedModel = "provider-reported-model-version";

    public static IEnumerable<object[]> Providers => new[]
    {
        "OpenAI Chat", "OpenAI Responses", "Anthropic", "Google", "xAI", "DeepSeek",
        "Perplexity", "Qwen", "vLLM", "Ollama"
    }.Select(provider => new object[] { provider });

    public static IEnumerable<object[]> MetadataCases => Providers.SelectMany(provider =>
        new[] { true, false }.SelectMany(includeMetadata =>
            new[] { true, false }.Select(includeModel => new[] { provider[0], includeMetadata, includeModel })));

    public static IEnumerable<object[]> UsageCases => Providers.SelectMany(provider =>
        new[] { true, false }.Select(reportZero => new[] { provider[0], reportZero }));

    [TestMethod]
    [DynamicData(nameof(UsageCases))]
    public async Task RunResult_DistinguishesMissingUsageFromExplicitZeroForEachProvider(string provider, bool reportZero)
    {
        using var client = new HttpClient(new SseHandler(StreamBodyWithUsage(provider, reportZero)));
        var service = CreateService(provider, client);
        await using var run = await service.StartRunAsync("question");

        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("answer", result.Text);
        Assert.AreEqual(ReportedModel, result.Model);
        Assert.AreEqual(1, result.RoundCount);
        Assert.AreEqual(AIFinishReason.Stop, result.FinishReason);
        if (!reportZero)
        {
            Assert.IsNull(result.Usage, "Omitting every provider usage record must not manufacture a zero estimate.");
            return;
        }
        Assert.IsNotNull(result.Usage, "An explicitly reported zero is still a reported usage record.");
        Assert.AreEqual(0, result.Usage.InputTokens);
        Assert.AreEqual(0, result.Usage.OutputTokens);
        Assert.AreEqual(0, result.Usage.TotalTokens);
        Assert.AreEqual(0, result.Usage.CachedInputTokens);
        Assert.AreEqual(0, result.Usage.CacheCreationTokens);
        Assert.AreEqual(0, result.Usage.ReasoningTokens);
    }

    [TestMethod]
    [DynamicData(nameof(MetadataCases))]
    public async Task StreamCompletion_RetainsTypedProviderDataIndependentlyOfMetadataVisibility(
        string provider, bool includeMetadata, bool includeModel)
    {
        using var client = new HttpClient(new SseHandler(StreamBody(provider, includeModel)));
        var service = CreateService(provider, client);
        var events = new List<StreamingContent>();
        await foreach (var item in service.StreamAsync("question", new StreamOptions
        {
            IncludeMetadata = includeMetadata,
            IncludeFunctionCalls = false
        }))
            events.Add(item);

        Assert.AreEqual("answer", string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        var completion = events.Single(item => item.Type == StreamingContentType.Completion);
        Assert.AreEqual(includeModel ? ReportedModel : null, completion.ResponseModel,
            "An omitted server model must not be replaced by the requested model.");
        Assert.AreEqual(ExpectedRawReason(provider), completion.RawFinishReason);
        Assert.AreEqual(AIFinishReason.Stop, completion.FinishReason);
        Assert.IsNotNull(completion.Usage);
        Assert.AreEqual(13, completion.Usage.TotalTokens);
        if (!includeMetadata) Assert.IsNull(completion.Metadata);
    }

    [TestMethod]
    [DynamicData(nameof(Providers))]
    public async Task RunResult_RetainsProviderDataWithoutReadingStream(string provider)
    {
        using var client = new HttpClient(new SseHandler(StreamBody(provider, includeModel: true)));
        var service = CreateService(provider, client);
        await using var run = await service.StartRunAsync("question");

        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("answer", result.Text);
        Assert.AreEqual(ReportedModel, result.Model);
        Assert.AreEqual(service.Model, result.RequestedModel);
        Assert.AreEqual(service.Provider, result.Provider);
        Assert.AreEqual(ExpectedRawReason(provider), result.RawFinishReason);
        Assert.AreEqual(AIFinishReason.Stop, result.FinishReason);
        Assert.AreEqual(1, result.RoundCount);
        Assert.IsNotNull(result.Usage);
        Assert.AreEqual(13, result.Usage.TotalTokens);
    }

    private static string ExpectedRawReason(string provider) => provider switch
    {
        "OpenAI Responses" or "Perplexity" => "completed",
        "Anthropic" => "end_turn",
        "Google" => "STOP",
        _ => "stop"
    };

    private static AIService CreateService(string provider, HttpClient client)
    {
        AIService service = provider switch
        {
            "OpenAI Chat" => new OpenAIService("offline-key", AIModels.OpenAI.Gpt4o, client),
            "OpenAI Responses" => new OpenAIService("offline-key", AIModels.OpenAI.Gpt4_1, client),
            "Anthropic" => new AnthropicService("offline-key", client),
            "Google" => new GoogleAIService("offline-key", AIModels.Google.Gemini2_5Flash, client),
            "xAI" => new XAIService("offline-key", client),
            "DeepSeek" => new DeepSeekService("offline-key", client),
            "Perplexity" => new PerplexityService("offline-key", client),
            "Qwen" => new QwenService("offline-key", client),
            "vLLM" => new QwenService("https://offline.invalid/", EndpointPlatform.Vllm, client),
            "Ollama" => new QwenService("https://offline.invalid/", EndpointPlatform.Ollama, client),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        service.DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = null, MaxRounds = 3, EnableLogging = false };
        return service;
    }

    private static string StreamBody(string provider, bool includeModel)
    {
        var model = includeModel ? $"\"model\":\"{ReportedModel}\"," : string.Empty;
        if (provider is "OpenAI Responses" or "Perplexity")
            return Sse(
                """{"type":"response.output_text.delta","delta":"answer"}""",
                """{"type":"response.completed","response":{MODEL"id":"resp-test","status":"completed","usage":{"input_tokens":10,"output_tokens":3,"total_tokens":13},"output":[{"id":"msg-test","type":"message","role":"assistant","status":"completed","content":[{"type":"output_text","text":"answer"}]}]}}""".Replace("MODEL", model));
        if (provider == "Anthropic")
            return Sse(
                """{"type":"message_start","message":{MODEL"id":"msg-test","type":"message","role":"assistant","content":[],"usage":{"input_tokens":10,"output_tokens":0}}}""".Replace("MODEL", model),
                """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
                """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"answer"}}""",
                """{"type":"content_block_stop","index":0}""",
                """{"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":3}}""",
                """{"type":"message_stop"}""");
        if (provider == "Google")
            return Sse("""{MODEL"candidates":[{"content":{"role":"model","parts":[{"text":"answer"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":3,"totalTokenCount":13}}"""
                .Replace("MODEL", includeModel ? $"\"modelVersion\":\"{ReportedModel}\"," : string.Empty));

        // Model is only present on the first chunk, so terminal accumulation is required.
        return Sse(
            """{MODEL"choices":[{"index":0,"delta":{"role":"assistant","content":"answer"},"finish_reason":null}]}""".Replace("MODEL", model),
            """{"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""",
            """{"choices":[],"usage":{"prompt_tokens":10,"completion_tokens":3,"total_tokens":13}}""",
            "[DONE]");
    }

    private static string Sse(params string[] events) => string.Concat(events.Select(item => $"data: {item}\n\n"));

    private static string StreamBodyWithUsage(string provider, bool reportZero)
    {
        var events = StreamBody(provider, includeModel: true)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Substring("data: ".Length))
            .Select(json =>
            {
                if (json == "[DONE]") return json;
                var root = JsonNode.Parse(json)!;
                RewriteUsage(root, reportZero);
                return root.ToJsonString();
            }).ToArray();
        return Sse(events);
    }

    private static void RewriteUsage(JsonNode node, bool reportZero)
    {
        if (node is JsonObject record)
        {
            foreach (var property in record.ToArray())
            {
                if (property.Key is "usage" or "usageMetadata")
                {
                    if (!reportZero) record.Remove(property.Key);
                    else if (property.Value is JsonObject usage)
                        foreach (var count in usage.ToArray()) usage[count.Key] = 0;
                }
                else if (property.Value != null) RewriteUsage(property.Value, reportZero);
            }
        }
        else if (node is JsonArray items)
            foreach (var item in items)
                if (item != null) RewriteUsage(item, reportZero);
    }

    private sealed class SseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            });
    }
}
