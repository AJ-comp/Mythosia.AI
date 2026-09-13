using System.Net;
using System.Text.Json.Nodes;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class PerplexitySearchClientTests
{
    private const string Pages = """
        {"id":"search-1","server_time":"2026-09-11T00:00:00Z","results":[
          {"title":"Source A","url":"https://example.org/a","snippet":"first passage","date":"2026-09-01","last_updated":"2026-09-09"},
          {"title":"Source B","url":"https://example.net/b","snippet":"second passage"}]}
        """;

    [TestMethod]
    public async Task SingleQuery_PreservesRankMetadataAndCallerHttpClient()
    {
        using var handler = new PerplexityApiTestTransport { Reply = _ => Pages };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://caller.example/") };
        http.DefaultRequestHeaders.Add("X-Caller", "retained");
        var client = new PerplexitySearchClient("test-key", http);
        var result = await client.SearchAsync("independent search");
        var body = handler.Bodies.Single();
        Assert.AreEqual("https://api.perplexity.ai/search", handler.Uris.Single().AbsoluteUri);
        Assert.AreEqual("Bearer test-key", handler.Authorization.Single());
        Assert.AreEqual("independent search", body["query"]!.GetValue<string>());
        Assert.AreEqual(10, body["max_results"]!.GetValue<int>());
        Assert.AreEqual("web", body["search_type"]!.GetValue<string>());
        Assert.AreEqual(3, body.Count);
        Assert.AreEqual("search-1", result.Id);
        Assert.AreEqual("2026-09-11T00:00:00Z", result.ServerTime);
        CollectionAssert.AreEqual(new[] { 1, 2 }, result.Results.Select(page => page.Rank).ToArray());
        Assert.AreEqual("Source A", result.Results[0].Title);
        Assert.AreEqual("first passage", result.Results[0].Snippet);
        Assert.AreEqual("2026-09-01", result.Results[0].Date);
        Assert.AreEqual("2026-09-09", result.Results[0].LastUpdated);
        Assert.IsNull(result.Results[1].Date);
        Assert.AreEqual("https://caller.example/", http.BaseAddress.AbsoluteUri);
        Assert.AreEqual("retained", http.DefaultRequestHeaders.GetValues("X-Caller").Single());
        Assert.IsNull(http.DefaultRequestHeaders.Authorization);
        Assert.IsTrue(handler.Contents.Single().WasDisposed);
        await client.SearchAsync("second call proves caller client remains usable");
        Assert.HasCount(2, handler.Bodies);
    }

    [TestMethod]
    public async Task MultipleQueries_MapsFiltersDatesAndIndependentBudgets()
    {
        using var handler = new PerplexityApiTestTransport { Reply = _ => Pages };
        using var http = new HttpClient(handler);
        var result = await new PerplexitySearchClient("key", http).SearchAsync(new[] { "query one", "query two" }, new()
        {
            MaxResults = 20, Country = "kr", DomainFilter = ["-example.org/private", "-example.net"],
            LanguageFilter = ["KO", "en"], PublishedAfter = new DateTime(2026, 1, 2), PublishedBefore = new DateTime(2026, 9, 10),
            UpdatedAfter = new DateTime(2026, 8, 1), UpdatedBefore = new DateTime(2026, 9, 11),
            Recency = PerplexitySearchRecency.Month, MaxTokens = 2000, MaxTokensPerPage = 500
        });
        var body = handler.Bodies.Single();
        CollectionAssert.AreEqual(new[] { "query one", "query two" }, body["query"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray());
        Assert.AreEqual("KR", body["country"]!.GetValue<string>());
        Assert.AreEqual("ko", body["search_language_filter"]![0]!.GetValue<string>());
        Assert.AreEqual("-example.org/private", body["search_domain_filter"]![0]!.GetValue<string>());
        Assert.AreEqual("01/02/2026", body["search_after_date_filter"]!.GetValue<string>());
        Assert.AreEqual("09/10/2026", body["search_before_date_filter"]!.GetValue<string>());
        Assert.AreEqual("08/01/2026", body["last_updated_after_filter"]!.GetValue<string>());
        Assert.AreEqual("09/11/2026", body["last_updated_before_filter"]!.GetValue<string>());
        Assert.AreEqual("month", body["search_recency_filter"]!.GetValue<string>());
        Assert.AreEqual(2000, body["max_tokens"]!.GetValue<int>());
        Assert.AreEqual(500, body["max_tokens_per_page"]!.GetValue<int>());
        Assert.IsFalse(body.ContainsKey("search_context_size"));
        Assert.HasCount(2, result.Results);
    }

    [TestMethod]
    public async Task PeopleFifty_IsSupportedWithEmptyResultsAndNoContentPreset()
    {
        using var handler = new PerplexityApiTestTransport { Reply = _ => "{\"id\":\"empty\",\"results\":[]}" };
        using var http = new HttpClient(handler);
        var result = await new PerplexitySearchClient("key", http).SearchAsync("person", new()
        { SearchType = PerplexitySearchType.People, MaxResults = 50 });
        Assert.IsEmpty(result.Results);
        Assert.AreEqual("people", handler.Bodies[0]["search_type"]!.GetValue<string>());
        Assert.AreEqual(50, handler.Bodies[0]["max_results"]!.GetValue<int>());
        Assert.IsFalse(handler.Bodies[0].ContainsKey("search_context_size"));
        Assert.IsFalse(handler.Bodies[0].ContainsKey("max_tokens"));
    }

    public static IEnumerable<object[]> InvalidOptions()
    {
        yield return [new PerplexitySearchOptions { MaxResults = 0 }];
        yield return [new PerplexitySearchOptions { MaxResults = 21 }];
        yield return [new PerplexitySearchOptions { SearchType = PerplexitySearchType.People, MaxResults = 51 }];
        yield return [new PerplexitySearchOptions { SearchType = (PerplexitySearchType)99 }];
        yield return [new PerplexitySearchOptions { ContentSize = (PerplexitySearchContentSize)99 }];
        yield return [new PerplexitySearchOptions { SearchType = PerplexitySearchType.People, ContentSize = PerplexitySearchContentSize.Low }];
        yield return [new PerplexitySearchOptions { Recency = (PerplexitySearchRecency)99 }];
        yield return [new PerplexitySearchOptions { Country = "USA" }];
        yield return [new PerplexitySearchOptions { LanguageFilter = ["eng"] }];
        yield return [new PerplexitySearchOptions { LanguageFilter = Enumerable.Repeat("en", 21).ToArray() }];
        yield return [new PerplexitySearchOptions { DomainFilter = ["example.org", "-example.net"] }];
        yield return [new PerplexitySearchOptions { DomainFilter = Enumerable.Repeat("example.org", 21).ToArray() }];
        yield return [new PerplexitySearchOptions { DomainFilter = [new string('x', 254)] }];
        yield return [new PerplexitySearchOptions { MaxTokens = 0 }];
        yield return [new PerplexitySearchOptions { MaxTokensPerPage = 1000001 }];
        yield return [new PerplexitySearchOptions { ContentSize = PerplexitySearchContentSize.High, MaxTokens = 1 }];
        yield return [new PerplexitySearchOptions { ContentSize = PerplexitySearchContentSize.High, MaxTokensPerPage = 1 }];
        yield return [new PerplexitySearchOptions { PublishedAfter = new DateTime(2026, 9, 11), PublishedBefore = new DateTime(2026, 9, 10) }];
        yield return [new PerplexitySearchOptions { UpdatedAfter = new DateTime(2026, 9, 11), UpdatedBefore = new DateTime(2026, 9, 10) }];
    }

    [TestMethod]
    [DynamicData(nameof(InvalidOptions))]
    public async Task InvalidOption_RejectsBeforeHttp(PerplexitySearchOptions options)
    {
        using var handler = new PerplexityApiTestTransport();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => new PerplexitySearchClient("key", http).SearchAsync("query", options));
        Assert.IsEmpty(handler.Bodies);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(6)]
    [DataRow(-1)]
    public async Task InvalidQueryBatch_RejectsBeforeHttp(int count)
    {
        using var handler = new PerplexityApiTestTransport();
        using var http = new HttpClient(handler);
        var queries = count < 0 ? new[] { " " } : Enumerable.Repeat("query", count).ToArray();
        await Assert.ThrowsAsync<ArgumentException>(() => new PerplexitySearchClient("key", http).SearchAsync(queries));
        Assert.IsEmpty(handler.Bodies);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"id\":\"x\",\"results\":null}")]
    [DataRow("{\"results\":[]}")]
    [DataRow("{\"id\":\"x\",\"results\":[{\"title\":\"x\",\"url\":\"javascript:alert(1)\",\"snippet\":\"x\"}]}")]
    [DataRow("{\"id\":\"x\",\"results\":[{\"title\":\"x\",\"url\":\"https://example.org\"}]}")]
    public async Task MalformedResponse_FailsWithoutPartialResults(string json)
    {
        using var handler = new PerplexityApiTestTransport { Reply = _ => json };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AIServiceException>(() => new PerplexitySearchClient("key", http).SearchAsync("query"));
        Assert.IsTrue(handler.Contents.Single().WasDisposed);
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(429)]
    [DataRow(500)]
    public async Task HttpFailure_IsNotASuccessfulEmptySearch(int status)
    {
        using var handler = new PerplexityApiTestTransport { Status = (HttpStatusCode)status };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AIServiceException>(() => new PerplexitySearchClient("key", http).SearchAsync("query"));
        Assert.IsTrue(handler.Contents.Single().WasDisposed);
    }

    [TestMethod]
    public async Task Cancellation_StopsPendingTransport()
    {
        using var handler = new PerplexityApiTestTransport { WaitForCancellation = true };
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var pending = new PerplexitySearchClient("key", http).SearchAsync("query", cancellationToken: cancellation.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
