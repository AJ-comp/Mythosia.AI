using Qdrant.Client;
using Qdrant.Client.Grpc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Mythosia.VectorDb.Qdrant.QdrantHelpers;

namespace Mythosia.VectorDb.Qdrant
{
    /// <summary>
    /// Qdrant implementation of <see cref="IVectorStore"/>.
    /// Uses a single Qdrant collection (configured via <see cref="QdrantOptions.CollectionName"/>)
    /// with payload-based logical isolation:
    /// <list type="bullet">
    ///   <item><c>_namespace</c> — first-tier logical partition (maps to <see cref="VectorRecord.Namespace"/>)</item>
    ///   <item><c>_scope</c> — second-tier logical partition (maps to <see cref="VectorRecord.Scope"/>)</item>
    /// </list>
    /// </summary>
    public class QdrantStore : IVectorStore, IDisposable
    {
        private readonly QdrantOptions _options;
        private readonly QdrantClient _client;
        private readonly bool _ownsClient;
        private readonly SemaphoreSlim _collectionLock = new SemaphoreSlim(1, 1);
        private volatile bool _collectionEnsured;

        /// <summary>
        /// Creates a new <see cref="QdrantStore"/> that owns its <see cref="QdrantClient"/>.
        /// </summary>
        /// <param name="options">Configuration options. Validated on construction.</param>
        public QdrantStore(QdrantOptions options)
        {
            options.Validate();
            _options = options;
            _client = new QdrantClient(options.Host, options.Port, options.UseTls, options.ApiKey);
            _ownsClient = true;
        }

        /// <summary>
        /// Creates a new <see cref="QdrantStore"/> using an externally managed <see cref="QdrantClient"/>.
        /// The caller is responsible for disposing the client.
        /// </summary>
        /// <param name="options">Configuration options. Validated on construction.</param>
        /// <param name="client">Pre-configured Qdrant client instance.</param>
        public QdrantStore(QdrantOptions options, QdrantClient client)
        {
            options.Validate();
            _options = options;
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _ownsClient = false;
        }

        #region IVectorStore — Upsert

        public async Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var point = QdrantHelpers.ToPointStruct(record, includeSparseVector: true);
            await _client.UpsertAsync(_options.CollectionName, new[] { point }, cancellationToken: cancellationToken);
        }

