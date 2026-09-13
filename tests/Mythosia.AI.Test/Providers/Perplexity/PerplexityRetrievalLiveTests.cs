using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb.InMemory;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Perplexity;

[TestClass]
[TestCategory("Live")]
[TestCategory("Perplexity")]
[TestCategory("PerplexityRetrieval")]
[DoNotParallelize]
public sealed class PerplexityRetrievalLiveTests
{
    private const string Refund = "Customers may request a refund within fourteen days after purchase.";
    private const string Shipping = "International shipping normally takes ten business days.";

    [TestMethod]
    public async Task Search_IndependentApiReturnsRankedPublicSources()
    {
        using var probe = await PerplexityRetrievalLiveProbe.CreateAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var result = await probe.Search.SearchAsync("Perplexity API authentication bearer token", new()
        {
            MaxResults = 3, DomainFilter = ["docs.perplexity.ai"], ContentSize = PerplexitySearchContentSize.Low
        }, cancellation.Token);
        AssertSearchResults(result, requireResults: true);
        Assert.IsTrue(result.Results.All(item => new Uri(item.Url).Host == "docs.perplexity.ai"));
        Assert.IsTrue(probe.Requests[0].Body["search_domain_filter"]![0]!.GetValue<string>() == "docs.perplexity.ai");
        Assert.AreEqual("low", probe.Requests[0].Body["search_context_size"]!.GetValue<string>());
        probe.AssertTransport(1, "/search");
        Console.WriteLine("LIVE_PERPLEXITY_RETRIEVAL_OK feature=search-ranked");
    }

    [TestMethod]
    public async Task Search_MultiQueryFiltersAndBudgetsReachActualRequest()
    {
        using var probe = await PerplexityRetrievalLiveProbe.CreateAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var queries = new[] { "Perplexity API search documentation", "Perplexity embeddings API documentation" };
        var result = await probe.Search.SearchAsync(queries, new()
        {
            MaxResults = 3, Country = "US", LanguageFilter = ["en"], PublishedAfter = new DateTime(2026, 1, 1),
            PublishedBefore = new DateTime(2026, 9, 11), UpdatedAfter = new DateTime(2026, 1, 1),
            UpdatedBefore = new DateTime(2026, 9, 11), MaxTokens = 1200, MaxTokensPerPage = 400
        }, cancellation.Token);
        AssertSearchResults(result, requireResults: false);
        var body = probe.Requests.Single().Body;
        Assert.IsTrue(body["query"]!.AsArray().Select(value => value!.GetValue<string>()).SequenceEqual(queries));
        Assert.IsTrue(body["country"]!.GetValue<string>() == "US" && body["search_language_filter"]![0]!.GetValue<string>() == "en");
        Assert.IsTrue(body["search_after_date_filter"]!.GetValue<string>() == "01/01/2026");
        Assert.IsTrue(body["search_before_date_filter"]!.GetValue<string>() == "09/11/2026");
        Assert.IsTrue(body["last_updated_after_filter"]!.GetValue<string>() == "01/01/2026");
        Assert.IsTrue(body["last_updated_before_filter"]!.GetValue<string>() == "09/11/2026");
        Assert.AreEqual(1200, body["max_tokens"]!.GetValue<int>());
        Assert.AreEqual(400, body["max_tokens_per_page"]!.GetValue<int>());
        Assert.IsFalse(body.ContainsKey("search_context_size"));
        probe.AssertTransport(1, "/search");
        Console.WriteLine("LIVE_PERPLEXITY_RETRIEVAL_OK feature=search-multi-filters");
    }

