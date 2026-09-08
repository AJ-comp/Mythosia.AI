using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using System.Text;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Live")]
[TestCategory("RequestFeatures")]
[DoNotParallelize]
public class RequestFeaturesLiveTests
{
    [TestMethod]
    [TestCategory("OpenAI")]
    [DataRow(false)]
    [DataRow(true)]
    public Task OpenAI_WebSearch_ReturnsRealCitations(bool useRun) => WebSearchAsync("OpenAI", useRun);

    [TestMethod]
    [TestCategory("Anthropic")]
    [DataRow(false)]
    [DataRow(true)]
    public Task Anthropic_WebSearch_ReturnsRealCitations(bool useRun) => WebSearchAsync("Anthropic", useRun);

    [TestMethod]
    [TestCategory("Google")]
    [DataRow(false)]
    [DataRow(true)]
    public Task Google_WebSearch_ReturnsRealCitations(bool useRun) => WebSearchAsync("Google", useRun);

    [TestMethod]
    [TestCategory("OpenAI")]
    [DataRow(false)]
    [DataRow(true)]
    public Task OpenAI_Reasoning_ChangesLevelWithRequiredCachePreservation(bool useRun) =>
        ReasoningAsync("OpenAI", useRun, CachePreservation.Required);

    [TestMethod]
    [TestCategory("Anthropic")]
    [DataRow(false)]
    [DataRow(true)]
    public Task Anthropic_Opus5_ChangesEffortWithRequiredCachePreservation(bool useRun) =>
        ReasoningAsync("Anthropic", useRun, CachePreservation.Required);

    [TestMethod]
    [TestCategory("Google")]
    [DataRow(false)]
    [DataRow(true)]
    public Task Google_Reasoning_ChangesSupportedThinkingLevel(bool useRun) =>
        ReasoningAsync("Google", useRun, CachePreservation.None);

    [TestMethod]
    [TestCategory("OpenAI")]
    [DataRow(false)]
    [DataRow(true)]
    public Task OpenAI_FileSearch_RetrievesSyntheticDocumentAndCitation(bool useRun) => FileSearchAsync("OpenAI", useRun);

    [TestMethod]
    [TestCategory("Google")]
    [DataRow(false)]
    [DataRow(true)]
    public Task Google_FileSearch_RetrievesSyntheticDocumentAndCitation(bool useRun) => FileSearchAsync("Google", useRun);

