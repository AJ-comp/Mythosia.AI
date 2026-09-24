using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public class RetrievalIsolationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Query_DoesNotMutateCallerFilter_WithOrWithoutGlobalFilter(bool useGlobalFilter)
    {
        using var store = new InMemoryVectorStore();
        var pipeline = new RagPipeline(new LocalEmbeddingProvider(16), store,
            new CharacterTextSplitter(), new DefaultContextBuilder());
        var retriever = new RecordingRetriever();
        pipeline.SetRetriever(retriever);
        var callerFilter = new VectorFilter().Where("tenant", "mine").WithMinScore(.95);
        var options = new RagQueryOptions { StoreFilter = useGlobalFilter ? new VectorFilter().Where("category", "manual") : null };
        options.FinalFilter.MinScore = .4;

        await pipeline.QueryAsync("question", options, callerFilter);

        Assert.AreEqual(.95, callerFilter.MinScore);
        Assert.AreEqual(1, callerFilter.Conditions.Count);
        Assert.IsNotNull(retriever.Request);
        Assert.AreEqual(.4, retriever.Request.Filter!.MinScore);
        Assert.AreEqual(useGlobalFilter ? 2 : 1, retriever.Request.Filter.Conditions.Count);
        retriever.Request.Filter.Where("new", "condition");
        Assert.AreEqual(1, callerFilter.Conditions.Count);
    }

    [TestMethod]
    public async Task CancellationDuringFilteringProgress_DoesNotDispatchCustomRetriever()
    {
        using var cancellation = new CancellationTokenSource();
        using var store = new InMemoryVectorStore();
        var pipeline = new RagPipeline(new LocalEmbeddingProvider(16), store,
            new CharacterTextSplitter(), new DefaultContextBuilder());
        var retriever = new RecordingRetriever();
        pipeline.SetRetriever(retriever);
        var options = new RagQueryOptions
        {
            ProgressAsync = stage =>
            {
                if (stage == RagProgressStage.Filtering) cancellation.Cancel();
                return Task.CompletedTask;
            }
        };

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            pipeline.QueryAsync("question", options, cancellationToken: cancellation.Token));
        Assert.IsNull(retriever.Request);
    }

    private sealed class RecordingRetriever : IRagRetriever
    {
        public RagRetrievalRequest? Request;
        public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(RagRetrievalRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(Array.Empty<VectorSearchResult>());
        }
    }
}