    [TestMethod]
    public async Task Search_PeopleSearchFindsAPublicFigure()
    {
        using var probe = await PerplexityRetrievalLiveProbe.CreateAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var result = await probe.Search.SearchAsync("Demis Hassabis", new()
        { SearchType = PerplexitySearchType.People, MaxResults = 3 }, cancellation.Token);
        AssertSearchResults(result, requireResults: true);
        Assert.IsTrue(result.Results.Any(item => (item.Title + " " + item.Snippet).Contains("Hassabis", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual("people", probe.Requests.Single().Body["search_type"]!.GetValue<string>());
        Assert.IsFalse(probe.Requests.Single().Body.ContainsKey("search_context_size"));
        probe.AssertTransport(1, "/search");
        Console.WriteLine("LIVE_PERPLEXITY_RETRIEVAL_OK feature=search-people");
    }

    [TestMethod]
    [DataRow(PerplexityEmbeddingModels.Standard0_6B)]
    [DataRow(PerplexityEmbeddingModels.Standard4B)]
    public async Task StandardInt8_IndexAndRetrieveThroughRealRagPipeline(string model)
    {
        using var probe = await PerplexityRetrievalLiveProbe.CreateAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var pipeline = new RagPipeline(probe.Standard(model), new InMemoryVectorStore(), new CharacterTextSplitter(300, 0),
            new DefaultContextBuilder(), new RagPipelineOptions
            { DefaultQuery = new RagQueryOptions { FinalFilter = new RagFilter { TopK = 1 } } });
        await pipeline.IndexDocumentAsync(new RagDocument { Id = "refund", Source = "synthetic-refund.txt", Content = Refund }, cancellation.Token);
        await pipeline.IndexDocumentAsync(new RagDocument { Id = "shipping", Source = "synthetic-shipping.txt", Content = Shipping }, cancellation.Token);
        var result = await pipeline.QueryAsync(Refund, cancellationToken: cancellation.Token);
        Assert.HasCount(1, result.SearchResults);
        Assert.IsTrue(result.SearchResults[0].Record.Content == Refund, "The matching source must rank above the unrelated source.");
        Assert.IsGreaterThan(0.99d, result.SearchResults[0].Score);
        PerplexityRetrievalLiveProbe.AssertNormalized(result.SearchResults[0].Record.Vector);
        Assert.IsTrue(result.Context.Contains(Refund, StringComparison.Ordinal));
        probe.AssertTransport(3, "/v1/embeddings", model, "base64_int8");
        Console.WriteLine("LIVE_PERPLEXITY_RETRIEVAL_OK feature=standard-rag model=" + model);
    }

    [TestMethod]
    [DataRow(PerplexityEmbeddingModels.Context0_6B)]
    [DataRow(PerplexityEmbeddingModels.Context4B)]
    public async Task ContextInt8_PreservesDocumentsAndEmbedsQueryInSameSpace(string model)
    {
        using var probe = await PerplexityRetrievalLiveProbe.CreateAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var provider = probe.Context(model);
        var documents = new[] { new[] { Refund, "The item must remain unused and include its original receipt." }, new[] { Shipping } };
        var groups = await provider.GetDocumentEmbeddingsAsync(documents, cancellation.Token);
        Assert.HasCount(2, groups);
        Assert.HasCount(2, groups[0]);
        Assert.HasCount(1, groups[1]);
        foreach (var vector in groups.SelectMany(group => group)) PerplexityRetrievalLiveProbe.AssertNormalized(vector);
        var query = await provider.GetQueryEmbeddingAsync(Refund, cancellation.Token);
        PerplexityRetrievalLiveProbe.AssertNormalized(query);
        AssertGroups(probe.Requests[0].Body["input"]!.AsArray(), documents);
        AssertGroups(probe.Requests[1].Body["input"]!.AsArray(), new[] { new[] { Refund } });
        probe.AssertTransport(2, "/v1/contextualizedembeddings", model, "base64_int8");
        Console.WriteLine("LIVE_PERPLEXITY_RETRIEVAL_OK feature=context-groups-query model=" + model);
    }

    [TestMethod]
    [DataRow(PerplexityEmbeddingModels.Standard0_6B, false)]
    [DataRow(PerplexityEmbeddingModels.Standard4B, false)]
    [DataRow(PerplexityEmbeddingModels.Context0_6B, true)]
    [DataRow(PerplexityEmbeddingModels.Context4B, true)]
    public async Task Binary_AllModelsReturnPackedBitsWithExactHammingSemantics(string model, bool contextualized)
    {
        using var probe = await PerplexityRetrievalLiveProbe.CreateAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        IReadOnlyList<PerplexityBinaryEmbedding> vectors;
        if (contextualized)
        {
            // Identical independent documents must produce identical contextualized bits.
            var groups = await probe.Context(model).GetBinaryDocumentEmbeddingsAsync(new[] { new[] { Refund }, new[] { Refund }, new[] { Shipping } }, cancellation.Token);
            Assert.HasCount(3, groups);
            Assert.IsTrue(groups.All(group => group.Count == 1));
            vectors = groups.Select(group => group[0]).ToArray();
        }
        else vectors = await probe.Standard(model).GetBinaryEmbeddingsAsync(new[] { Refund, Refund, Shipping }, cancellation.Token);
        Assert.HasCount(3, vectors);
        Assert.IsTrue(vectors.All(vector => vector.Dimensions == 128 && vector.ToArray().Length == 16));
        Assert.AreEqual(0, vectors[0].HammingDistance(vectors[1]));
        Assert.IsGreaterThan(0, vectors[0].HammingDistance(vectors[2]));
        Assert.IsLessThanOrEqualTo(128, vectors[0].HammingDistance(vectors[2]));
        Assert.AreEqual(vectors[0].HammingDistance(vectors[2]), vectors[2].HammingDistance(vectors[0]));
        probe.AssertTransport(1, contextualized ? "/v1/contextualizedembeddings" : "/v1/embeddings", model, "base64_binary");
        Console.WriteLine("LIVE_PERPLEXITY_RETRIEVAL_OK feature=binary-hamming model=" + model);
    }

    private static void AssertSearchResults(PerplexitySearchResponse response, bool requireResults)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.Id));
        if (requireResults) Assert.IsNotEmpty(response.Results);
        Assert.IsLessThanOrEqualTo(3, response.Results.Count);
        for (var index = 0; index < response.Results.Count; index++)
        {
            var item = response.Results[index];
            Assert.AreEqual(index + 1, item.Rank);
            Assert.IsFalse(string.IsNullOrWhiteSpace(item.Title));
            Assert.IsFalse(string.IsNullOrWhiteSpace(item.Snippet));
            Assert.IsTrue(Uri.TryCreate(item.Url, UriKind.Absolute, out var url) && url.Scheme is "http" or "https");
        }
    }

    private static void AssertGroups(JsonArray actual, string[][] expected)
    {
        Assert.HasCount(expected.Length, actual);
        for (var index = 0; index < expected.Length; index++)
            Assert.IsTrue(actual[index]!.AsArray().Select(value => value!.GetValue<string>()).SequenceEqual(expected[index]),
                "Original document groups and chunk ordering must reach the real request unchanged.");
    }
}