        public async Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var points = records.Select(r => QdrantHelpers.ToPointStruct(r, includeSparseVector: true)).ToList();
            if (points.Count > 0)
                await _client.UpsertAsync(_options.CollectionName, points, cancellationToken: cancellationToken);
        }

        #endregion

        #region IVectorStore — Get / Delete

        public async Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var ns = filter?.Namespace;
            var pointId = CreatePointId(ns, id);
            var points = await _client.RetrieveAsync(
                _options.CollectionName,
                new PointId[] { pointId },
                withPayload: true,
                withVectors: true,
                cancellationToken: cancellationToken);

            if (points.Count == 0)
                return null;

            var point = points[0];
            if (ns != null && !QdrantHelpers.HasNamespace(point.Payload, ns))
                return null;

            return QdrantHelpers.ToVectorRecord(point);
        }

        public async Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var ns = filter?.Namespace;
            var pointId = CreatePointId(ns, id);
            await _client.DeleteAsync(_options.CollectionName, new PointId[] { pointId }, cancellationToken: cancellationToken);
        }

        public async Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var qdrantFilter = BuildFilter(filter);
            await _client.DeleteAsync(_options.CollectionName, qdrantFilter, cancellationToken: cancellationToken);
        }

        #endregion

        #region IVectorStore — Search

        public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var qdrantFilter = BuildFilter(filter);
            var scoreThreshold = filter?.MinScore.HasValue == true
                ? (float)filter.MinScore.Value
                : (float?)null;

            var results = await _client.SearchAsync(
                _options.CollectionName,
                queryVector,
                vectorName: QdrantOptions.DenseVectorName,
                filter: qdrantFilter,
                limit: (ulong)topK,
                scoreThreshold: scoreThreshold,
                payloadSelector: true,
                vectorsSelector: true,
                cancellationToken: cancellationToken);

            var searchResults = new List<VectorSearchResult>(results.Count);
            foreach (var scored in results)
            {
                var rec = QdrantHelpers.ToVectorRecord(scored);
                searchResults.Add(new VectorSearchResult(rec, scored.Score));
            }

            return ApplyMinScoreFilter(searchResults, filter);
        }

        #endregion

        #region IVectorStore — Hybrid Search

        /// <summary>
        /// Performs a native hybrid search using Qdrant's prefetch + server-side fusion.
        /// Dense vector search and sparse (BM25-based) vector search are combined using
        /// <see cref="QdrantOptions.HybridFusionStrategy"/>.
        /// </summary>
        public async Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
            float[] denseVector,
            string query,
            int topK,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var qdrantFilter = BuildFilter(filter);

            // Build sparse vector from query using BM25 tokenizer
            var (sparseIndices, sparseValues) = BuildSparseVector(query);

            // Dense vector prefetch
            var denseVec = new DenseVector();
            denseVec.Data.AddRange(denseVector);
            var densePrefetch = new PrefetchQuery
            {
                Query = new Query { Nearest = new VectorInput { Dense = denseVec } },
                Using = QdrantOptions.DenseVectorName,
                Limit = (ulong)(topK * 2),
                Filter = qdrantFilter
            };

            // Sparse vector prefetch
            var sparseVec = new SparseVector();
            sparseVec.Indices.AddRange(sparseIndices);
            sparseVec.Values.AddRange(sparseValues);
            var sparsePrefetch = new PrefetchQuery
            {
                Query = new Query { Nearest = new VectorInput { Sparse = sparseVec } },
                Using = QdrantOptions.SparseVectorName,
                Limit = (ulong)(topK * 2),
                Filter = qdrantFilter
            };

            var prefetches = new List<PrefetchQuery> { densePrefetch, sparsePrefetch };

            var results = await _client.QueryAsync(
                _options.CollectionName,
                query: new Query { Fusion = MapFusion(_options.HybridFusionStrategy) },
                prefetch: prefetches,
                limit: (ulong)topK,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: new WithVectorsSelector { Enable = true },
                cancellationToken: cancellationToken);

            var searchResults = new List<VectorSearchResult>(results.Count);
            foreach (var scored in results)
            {
                var rec = QdrantHelpers.ToVectorRecord(scored);
                searchResults.Add(new VectorSearchResult(rec, scored.Score));
            }

            return ApplyMinScoreFilter(searchResults, filter);
        }

        #endregion

        #region Private Helpers — Search Scoring

        private static Fusion MapFusion(QdrantHybridFusionStrategy strategy)
        {
            return strategy switch
            {
                QdrantHybridFusionStrategy.Rrf => Fusion.Rrf,
                QdrantHybridFusionStrategy.Dbsf => Fusion.Dbsf,
                _ => throw new InvalidOperationException($"Unsupported hybrid fusion strategy: {strategy}")
            };
        }

        private static IReadOnlyList<VectorSearchResult> ApplyMinScoreFilter(
            List<VectorSearchResult> results,
            VectorFilter? filter)
        {
            if (filter == null || !filter.MinScore.HasValue)
                return results;

            var minScore = filter.MinScore.Value;

            return results
                .Where(r => r.Score >= minScore)
                .ToList();
        }

        #endregion

        #region Private Helpers — Collection Management

        private async Task EnsureCollectionAsync(CancellationToken cancellationToken)
        {
            if (_collectionEnsured)
                return;

            await _collectionLock.WaitAsync(cancellationToken);
            try
            {
                if (_collectionEnsured)
                    return;

                if (!await _client.CollectionExistsAsync(_options.CollectionName, cancellationToken))
                {
                    if (!_options.AutoCreateCollection)
                        throw new InvalidOperationException(
                            $"Collection \"{_options.CollectionName}\" does not exist. " +
                            $"Create the collection manually or set AutoCreateCollection = true.");

                    // Always create collection with both dense and sparse vector params.
                    var denseConfig = new VectorParamsMap();
                    denseConfig.Map.Add(
                        QdrantOptions.DenseVectorName,
                        new VectorParams
                        {
                            Size = (ulong)_options.Dimension,
                            Distance = MapDistance(_options.DistanceStrategy)
                        });

                    var sparseConfig = new SparseVectorConfig();
                    sparseConfig.Map.Add(QdrantOptions.SparseVectorName, new SparseVectorParams());

                    await _client.CreateCollectionAsync(
                        _options.CollectionName,
                        denseConfig,
                        sparseVectorsConfig: sparseConfig,
                        cancellationToken: cancellationToken);

                    await WriteSchemaMarkerAsync(cancellationToken);
                }

                await QdrantHelpers.CreatePayloadIndexesAsync(_client, _options.CollectionName, _options, cancellationToken);

                _collectionEnsured = true;
            }
            finally
            {
                _collectionLock.Release();
            }
        }

        private async Task WriteSchemaMarkerAsync(CancellationToken cancellationToken)
        {
            var marker = QdrantHelpers.CreateSchemaMarkerPoint(_options.Dimension);
            await _client.UpsertAsync(_options.CollectionName, new[] { marker }, cancellationToken: cancellationToken);
        }

        #endregion

        #region Private Helpers — Filtering

        private static Filter BuildFilter(VectorFilter? filter)
        {
            var conditions = new List<Condition>();

            if (filter?.Namespace != null)
            {
                conditions.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = QdrantHelpers.PayloadKeyNamespace,
                        Match = new Match { Keyword = filter.Namespace }
                    }
                });
            }

            if (filter?.Scope != null)
            {
                conditions.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = QdrantHelpers.PayloadKeyScope,
                        Match = new Match { Keyword = filter.Scope }
                    }
                });
            }

            if (filter?.MetadataMatch != null)
            {
                foreach (var kvp in filter.MetadataMatch)
                {
                    conditions.Add(new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = $"{QdrantHelpers.PayloadMetadataPrefix}{kvp.Key}",
                            Match = new Match { Keyword = kvp.Value }
                        }
                    });
                }
            }

            if (conditions.Count == 0)
                return new Filter();

            var result = new Filter();
            result.Must.AddRange(conditions);

            return result;
        }

        #endregion

        #region Private Helpers — Distance

        private static Distance MapDistance(QdrantDistanceStrategy strategy)
        {
            return strategy switch
            {
                QdrantDistanceStrategy.Cosine => Distance.Cosine,
                QdrantDistanceStrategy.Euclidean => Distance.Euclid,
                QdrantDistanceStrategy.DotProduct => Distance.Dot,
                _ => throw new InvalidOperationException($"Unsupported distance strategy: {strategy}")
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _collectionLock.Dispose();

            if (_ownsClient)
                _client.Dispose();
        }

        #endregion
    }
}
