using Qdrant.Client;
using Qdrant.Client.Grpc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

        // Reserved payload keys
        private const string PayloadKeyId = "_id";
        private const string PayloadKeyContent = "_content";
        private const string PayloadKeyNamespace = "_namespace";
        private const string PayloadKeyScope = "_scope";
        private const string PayloadMetadataPrefix = "meta.";

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

            var point = ToPointStruct(record);
            await _client.UpsertAsync(_options.CollectionName, new[] { point }, cancellationToken: cancellationToken);
        }

        public async Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var points = records.Select(r => ToPointStruct(r)).ToList();
            if (points.Count > 0)
                await _client.UpsertAsync(_options.CollectionName, points, cancellationToken: cancellationToken);
        }

        #endregion

        #region IVectorStore — Get / Delete

        public async Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var ns = filter?.Namespace;
            var pointId = StringToPointId(ns, id);
            var points = await _client.RetrieveAsync(
                _options.CollectionName,
                new PointId[] { pointId },
                withPayload: true,
                withVectors: true,
                cancellationToken: cancellationToken);

            if (points.Count == 0)
                return null;

            var point = points[0];
            if (ns != null && !HasNamespace(point.Payload, ns))
                return null;

            return ToVectorRecord(point);
        }

        public async Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            await EnsureCollectionAsync(cancellationToken);

            var ns = filter?.Namespace;
            var pointId = StringToPointId(ns, id);
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
                filter: qdrantFilter,
                limit: (ulong)topK,
                scoreThreshold: scoreThreshold,
                payloadSelector: true,
                vectorsSelector: true,
                cancellationToken: cancellationToken);

            var searchResults = new List<VectorSearchResult>(results.Count);
            foreach (var scored in results)
            {
                var record = ToVectorRecord(scored);
                searchResults.Add(new VectorSearchResult(record, scored.Score));
            }

            return searchResults;
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

                    await _client.CreateCollectionAsync(
                        _options.CollectionName,
                        new VectorParams
                        {
                            Size = (ulong)_options.Dimension,
                            Distance = MapDistance(_options.DistanceStrategy)
                        },
                        cancellationToken: cancellationToken);
                }

                await TryCreatePayloadIndexesAsync(cancellationToken);

                _collectionEnsured = true;
            }
            finally
            {
                _collectionLock.Release();
            }
        }

        /// <summary>
        /// Creates payload indexes required for filtering.
        /// Reserved fields (<c>_namespace</c>, <c>_scope</c>) are always included,
        /// and user-defined indexes are taken from <see cref="QdrantOptions.AdditionalPayloadIndexes"/>.
        /// Failures are silently ignored (indexes may already exist).
        /// </summary>
        private async Task TryCreatePayloadIndexesAsync(CancellationToken cancellationToken)
        {
            var indexedFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var indexOption in _options.GetAllPayloadIndexes())
            {
                if (!indexedFields.Add(indexOption.Field))
                    continue;

                try
                {
                    await _client.CreatePayloadIndexAsync(
                        _options.CollectionName,
                        indexOption.Field,
                        indexOption.SchemaType,
                        cancellationToken: cancellationToken);
                }
                catch
                {
                    // Index may already exist; safe to ignore.
                }
            }
        }

        #endregion

        #region Private Helpers — Mapping

        private static PointStruct ToPointStruct(VectorRecord record)
        {
            var point = new PointStruct
            {
                Id = StringToPointId(record.Namespace, record.Id),
                Vectors = record.Vector,
            };

            point.Payload[PayloadKeyId] = record.Id;
            point.Payload[PayloadKeyContent] = record.Content ?? string.Empty;

            if (record.Namespace != null)
                point.Payload[PayloadKeyNamespace] = record.Namespace;

            if (record.Scope != null)
                point.Payload[PayloadKeyScope] = record.Scope;

            if (record.Metadata != null)
            {
                foreach (var kvp in record.Metadata)
                {
                    point.Payload[$"{PayloadMetadataPrefix}{kvp.Key}"] = kvp.Value;
                }
            }

            return point;
        }

        private static VectorRecord ToVectorRecord(RetrievedPoint point)
        {
            return new VectorRecord
            {
                Id = point.Payload.TryGetValue(PayloadKeyId, out var idVal)
                    ? idVal.StringValue : string.Empty,
                Content = point.Payload.TryGetValue(PayloadKeyContent, out var contentVal)
                    ? contentVal.StringValue : string.Empty,
                Namespace = point.Payload.TryGetValue(PayloadKeyNamespace, out var nsVal)
                    ? nsVal.StringValue : null,
                Scope = point.Payload.TryGetValue(PayloadKeyScope, out var scopeVal)
                    ? scopeVal.StringValue : null,
                Vector = ExtractVector(point.Vectors),
                Metadata = ExtractMetadata(point.Payload)
            };
        }

        private static VectorRecord ToVectorRecord(ScoredPoint point)
        {
            return new VectorRecord
            {
                Id = point.Payload.TryGetValue(PayloadKeyId, out var idVal)
                    ? idVal.StringValue : string.Empty,
                Content = point.Payload.TryGetValue(PayloadKeyContent, out var contentVal)
                    ? contentVal.StringValue : string.Empty,
                Namespace = point.Payload.TryGetValue(PayloadKeyNamespace, out var nsVal)
                    ? nsVal.StringValue : null,
                Scope = point.Payload.TryGetValue(PayloadKeyScope, out var scopeVal)
                    ? scopeVal.StringValue : null,
                Vector = ExtractVector(point.Vectors),
                Metadata = ExtractMetadata(point.Payload)
            };
        }

        private static float[] ExtractVector(VectorsOutput? vectors)
        {
            var dense = vectors?.Vector?.GetDenseVector();
            if (dense?.Data == null)
                return Array.Empty<float>();

            return dense.Data.ToArray();
        }

        private static Dictionary<string, string> ExtractMetadata(
            Google.Protobuf.Collections.MapField<string, Value> payload)
        {
            var metadata = new Dictionary<string, string>();
            foreach (var kvp in payload)
            {
                if (kvp.Key.StartsWith(PayloadMetadataPrefix, StringComparison.Ordinal))
                {
                    var metaKey = kvp.Key.Substring(PayloadMetadataPrefix.Length);
                    metadata[metaKey] = kvp.Value.StringValue;
                }
            }
            return metadata;
        }

        private static bool HasNamespace(
            Google.Protobuf.Collections.MapField<string, Value> payload, string @namespace)
        {
            return payload.TryGetValue(PayloadKeyNamespace, out var nsVal)
                && nsVal.StringValue == @namespace;
        }

        /// <summary>
        /// Creates a deterministic <see cref="PointId"/> (UUID) from namespace + record Id.
        /// When namespace is provided, it is included so that the same record Id in different
        /// namespaces produces distinct point IDs within the single shared collection.
        /// </summary>
        private static PointId StringToPointId(string? @namespace, string id)
        {
            var input = @namespace != null ? $"{@namespace}\0{id}" : id;
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                return new Guid(hash);
            }
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
                        Key = PayloadKeyNamespace,
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
                        Key = PayloadKeyScope,
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
                            Key = $"{PayloadMetadataPrefix}{kvp.Key}",
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
