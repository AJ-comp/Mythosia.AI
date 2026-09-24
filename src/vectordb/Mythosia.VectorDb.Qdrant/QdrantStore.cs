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
    /// with payload-based metadata filtering for logical isolation.
    /// </summary>
    public class QdrantStore : IVectorStore, ITextSearchStore, IConfigurableHybridSearchStore, IDisposable
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

            var point = QdrantHelpers.ToPointStruct(record);
            await _client.UpsertAsync(_options.CollectionName, new[] { point }, cancellationToken: cancellationToken);
        }

        public async Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var points = records.Select(r => QdrantHelpers.ToPointStruct(r)).ToList();
            if (points.Count > 0)
                await _client.UpsertAsync(_options.CollectionName, points, cancellationToken: cancellationToken);
        }

        #endregion

        #region IVectorStore — Get / Delete

        public async Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var pointId = CreatePointId(id);
            var points = await _client.RetrieveAsync(
                _options.CollectionName,
                new PointId[] { pointId },
                withPayload: true,
                withVectors: true,
                cancellationToken: cancellationToken);

            if (points.Count == 0)
                return null;

            var record = QdrantHelpers.ToVectorRecord(points[0]);

            if (filter != null && !MatchesFilter(record, filter))
                return null;

            return record;
        }

        private static bool MatchesFilter(VectorRecord record, VectorFilter filter)
        {
            if (filter.Conditions.Count > 0 && !EvaluateConditions(record, filter.Conditions, FilterLogic.And))
                return false;

            return true;
        }

        private static bool EvaluateConditions(VectorRecord record, IReadOnlyList<FilterCondition> conditions, FilterLogic logic)
        {
            if (logic == FilterLogic.And)
            {
                foreach (var c in conditions)
                    if (!EvaluateCondition(record, c)) return false;
                return true;
            }
            else
            {
                foreach (var c in conditions)
                    if (EvaluateCondition(record, c)) return true;
                return false;
            }
        }

        private static bool EvaluateCondition(VectorRecord record, FilterCondition condition)
        {
            if (condition is MetadataCondition mc) return EvaluateMetadataCondition(record, mc);
            if (condition is FilterGroup group) return EvaluateConditions(record, group.Conditions, group.Logic);

            return true;
        }

        private static bool EvaluateMetadataCondition(VectorRecord record, MetadataCondition mc)
        {
            switch (mc.Operator)
            {
                case FilterOperator.Eq:
                    return record.Metadata.TryGetValue(mc.Key, out var eq) && string.Equals(eq, mc.Value, StringComparison.Ordinal);
                case FilterOperator.Ne:
                    return record.Metadata.TryGetValue(mc.Key, out var ne) && !string.Equals(ne, mc.Value, StringComparison.Ordinal);
                case FilterOperator.In:
                    return record.Metadata.TryGetValue(mc.Key, out var inV) && mc.Values != null && ContainsOrdinal(mc.Values, inV);
                case FilterOperator.NotIn:
                    return record.Metadata.TryGetValue(mc.Key, out var ninV) && (mc.Values == null || !ContainsOrdinal(mc.Values, ninV));
                case FilterOperator.Like:
                    return record.Metadata.TryGetValue(mc.Key, out var like) && LikeMatch(like, mc.Value ?? string.Empty);
                case FilterOperator.Gt:
                    return record.Metadata.TryGetValue(mc.Key, out var gt) && string.Compare(gt, mc.Value, StringComparison.Ordinal) > 0;
                case FilterOperator.Gte:
                    return record.Metadata.TryGetValue(mc.Key, out var gte) && string.Compare(gte, mc.Value, StringComparison.Ordinal) >= 0;
                case FilterOperator.Lt:
                    return record.Metadata.TryGetValue(mc.Key, out var lt) && string.Compare(lt, mc.Value, StringComparison.Ordinal) < 0;
                case FilterOperator.Lte:
                    return record.Metadata.TryGetValue(mc.Key, out var lte) && string.Compare(lte, mc.Value, StringComparison.Ordinal) <= 0;
                case FilterOperator.Exists:
                    return record.Metadata.ContainsKey(mc.Key);
                case FilterOperator.NotExists:
                    return !record.Metadata.ContainsKey(mc.Key);
                default:
                    return true;
            }
        }

        private static bool ContainsOrdinal(IReadOnlyList<string> values, string target)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], target, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool LikeMatch(string text, string pattern) => LikeMatchCore(text, pattern, 0, 0);

        private static bool LikeMatchCore(string text, string pattern, int t, int p)
        {
            while (t < text.Length && p < pattern.Length)
            {
                if (pattern[p] == '%')
                {
                    while (p < pattern.Length && pattern[p] == '%') p++;
                    if (p == pattern.Length) return true;
                    for (int i = t; i <= text.Length; i++)
                        if (LikeMatchCore(text, pattern, i, p)) return true;
                    return false;
                }
                if (pattern[p] == '_' || pattern[p] == text[t]) { t++; p++; }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '%') p++;
            return t == text.Length && p == pattern.Length;
        }

        public async Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            if (filter != null && filter.Conditions.Count > 0)
            {
                // conditions present: use filter-based delete so conditions are respected atomically
                var deleteFilter = BuildFilter(filter);
                deleteFilter.Must.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = QdrantHelpers.PayloadKeyId,
                        Match = new Match { Keyword = id }
                    }
                });
                await _client.DeleteAsync(_options.CollectionName, deleteFilter, cancellationToken: cancellationToken);
                return;
            }

            var pointId = CreatePointId(id);
            await _client.DeleteAsync(_options.CollectionName, new PointId[] { pointId }, cancellationToken: cancellationToken);
        }

        public async Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var qdrantFilter = BuildFilter(filter);
            await _client.DeleteAsync(_options.CollectionName, qdrantFilter, cancellationToken: cancellationToken);
        }

        #endregion

        #region IVectorStore — Get Batch

        public async Task<IReadOnlyList<VectorRecord>> GetBatchAsync(
            IEnumerable<string> ids,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var idList = ids.ToList();
            if (idList.Count == 0)
                return Array.Empty<VectorRecord>();

            var pointIds = idList.Select(id => CreatePointId(id)).ToArray();

            var points = await _client.RetrieveAsync(
                _options.CollectionName,
                pointIds,
                withPayload: true,
                withVectors: true,
                cancellationToken: cancellationToken);

            var results = new List<VectorRecord>(points.Count);
            foreach (var point in points)
            {
                var record = QdrantHelpers.ToVectorRecord(point);
                if (filter != null && !MatchesFilter(record, filter))
                    continue;

                results.Add(record);
            }

            return results;
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

        #region Independent Text Search and Configurable Hybrid Search

        /// <summary>
        /// Searches the named sparse index without requiring a dense query vector.
        /// Uses the same lexical analyzer as document ingestion.
        /// </summary>
        /// <remarks>
        /// Equality, set membership, and key-existence filters are supported. Other metadata
        /// operators are rejected before sending a request rather than silently omitted.
        /// </remarks>
        public async Task<IReadOnlyList<VectorSearchResult>> TextSearchAsync(
            string query,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK));
            cancellationToken.ThrowIfCancellationRequested();
            var queryFilter = BuildStrictSearchFilter(filter);
            var (indices, values) = BuildSparseVector(query);
            if (indices.Length == 0)
                return Array.Empty<VectorSearchResult>();

            await EnsureCollectionAsync(cancellationToken);
            var sparse = new SparseVector();
            sparse.Indices.AddRange(indices);
            sparse.Values.AddRange(values);
            var results = await QueryNamedVectorAsync(
                new VectorInput { Sparse = sparse }, QdrantOptions.SparseVectorName,
                queryFilter, topK, cancellationToken);
            return ApplyMinScoreFilter(results, filter);
        }

        /// <summary>
        /// Searches independent dense and sparse candidate lists and combines them with
        /// normalized weighted reciprocal-rank fusion using every supplied option.
        /// </summary>
        /// <remarks>
        /// This overload uses the portable weighted RRF contract. The legacy overload
        /// continues to use <see cref="QdrantOptions.HybridFusionStrategy"/> on the server.
        /// Minimum score applies to the final fused score, not individual candidate lists.
        /// </remarks>
        public async Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
            float[] denseVector,
            string query,
            HybridSearchOptions options,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var settings = options.Snapshot();
            int candidates = settings.GetCandidateCount(topK);
            if (settings.VectorWeight > 0 && (denseVector == null || denseVector.Length == 0))
                throw new ArgumentException("A dense vector is required when VectorWeight is greater than zero.", nameof(denseVector));
            if (settings.VectorWeight < 1 && query == null)
                throw new ArgumentNullException(nameof(query));
            cancellationToken.ThrowIfCancellationRequested();
            var queryFilter = BuildStrictSearchFilter(filter);

            Task<List<VectorSearchResult>> denseTask = Task.FromResult(new List<VectorSearchResult>());
            Task<List<VectorSearchResult>> sparseTask = Task.FromResult(new List<VectorSearchResult>());
            var (indices, values) = settings.VectorWeight < 1
                ? BuildSparseVector(query) : (Array.Empty<uint>(), Array.Empty<float>());
            if (settings.VectorWeight > 0 || indices.Length > 0)
                await EnsureCollectionAsync(cancellationToken);

            if (settings.VectorWeight > 0)
            {
                var dense = new DenseVector();
                dense.Data.AddRange(denseVector);
                denseTask = QueryNamedVectorAsync(new VectorInput { Dense = dense },
                    QdrantOptions.DenseVectorName, queryFilter, candidates, cancellationToken);
            }
            if (settings.VectorWeight < 1 && indices.Length > 0)
            {
                var sparse = new SparseVector();
                sparse.Indices.AddRange(indices);
                sparse.Values.AddRange(values);
                sparseTask = QueryNamedVectorAsync(new VectorInput { Sparse = sparse },
                    QdrantOptions.SparseVectorName, queryFilter, candidates, cancellationToken);
            }

            await Task.WhenAll(denseTask, sparseTask);
            cancellationToken.ThrowIfCancellationRequested();
            return HybridSearchFusion.Merge(await denseTask, await sparseTask, settings, topK, filter?.MinScore);
        }

        private async Task<List<VectorSearchResult>> QueryNamedVectorAsync(
            VectorInput vector,
            string vectorName,
            Filter filter,
            int topK,
            CancellationToken cancellationToken)
        {
            var points = await _client.QueryAsync(
                _options.CollectionName,
                query: new Query { Nearest = vector },
                usingVector: vectorName,
                filter: filter,
                limit: (ulong)topK,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: new WithVectorsSelector { Enable = true },
                cancellationToken: cancellationToken);
            return points.Select(point => new VectorSearchResult(
                QdrantHelpers.ToVectorRecord(point), point.Score)).ToList();
        }

        private static Filter BuildStrictSearchFilter(VectorFilter? filter)
        {
            var result = new Filter();
            if (filter != null)
            {
                foreach (var condition in filter.Conditions)
                    result.Must.Add(BuildStrictSearchCondition(condition));
            }
            // The schema marker is infrastructure, never a search document.
            result.MustNot.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = QdrantHelpers.PayloadKeyId,
                    Match = new Match { Keyword = QdrantHelpers.SchemaMarkerId }
                }
            });
            return result;
        }

        private static Condition BuildStrictSearchCondition(FilterCondition condition)
        {
            if (condition is FilterGroup group)
            {
                var nested = new Filter();
                foreach (var child in group.Conditions)
                {
                    var translated = BuildStrictSearchCondition(child);
                    if (group.Logic == FilterLogic.And) nested.Must.Add(translated);
                    else nested.Should.Add(translated);
                }
                return new Condition { Filter = nested };
            }
            if (!(condition is MetadataCondition metadata))
                throw new NotSupportedException("The metadata filter condition is not supported by Qdrant search.");

            string key = QdrantHelpers.QuotePayloadKey(QdrantHelpers.PayloadMetadataPrefix + metadata.Key);
            var empty = new Condition { IsEmpty = new IsEmptyCondition { Key = key } };
            if (metadata.Operator == FilterOperator.NotExists)
                return empty;
            if (metadata.Operator == FilterOperator.Exists)
            {
                var exists = new Filter();
                exists.MustNot.Add(empty);
                return new Condition { Filter = exists };
            }
            Condition match;
            switch (metadata.Operator)
            {
                case FilterOperator.Eq:
                case FilterOperator.Ne:
                    match = new Condition
                    {
                        Field = new FieldCondition { Key = key, Match = new Match { Keyword = metadata.Value! } }
                    };
                    break;
                case FilterOperator.In:
                case FilterOperator.NotIn:
                    var any = new Filter();
                    foreach (var value in metadata.Values!)
                        any.Should.Add(new Condition
                        {
                            Field = new FieldCondition { Key = key, Match = new Match { Keyword = value } }
                        });
                    // An empty IN set must match nothing; an empty Qdrant filter matches everything.
                    if (metadata.Values!.Count == 0)
                    {
                        any.Must.Add(empty);
                        any.MustNot.Add(empty);
                    }
                    match = new Condition { Filter = any };
                    break;
                default:
                    throw new NotSupportedException(
                        $"Metadata operator '{metadata.Operator}' cannot be represented by Qdrant search with string metadata.");
            }
            if (metadata.Operator == FilterOperator.Ne || metadata.Operator == FilterOperator.NotIn)
            {
                var negated = new Filter();
                negated.MustNot.Add(empty);
                negated.MustNot.Add(match);
                return new Condition { Filter = negated };
            }
            return match;
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
            var result = new Filter();

            if (filter?.Conditions != null && filter.Conditions.Count > 0)
                AppendConditionsToFilter(result, filter.Conditions, FilterLogic.And);

            return result;
        }

        /// <summary>
        /// Recursively translates <see cref="FilterCondition"/> tree into Qdrant filter predicates.
        /// Supported natively: Eq, Ne, In, NotIn, And/Or groups.
        /// Unsupported operators (Gt, Gte, Lt, Lte, Like, Exists, NotExists) are silently skipped
        /// at the Qdrant level — <see cref="MatchesFilter"/> handles them for GetAsync/GetBatchAsync.
        /// </summary>
        private static void AppendConditionsToFilter(Filter filter, IReadOnlyList<FilterCondition> conditions, FilterLogic logic)
        {
            foreach (var condition in conditions)
            {
                if (condition is MetadataCondition mc)
                {
                    // Keep the legacy operator set while sharing literal-key and missing-key semantics.
                    if (mc.Operator == FilterOperator.Eq || mc.Operator == FilterOperator.Ne ||
                        mc.Operator == FilterOperator.In || mc.Operator == FilterOperator.NotIn)
                    {
                        var translated = BuildStrictSearchCondition(mc);
                        if (logic == FilterLogic.And) filter.Must.Add(translated);
                        else filter.Should.Add(translated);
                    }
                }
                else if (condition is FilterGroup group)
                {
                    var nested = new Filter();
                    AppendConditionsToFilter(nested, group.Conditions, group.Logic);
                    var cond = new Condition { Filter = nested };
                    if (logic == FilterLogic.And) filter.Must.Add(cond);
                    else filter.Should.Add(cond);
                }
            }
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

        #region IVectorStore — Count

        public async Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var qdrantFilter = BuildCountFilter(filter);
            var count = await _client.CountAsync(_options.CollectionName, qdrantFilter, cancellationToken: cancellationToken);
            return (long)count;
        }

        /// <summary>
        /// Builds a count filter that applies user-specified conditions while always excluding
        /// the internal schema marker point (<see cref="QdrantHelpers.SchemaMarkerId"/>).
        /// </summary>
        private static Filter BuildCountFilter(VectorFilter? filter)
        {
            var result = BuildFilter(filter);

            result.MustNot.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = QdrantHelpers.PayloadKeyId,
                    Match = new Match { Keyword = QdrantHelpers.SchemaMarkerId }
                }
            });

            return result;
        }

        #endregion

        #region Connection Verification

        public async Task VerifyConnectionAsync(CancellationToken cancellationToken = default)
        {
            await _client.ListCollectionsAsync(cancellationToken);
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
