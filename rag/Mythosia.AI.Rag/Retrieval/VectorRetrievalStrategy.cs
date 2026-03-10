using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Retrieval
{
    /// <summary>
    /// Default retrieval strategy that wraps <see cref="IVectorStore.SearchAsync"/>.
    /// Provides backward compatibility with v3.x behavior (pure vector search).
    /// </summary>
    internal sealed class VectorRetrievalStrategy : IRetrievalStrategy
    {
        private readonly IVectorStore _vectorStore;

        public VectorRetrievalStrategy(IVectorStore vectorStore)
        {
            _vectorStore = vectorStore;
        }

        public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
            float[] denseVector,
            string query,
            int topK,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            // Pure vector search — ignores the text query
            return _vectorStore.SearchAsync(denseVector, topK, filter, cancellationToken);
        }
    }
}
