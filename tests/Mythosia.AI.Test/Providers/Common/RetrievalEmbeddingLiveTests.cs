using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb.InMemory;
using System.Text.RegularExpressions;

namespace Mythosia.AI.Tests;

[TestClass]
[TestCategory("Live")]
[TestCategory("RetrievalEmbedding")]
[DoNotParallelize]
public sealed class RetrievalEmbeddingLiveTests
{
    private const string Refund = "Customers may request a refund within fourteen days after purchase. Unused items require the original receipt.";
    private const string Shipping = "International shipping takes ten business days. Packages travel by cargo ship from the coastal warehouse.";
    private const string Query = "How many days after purchase can customers request a refund?";

    [TestMethod]
    [TestCategory("Voyage")]
    [DataRow(".txt")]
    [DataRow(".md")]
    [DataRow(".pdf")]
    public Task Voyage_ExtractIndexAndRetrieve(string extension) => ExtractIndexAndRetrieveAsync("Voyage", extension);

    [TestMethod]
    [TestCategory("Gemini")]
    [DataRow(".txt")]
    [DataRow(".md")]
    [DataRow(".pdf")]
    public Task Gemini_ExtractIndexAndRetrieve(string extension) => ExtractIndexAndRetrieveAsync("Gemini", extension);

    private static async Task ExtractIndexAndRetrieveAsync(string providerName, string extension)
    {
        using var probe = await RetrievalEmbeddingLiveProbe.CreateAsync(providerName);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var fixture = new RetrievalEmbeddingLiveFixture();
        using var store = new InMemoryVectorStore();
        var splitter = new CharacterTextSplitter(2048, 0);
        var pipeline = new RagPipeline(probe.Provider, store, splitter, new DefaultContextBuilder());
        var refund = await fixture.LoadAsync(extension, "Synthetic refund policy", Refund, cancellation.Token);
        var shipping = await fixture.LoadAsync(extension, "Synthetic shipping policy", Shipping, cancellation.Token);
        StringAssert.Contains(Regex.Replace(refund.Content, @"\s+", " "), Refund);
        StringAssert.Contains(Regex.Replace(shipping.Content, @"\s+", " "), Shipping);
        var refundChunks = splitter.Split(refund);
        var shippingChunks = splitter.Split(shipping);
        Assert.HasCount(1, refundChunks);
        Assert.HasCount(1, shippingChunks);
        await pipeline.IndexDocumentsAsync([refund, shipping], cancellation.Token);
        var records = await store.ListAllRecordsAsync(cancellation.Token);
        Assert.HasCount(2, records);
        foreach (var record in records) RetrievalEmbeddingLiveProbe.AssertVector(record.Vector, probe.Provider.Dimensions);

        var result = await pipeline.QueryAsync(Query, topK: 1, cancellationToken: cancellation.Token);
        Assert.HasCount(1, result.SearchResults);
        Assert.AreEqual(refund.Id, result.SearchResults[0].Record.Metadata["document_id"], "The refund source must rank above the unrelated shipping source.");
        Assert.AreEqual(refundChunks[0].Content, result.SearchResults[0].Record.Content, "Embedding task formatting must not alter stored text.");
        StringAssert.Contains(Regex.Replace(result.Context, @"\s+", " "), "fourteen days");
        Assert.IsTrue(double.IsFinite(result.SearchResults[0].Score));
        probe.AssertTransport(documentCalls: 2, queryCalls: 1);
        if (providerName == "Gemini")
        {
            var inputs = probe.Requests.Select(request => request.Body["content"]!["parts"]![0]!["text"]!.GetValue<string>()).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "title: Synthetic refund policy | text: " + refundChunks[0].Content,
                "title: Synthetic shipping policy | text: " + shippingChunks[0].Content,
                "task: search result | query: " + Query
            }, inputs);
        }
        Console.WriteLine($"LIVE_RETRIEVAL_EMBEDDING_OK provider={providerName} feature=extract-index-query extension={extension}");
    }

    [TestMethod]
    [TestCategory("Voyage")]
    public async Task Voyage_OverOneHundredTinyChunksKeepOneDocumentContext()
    {
        using var probe = await RetrievalEmbeddingLiveProbe.CreateAsync("Voyage");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var store = new InMemoryVectorStore();
        var chunks = Enumerable.Range(0, 101).Select(index => $"Warehouse shelf {index} holds blue cardboard cartons.").ToArray();
        chunks[100] = Refund;
        var splitter = new LineSplitter();
        var pipeline = new RagPipeline(probe.Provider, store, splitter, new DefaultContextBuilder(),
            new RagPipelineOptions { EmbeddingBatchSize = 8 });
        var document = new RagDocument("synthetic-101-chunks", string.Join("\n", chunks), "synthetic-101-chunks.txt");
        await pipeline.IndexDocumentAsync(document, cancellation.Token);
        Assert.AreEqual(101L, await store.CountAsync(cancellationToken: cancellation.Token));
        foreach (var record in await store.ListAllRecordsAsync(cancellation.Token))
            RetrievalEmbeddingLiveProbe.AssertVector(record.Vector, 1024);
        var result = await pipeline.QueryAsync(Query, topK: 1, cancellationToken: cancellation.Token);
        Assert.HasCount(1, result.SearchResults);
        Assert.AreEqual(Refund, result.SearchResults[0].Record.Content);
        probe.AssertTransport(documentCalls: 1, queryCalls: 1);
        var sentChunks = probe.Requests[0].Body["inputs"]![0]!.AsArray().Select(chunk => chunk!.GetValue<string>()).ToArray();
        CollectionAssert.AreEqual(chunks, sentChunks, "A small embedding batch size must not split contextual document input.");
        Console.WriteLine("LIVE_RETRIEVAL_EMBEDDING_OK provider=Voyage feature=101-chunks-one-document");
    }

    private sealed class LineSplitter : ITextSplitter
    {
        public IReadOnlyList<RagChunk> Split(RagDocument document) => document.Content.Split('\n')
            .Select((text, index) => new RagChunk($"{document.Id}:{index}", document.Id, text, index)).ToArray();
    }
}
