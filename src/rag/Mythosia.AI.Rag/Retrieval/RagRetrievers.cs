using Mythosia.VectorDb;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Retrieval
{
    internal static class RagRetrievers
    {
        private static async Task<float[]> GetOwnedQueryEmbeddingAsync(
            IEmbeddingProvider provider, string query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dimensions = provider.Dimensions;
            if (dimensions <= 0)
                throw new InvalidOperationException("The embedding provider must declare a positive Dimensions value.");

            var vector = await provider.GetEmbeddingAsync(query, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (vector == null || vector.Length != dimensions)
                throw new InvalidOperationException(
                    $"The query embedding must have {dimensions} dimensions; "
                    + (vector == null ? "the vector was null." : $"received {vector.Length}."));

            // Own the completed result before another callback or asynchronous operation
            // can request an embedding from a provider that reuses its returned buffers.
            var ownedVector = (float[])vector.Clone();
            for (var i = 0; i < ownedVector.Length; i++)
                if (float.IsNaN(ownedVector[i]) || float.IsInfinity(ownedVector[i]))
                    throw new InvalidOperationException("The query embedding contains a non-finite value.");
            cancellationToken.ThrowIfCancellationRequested();
            return ownedVector;
        }

        internal static async Task ReportAsync(RagRetrievalRequest request, RagProgressStage stage,
            CancellationToken cancellationToken)
        {
            if (request.ProgressAsync != null) await request.ProgressAsync(stage);
            cancellationToken.ThrowIfCancellationRequested();
        }

        internal sealed class Vector : IRagRetriever
        {
            private readonly IEmbeddingProvider _embeddings;
            private readonly IVectorStore _store;
            internal Vector(IEmbeddingProvider embeddings, IVectorStore store)
            { _embeddings = embeddings; _store = store; }

            public async Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
                RagRetrievalRequest request, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReportAsync(request, RagProgressStage.Embedding, cancellationToken);
                var vector = await GetOwnedQueryEmbeddingAsync(_embeddings, request.Query, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await ReportAsync(request, RagProgressStage.Retrieval, cancellationToken);
                return await _store.SearchAsync(vector, request.TopK, request.Filter, cancellationToken);
            }
        }

        internal sealed class Keyword : IRagRetriever
        {
            private readonly ITextSearchStore _store;
            internal Keyword(IVectorStore store)
            {
                _store = store as ITextSearchStore ?? throw new NotSupportedException(
                    $"Store {store.GetType().Name} does not support keyword-only search. " +
                    "Use a store implementing ITextSearchStore or provide an IRagRetriever.");
            }
            public async Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
                RagRetrievalRequest request, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = request.TextQuery ?? request.Query;
                if (string.IsNullOrWhiteSpace(text)) return Array.Empty<VectorSearchResult>();
                await ReportAsync(request, RagProgressStage.Retrieval, cancellationToken);
                return await _store.TextSearchAsync(text, request.TopK, request.Filter, cancellationToken);
            }
        }

        internal sealed class Hybrid : IRagRetriever
        {
            private readonly IEmbeddingProvider _embeddings;
            private readonly IVectorStore _store;
            private readonly HybridSearchOptions _options;
            private readonly bool _allowLegacyDefaults;
            internal Hybrid(IEmbeddingProvider embeddings, IVectorStore store,
                HybridSearchOptions options, bool allowLegacyDefaults = false)
            {
                _embeddings = embeddings;
                _store = store;
                _options = options.Snapshot();
                _allowLegacyDefaults = allowLegacyDefaults && _options.VectorWeight == .5f
                    && _options.CandidateMultiplier == 2 && _options.RrfK == 60;
                if (_options.VectorWeight == 0 && !(store is ITextSearchStore)
                    && !(store is IConfigurableHybridSearchStore))
                    throw new NotSupportedException($"Store {store.GetType().Name} does not support keyword-only search.");
                if (_options.VectorWeight > 0 && _options.VectorWeight < 1
                    && !(store is IConfigurableHybridSearchStore) && !(store is ITextSearchStore)
                    && !_allowLegacyDefaults)
                    throw new NotSupportedException(
                        $"Store {store.GetType().Name} does not support configurable hybrid search. " +
                        "It must implement IConfigurableHybridSearchStore or ITextSearchStore; settings cannot be ignored.");
            }

            public async Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
                RagRetrievalRequest request, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidateCount = _options.GetCandidateCount(request.TopK);
                var text = request.TextQuery ?? request.Query;
                bool useVector = _options.VectorWeight > 0;
                bool useText = _options.VectorWeight < 1 && !string.IsNullOrWhiteSpace(text);
                if (!useVector && !useText) return Array.Empty<VectorSearchResult>();

                // Select active legs before asking the embedding service to do work.
                var vector = Array.Empty<float>();
                if (useVector)
                {
                    await ReportAsync(request, RagProgressStage.Embedding, cancellationToken);
                    vector = await GetOwnedQueryEmbeddingAsync(_embeddings, request.Query, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                await ReportAsync(request, RagProgressStage.Retrieval, cancellationToken);
                // Native configurable stores own all endpoint semantics too, including
                // their filter validation. Never bypass those checks at weight zero/one.
                if (_store is IConfigurableHybridSearchStore configurable)
                    return await configurable.HybridSearchAsync(vector, text, _options.Snapshot(),
                        request.TopK, request.Filter, cancellationToken);

                if (!useVector || !useText || _store is ITextSearchStore)
                {
                    // Even a single active leg keeps hybrid rank-score semantics. Minimum
                    // score applies after fusion, never to raw cosine/BM25 candidates.
                    var candidateFilter = new VectorFilter();
                    if (request.Filter != null) candidateFilter.AppendConditionsFrom(request.Filter);
                    var vectors = useVector
                        ? await _store.SearchAsync(vector, candidateCount, candidateFilter, cancellationToken)
                        : Array.Empty<VectorSearchResult>();
                    cancellationToken.ThrowIfCancellationRequested();
                    var words = useText
                        ? await ((ITextSearchStore)_store).TextSearchAsync(text, candidateCount, candidateFilter, cancellationToken)
                        : Array.Empty<VectorSearchResult>();
                    cancellationToken.ThrowIfCancellationRequested();
                    return HybridSearchFusion.Merge(vectors, words, _options, request.TopK, request.Filter?.MinScore);
                }

                // Existing third-party/Pinecone default native search remains usable.
                // Do not catch NotSupportedException and silently substitute vector-only search.
                return await _store.HybridSearchAsync(vector, text, request.TopK, request.Filter, cancellationToken);
            }
        }

        internal sealed class Legacy : IRagRetriever
        {
            private readonly IRetrievalStrategy _strategy;
            private readonly IEmbeddingProvider _embeddings;
            internal Legacy(IRetrievalStrategy strategy, IEmbeddingProvider embeddings)
            { _strategy = strategy; _embeddings = embeddings; }
            public async Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
                RagRetrievalRequest request, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReportAsync(request, RagProgressStage.Embedding, cancellationToken);
                var vector = await GetOwnedQueryEmbeddingAsync(_embeddings, request.Query, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await ReportAsync(request, RagProgressStage.Retrieval, cancellationToken);
                return await _strategy.RetrieveAsync(vector, request.TextQuery, request.TopK,
                    request.Filter, cancellationToken);
            }
        }
    }
}
