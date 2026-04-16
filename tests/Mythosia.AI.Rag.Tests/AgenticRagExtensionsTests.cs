using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public class AgenticRagExtensionsTests
{
    [TestMethod]
    public async Task WithAgenticRag_QueryOptions_AppliesPerCallStoreFilter()
    {
        var store = await CreateTaggedStoreAsync();
        var service = new MockAIService();

        service.WithAgenticRag(
            store,
            queryOptions: _ => new RagQueryOptions
            {
                StoreFilter = new VectorFilter().Where("tenant", "alpha")
            });

        var tool = service.Functions.Single(f => f.Name == "search_documents");
        var response = await tool.Handler(new Dictionary<string, object>
        {
            ["query"] = "refund policy"
        });

        Assert.IsTrue(response.Contains("alpha-source"), "Filtered result should include the allowed tenant source.");
        Assert.IsFalse(response.Contains("beta-source"), "Filtered result should exclude other tenant sources.");
    }

    [TestMethod]
    public async Task WithAgenticRagTracing_ReceivesStructuredRagResult()
    {
        var store = await CreateTaggedStoreAsync();
        var service = new MockAIService();
        AgenticRagSearchTrace? captured = null;

        service
            .WithAgenticRag(
                store,
                queryOptions: _ => new RagQueryOptions
                {
                    StoreFilter = new VectorFilter().Where("tenant", "alpha")
                })
            .WithAgenticRagTracing(trace =>
            {
                captured = trace;
            });

        var tool = service.Functions.Single(f => f.Name == "search_documents");
        _ = await tool.Handler(new Dictionary<string, object>
        {
            ["query"] = "refund policy"
        });

        Assert.IsNotNull(captured, "Trace callback should be invoked.");
        Assert.AreEqual("search_documents", captured.ToolName);
        Assert.AreEqual("refund policy", captured.Query);
        Assert.IsTrue(captured.Succeeded);
        Assert.IsNotNull(captured.QueryOptions);
        Assert.IsNotNull(captured.Result);
        Assert.IsTrue(captured.HasReferences);
        Assert.IsTrue(captured.Result!.References.Count > 0);
        Assert.IsTrue(captured.Result.Diagnostics.FinalTopK > 0);
        Assert.IsTrue(captured.Result.Diagnostics.ElapsedMs >= 0);
        Assert.AreEqual("alpha", captured.Result.References[0].Record.Metadata["tenant"]);
    }

    [TestMethod]
    public async Task WithAgenticRagTracing_ReceivesFailuresFromQueryOptions()
    {
        var store = await CreateTaggedStoreAsync();
        var service = new MockAIService();
        AgenticRagSearchTrace? captured = null;

        service
            .WithAgenticRag(
                store,
                queryOptions: _ => throw new InvalidOperationException("permission lookup failed"))
            .WithAgenticRagTracing(trace =>
            {
                captured = trace;
            });

        var tool = service.Functions.Single(f => f.Name == "search_documents");
        var response = await tool.Handler(new Dictionary<string, object>
        {
            ["query"] = "refund policy"
        });

        Assert.AreEqual("Search failed: permission lookup failed", response);
        Assert.IsNotNull(captured, "Trace callback should run even when the search fails.");
        Assert.IsFalse(captured!.Succeeded);
        Assert.IsNull(captured.Result);
        Assert.IsNotNull(captured.Exception);
        Assert.AreEqual("permission lookup failed", captured.Exception!.Message);
    }

    private static async Task<RagStore> CreateTaggedStoreAsync()
    {
        var vectorStore = new InMemoryVectorStore();
        var pipeline = new RagPipeline(
            new LocalEmbeddingProvider(256),
            vectorStore,
            new CharacterTextSplitter(400, 0, separator: null),
            new DefaultContextBuilder(),
            options: new RagPipelineOptions
            {
                DefaultQuery = new RagQueryOptions
                {
                    FinalFilter = new RagFilter
                    {
                        TopK = 5
                    }
                }
            });

        await pipeline.IndexDocumentsAsync(new[]
        {
            new RagDocument
            {
                Id = "alpha-refund",
                Content = "Refund policy for tenant alpha. Alpha refunds are allowed within 14 days.",
                Source = "alpha-source",
                Metadata = new Dictionary<string, string>
                {
                    ["tenant"] = "alpha"
                }
            },
            new RagDocument
            {
                Id = "beta-refund",
                Content = "Refund policy for tenant beta. Beta refunds are allowed within 30 days.",
                Source = "beta-source",
                Metadata = new Dictionary<string, string>
                {
                    ["tenant"] = "beta"
                }
            }
        });

        return new RagStore(pipeline, vectorStore);
    }
}