    private static async Task WebSearchAsync(string provider, bool useRun)
    {
        using var http = new HttpClient();
        var service = await CreateServiceAsync(provider, http);
        service.WithWebSearch();
        string prompt = $"Use your web search tool now to search NASA's official website for its most recent " +
            $"Mars mission news as of {DateTime.UtcNow:yyyy-MM-dd}. Summarize one item in one sentence and cite the source URL. " +
            "You must perform the search instead of answering from memory.";
        var result = await ExecuteAsync(service, prompt, useRun);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Text));
        Assert.IsTrue(result.Citations.Any(c => Uri.TryCreate(c.Url, UriKind.Absolute, out var url) &&
            (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)),
            "A plain answer without an actual provider citation does not verify web search.");
        Assert.IsTrue(result.Citations.All(c => string.Equals(c.Provider, provider, StringComparison.OrdinalIgnoreCase)));
        Console.WriteLine($"LIVE_FEATURE_OK provider={provider} feature=web-search mode={Mode(useRun)} citations={result.Citations.Count}");
    }

    private static async Task ReasoningAsync(string provider, bool useRun, CachePreservation cache)
    {
        using var http = new HttpClient();
        var service = await CreateServiceAsync(provider, http, reasoning: true);
        service.WithReasoning(ReasoningLevel.Medium);
        var first = await ExecuteAsync(service, "Calculate 17 times 19. Reply with only the integer.", useRun);
        StringAssert.Contains(first.Text, "323");

        service.WithReasoning(ReasoningLevel.Low, cache);
        var second = await ExecuteAsync(service, "Now calculate 18 times 19. Reply with only the integer.", useRun);
        StringAssert.Contains(second.Text, "342");
        // This verifies the real provider accepts the cache-preserving transition, not a guaranteed cache hit.
        Console.WriteLine($"LIVE_FEATURE_OK provider={provider} feature=reasoning mode={Mode(useRun)} cache={cache}");
    }

    private static async Task FileSearchAsync(string provider, bool useRun)
    {
        string key = await LiveTestSecrets.GetAsync(SecretName(provider));
        using var creation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var fixture = await HostedSearchLiveFixture.CreateAsync(provider, key, creation.Token);
        using var http = new HttpClient();
        var service = CreateService(provider, key, http);
        service.WithFileSearch(fixture.Store);

        var result = await ExecuteAsync(service, fixture.Query, useRun);
        StringAssert.Contains(result.Text, fixture.VerificationToken,
            "The verification token is supplied only in the synthetic document, never in the prompt.");
        Assert.IsTrue(result.Citations.Count > 0, "The retrieved answer must expose its provider citation.");
        Assert.IsTrue(result.Citations.All(c => string.Equals(c.Provider, provider, StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Citations.Any(c =>
            (!string.IsNullOrEmpty(fixture.FileId) &&
                (string.Equals(c.FileId, fixture.FileId, StringComparison.Ordinal) ||
                 (c.Url?.Contains(fixture.FileId, StringComparison.Ordinal) ?? false))) ||
            (c.Title?.Contains(fixture.DocumentTitle, StringComparison.Ordinal) ?? false)),
            "At least one citation must identify the document created by this test.");
        Console.WriteLine($"LIVE_FEATURE_OK provider={provider} feature=file-search mode={Mode(useRun)} citations={result.Citations.Count}");
    }

    private static async Task<(string Text, IReadOnlyList<AICitation> Citations)> ExecuteAsync(
        AIService service, string prompt, bool useRun)
    {
        if (!useRun)
        {
            string text = await service.GetCompletionAsync(prompt).WaitAsync(TimeSpan.FromMinutes(4));
            return (text, service.LastCitations.ToArray());
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var observed = new StringBuilder();
        await using var run = await service.StartRunAsync(prompt, text => observed.Append(text),
            cancellationToken: cancellation.Token);
        int completed = 0;
        await foreach (var item in run.StreamAsync(cancellation.Token))
            if (item.Type == StreamingContentType.Completion) completed++;
        string result = await run.Result;
        Assert.AreEqual(result, observed.ToString(), "The callback and result must observe the same response.");
        Assert.AreEqual(1, completed, "A run must emit exactly one final completion.");
        return (result, run.Citations.ToArray());
    }

    private static async Task<AIService> CreateServiceAsync(string provider, HttpClient http, bool reasoning = false) =>
        CreateService(provider, await LiveTestSecrets.GetAsync(SecretName(provider)), http, reasoning);

    private static AIService CreateService(string provider, string key, HttpClient http, bool reasoning = false)
    {
        AIService service = provider switch
        {
            "OpenAI" => new OpenAIService(key, AIModels.OpenAI.Gpt6Astra, http),
            "Anthropic" => new AnthropicService(key, reasoning ? AIModels.Anthropic.ClaudeOpus5 : AIModels.Anthropic.ClaudeSonnet5, http),
            "Google" => new GoogleAIService(key, AIModels.Google.Gemini3_6Flash, http),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        service.MaxTokens = 4096;
        service.DefaultPolicy.TimeoutSeconds = 210;
        service.DefaultPolicy.MaxRounds = 8;
        return service;
    }

    private static string SecretName(string provider) => provider switch
    {
        "OpenAI" => "momedit-openai-secret",
        "Anthropic" => "momedit-antropic-secret",
        "Google" => "gemini-secret",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
    private static string Mode(bool useRun) => useRun ? "run" : "completion";
}
