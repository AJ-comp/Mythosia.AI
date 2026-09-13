using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Google;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class GoogleRequestFeatureTests
{
    private const string Answer = """{"responseId":"g1","candidates":[{"content":{"role":"model","parts":[{"text":"answer"}]},"finishReason":"STOP"}]}""";
    private const string Grounded = """
        {"responseId":"g-grounded","candidates":[{"content":{"role":"model","parts":[{"text":"answer"}]},"finishReason":"STOP",
        "groundingMetadata":{"groundingChunks":[{"web":{"uri":"https://example.com/source","title":"Source"}}],
        "groundingSupports":[{"segment":{"startIndex":0,"endIndex":6,"text":"answer"},"groundingChunkIndices":[0]}]}}]}
        """;

    [TestMethod]
    public async Task NativeReasoning_IsOneShot_AndDoesNotChangeProviderSettings()
    {
        var (service, handler) = Create();
        service.ThinkingLevel = Mythosia.AI.Models.Enums.GeminiThinkingLevel.Medium;
        await service.WithReasoning(ReasoningLevel.Low).GetCompletionAsync("first");
        await service.GetCompletionAsync("second");
        Assert.AreEqual("LOW", Body(handler, 0).GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        Assert.AreEqual("MEDIUM", Body(handler, 1).GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
    }

    [TestMethod]
    [DataRow("gemini-3.1-pro-preview", ReasoningLevel.Minimal)]
    [DataRow("gemini-3.6-flash", ReasoningLevel.None)]
    [DataRow("gemini-3.6-flash", ReasoningLevel.Max)]
    [DataRow("gemini-3.8-flash", ReasoningLevel.Minimal)]
    [DataRow("gemini-2.5-pro", ReasoningLevel.Low)]
    public async Task UnsupportedReasoning_FailsBeforeHistoryOrHttp(string model, ReasoningLevel level)
    {
        var (service, handler) = Create(model);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.WithReasoning(level).GetCompletionAsync("test"));
        Assert.AreEqual(0, handler.Bodies.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task Gemini25Flash_NoneUsesNativeZeroBudget()
    {
        var (service, handler) = Create("gemini-2.5-flash");
        await service.WithReasoning(ReasoningLevel.None).GetCompletionAsync("test");
        Assert.AreEqual(0, Body(handler, 0).GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
    }

    [TestMethod]
    public async Task UnsupportedCacheAndDomainConstraints_AreExplicit()
    {
        var (service, handler) = Create();
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("test"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } }).GetCompletionAsync("test"));
        Assert.AreEqual(0, handler.Bodies.Count);
    }

    [TestMethod]
    public async Task NativeWebSearch_ReturnsCommonCitations_AndDoesNotRegisterClientTool()
    {
        var (service, handler) = Create(responses: new[] { Grounded });
        service.FunctionsDisabled = true;
        Assert.AreEqual("answer", await service.WithWebSearch().GetCompletionAsync("test"));
        Assert.IsTrue(Body(handler, 0).GetProperty("tools")[0].TryGetProperty("googleSearch", out _));
        Assert.AreEqual(0, service.Functions.Count);
        Assert.AreEqual(1, service.LastCitations.Count);
        var citation = service.LastCitations[0];
        Assert.AreEqual("Google", citation.Provider);
        Assert.AreEqual("https://example.com/source", citation.Url);
        Assert.AreEqual("g-grounded", citation.ResponseId);
        Assert.AreEqual(0, citation.StartIndex);
        Assert.AreEqual(6, citation.EndIndex);
    }

    [TestMethod]
    public async Task FileSearch_UsesProviderStore_AndRejectsIncompatibleCombination()
    {
        var (service, handler) = Create();
        await service.WithFileSearch(new FileSearchStore("Google", "fileSearchStores/test")).GetCompletionAsync("test");
        Assert.AreEqual("fileSearchStores/test", Body(handler, 0).GetProperty("tools")[0].GetProperty("fileSearch").GetProperty("fileSearchStoreNames")[0].GetString());
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.WithFileSearch(new FileSearchStore("OpenAI", "vs_test")).GetCompletionAsync("test"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.WithFileSearch(new FileSearchStore("Google", "fileSearchStores/test")).WithWebSearch().GetCompletionAsync("test"));
        Assert.AreEqual(1, handler.Bodies.Count);
    }

    [TestMethod]
    public async Task WebAndClientTools_PreserveServerPartsAndEnableContextCirculation()
    {
        const string first = """
            {"candidates":[{"content":{"role":"model","parts":[
            {"toolCall":{"toolType":"GOOGLE_SEARCH_WEB","id":"s1","args":{"queries":["test"]}},"thoughtSignature":"server-signature"},
            {"toolResponse":{"toolType":"GOOGLE_SEARCH_WEB","id":"s1","response":{"search_suggestions":"result"}}},
            {"functionCall":{"name":"local","id":"c1","args":{}},"thoughtSignature":"client-signature"}]},"finishReason":"STOP"}]}
            """;
        var (service, handler) = Create(responses: new[] { first, Answer });
        var calls = 0;
        service.Functions.Add(new FunctionDefinition { Name = "local", Description = "Local", Handler = _ => { calls++; return Task.FromResult("done"); } });
        await service.WithWebSearch().GetCompletionAsync("test");
        Assert.AreEqual(1, calls);
        var request = Body(handler, 1);
        Assert.IsTrue(request.GetProperty("toolConfig").GetProperty("includeServerSideToolInvocations").GetBoolean());
        Assert.AreEqual(2, request.GetProperty("tools").GetArrayLength());
        var parts = request.GetProperty("contents")[1].GetProperty("parts");
        Assert.AreEqual("server-signature", parts[0].GetProperty("thoughtSignature").GetString());
        Assert.AreEqual("s1", parts[1].GetProperty("toolResponse").GetProperty("id").GetString());
        Assert.AreEqual("c1", request.GetProperty("contents")[2].GetProperty("parts")[0].GetProperty("functionResponse").GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task NativeFinalParts_ArePreservedOnNextOrdinaryRequest()
    {
        const string response = """
            {"candidates":[{"content":{"role":"model","parts":[
            {"toolCall":{"toolType":"GOOGLE_SEARCH_WEB","id":"s1"},"thoughtSignature":"signature"},
            {"toolResponse":{"toolType":"GOOGLE_SEARCH_WEB","id":"s1"}},{"text":"answer"}]},"finishReason":"STOP"}]}
            """;
        var (service, handler) = Create(responses: new[] { response, Answer });
        await service.WithWebSearch().GetCompletionAsync("first");
        await service.GetCompletionAsync("second");
        var parts = Body(handler, 1).GetProperty("contents")[1].GetProperty("parts");
        Assert.AreEqual("signature", parts[0].GetProperty("thoughtSignature").GetString());
        Assert.AreEqual("s1", parts[1].GetProperty("toolResponse").GetProperty("id").GetString());
        Assert.IsFalse(Body(handler, 1).TryGetProperty("tools", out _));
    }

    [TestMethod]
    public async Task RunWithoutObserver_StillCollectsCitations()
    {
        var (service, _) = Create(responses: new[] { "data: " + Grounded.Replace("\r", "").Replace("\n", "") + "\n\n" });
        await using var run = await service.WithWebSearch().StartRunAsync("test");
        Assert.AreEqual("answer", (await run.Result).Text);
        Assert.AreEqual(1, service.LastCitations.Count);
    }

    [TestMethod]
    public async Task RichStream_EmitsCitations_WhenMetadataDisabled()
    {
        var (service, _) = Create(responses: new[] { "data: " + Grounded.Replace("\r", "").Replace("\n", "") + "\n\n" });
        await using var run = await service.WithWebSearch().StartRunAsync("test", options: new StreamOptions { IncludeMetadata = false });
        var citations = new List<AICitation>();
        await foreach (var item in run.StreamAsync())
            if (item.Type == StreamingContentType.Citation) citations.Add(item.Citation!);
        Assert.AreEqual("answer", (await run.Result).Text);
        Assert.AreEqual(1, citations.Count);
    }

    private static (GoogleAIService Service, CaptureHandler Handler) Create(string model = "gemini-3.6-flash", string[]? responses = null)
    {
        var handler = new CaptureHandler(responses ?? new[] { Answer });
        return (new GoogleAIService("offline-key", model, new HttpClient(handler)), handler);
    }

    private static JsonElement Body(CaptureHandler handler, int index) => JsonSerializer.Deserialize<JsonElement>(handler.Bodies[index]);

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;
        public List<string> Bodies { get; } = new();
        public CaptureHandler(IEnumerable<string> responses) => _responses = new Queue<string>(responses);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var response = _responses.Count > 0 ? _responses.Dequeue() : Answer;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, response.StartsWith("data:") ? "text/event-stream" : "application/json")
            };
        }
    }
}
