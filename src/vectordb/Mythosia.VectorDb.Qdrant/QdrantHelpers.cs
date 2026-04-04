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
    /// Shared constants and helper methods used by both <see cref="QdrantStore"/>
    /// and <see cref="QdrantVectorStoreMigrator"/>.
    /// </summary>
    internal static class QdrantHelpers
    {
        #region Payload Key Constants

        internal const string PayloadKeyId = "_id";
        internal const string PayloadKeyContent = "_content";
        internal const string PayloadMetadataPrefix = "meta.";

        #endregion

        #region Schema Versioning Constants

        internal const string SchemaMarkerId = "__mythosia_schema__";
        internal const string PayloadKeySchemaVersion = "_schema_version";
        internal const string PayloadKeySchemaKind = "_schema_kind";
        internal const int CurrentSchemaVersion = 2;
        internal const string CurrentSchemaKind = "hybrid";

        #endregion

        #region Sparse Vector

        /// <summary>
        /// Builds a sparse vector from text using BM25 tokenizer.
        /// Token hash -> index, term frequency -> value.
        /// </summary>
        internal static (uint[] indices, float[] values) BuildSparseVector(string text)
        {
            var tf = Bm25Tokenizer.Analyze(text).TermFrequencies;

            var indices = new uint[tf.Count];
            var values = new float[tf.Count];
            int i = 0;

            foreach (var kvp in tf)
            {
                indices[i] = StableHash(kvp.Key);
                values[i] = kvp.Value;
                i++;
            }

            return (indices, values);
        }

        /// <summary>
        /// Deterministic hash via <see cref="System.IO.Hashing.XxHash32"/>.
        /// Unlike <see cref="string.GetHashCode"/>, this produces the same value
        /// across process restarts and .NET runtime versions — critical for
        /// sparse vector indices persisted in Qdrant.
        /// </summary>
        internal static uint StableHash(string term)
        {
            var bytes = Encoding.UTF8.GetBytes(term);
            return System.IO.Hashing.XxHash32.HashToUInt32(bytes);
        }

        #endregion

        #region Point ID

        /// <summary>
        /// Creates a deterministic <see cref="PointId"/> (UUID) from record Id.
        /// </summary>
        internal static PointId CreatePointId(string id)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(id));
                return new Guid(hash);
            }
        }

        #endregion

        #region Mapping — VectorRecord <-> PointStruct

        internal static VectorRecord ToVectorRecord(RetrievedPoint point)
        {
            var metadata = ExtractMetadata(point.Payload);

            return new VectorRecord
            {
                Id = point.Payload.TryGetValue(PayloadKeyId, out var idVal)
                    ? idVal.StringValue : string.Empty,
                Content = point.Payload.TryGetValue(PayloadKeyContent, out var contentVal)
                    ? contentVal.StringValue : string.Empty,
                Vector = ExtractDenseVector(point.Vectors),
                Metadata = metadata
            };
        }

        internal static VectorRecord ToVectorRecord(ScoredPoint point)
        {
            var metadata = ExtractMetadata(point.Payload);

            return new VectorRecord
            {
                Id = point.Payload.TryGetValue(PayloadKeyId, out var idVal)
                    ? idVal.StringValue : string.Empty,
                Content = point.Payload.TryGetValue(PayloadKeyContent, out var contentVal)
                    ? contentVal.StringValue : string.Empty,
                Vector = ExtractDenseVector(point.Vectors),
                Metadata = metadata
            };
        }

        /// <summary>
        /// Converts a <see cref="VectorRecord"/> to a Qdrant <see cref="PointStruct"/>.
        /// Always includes both dense and sparse vectors.
        /// </summary>
        internal static PointStruct ToPointStruct(VectorRecord record)
        {
            var point = new PointStruct
            {
                Id = CreatePointId(record.Id),
                Vectors = record.Vector ?? Array.Empty<float>(),
            };

            var namedVectors = new NamedVectors();

            if (record.Vector != null && record.Vector.Length > 0)
            {
                var denseProto = new DenseVector();
                denseProto.Data.AddRange(record.Vector);
                namedVectors.Vectors.Add(QdrantOptions.DenseVectorName, new Vector { Dense = denseProto });
            }

            if (!string.IsNullOrEmpty(record.Content))
            {
                var (indices, values) = BuildSparseVector(record.Content);
                if (indices.Length > 0)
                {
                    var sparseProto = new SparseVector();
                    sparseProto.Indices.AddRange(indices);
                    sparseProto.Values.AddRange(values);
                    namedVectors.Vectors.Add(QdrantOptions.SparseVectorName, new Vector { Sparse = sparseProto });
                }
            }

            if (namedVectors.Vectors.Count > 0)
                point.Vectors = new Vectors { Vectors_ = namedVectors };

            point.Payload[PayloadKeyId] = record.Id;
            point.Payload[PayloadKeyContent] = record.Content ?? string.Empty;

            if (record.Metadata != null)
            {
                foreach (var kvp in record.Metadata)
                {
                    point.Payload[$"{PayloadMetadataPrefix}{kvp.Key}"] = kvp.Value;
                }
            }

            return point;
        }

        #endregion

        #region Vector Extraction

        /// <summary>
        /// Extracts the dense vector from a <see cref="VectorsOutput"/>.
        /// Handles both flat vector and named vector formats.
        /// </summary>
        internal static float[] ExtractDenseVector(VectorsOutput? vectors)
        {
            var dense = vectors?.Vector?.GetDenseVector();
            if (dense?.Data != null)
                return dense.Data.ToArray();

            var namedDense = vectors?.Vectors?.Vectors.TryGetValue(QdrantOptions.DenseVectorName, out var namedVector) == true
                ? namedVector.Dense
                : null;

            return namedDense?.Data?.ToArray() ?? Array.Empty<float>();
        }

        internal static Dictionary<string, string> ExtractMetadata(
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

        #endregion

        #region Schema Marker

        internal static bool IsSchemaMarker(RetrievedPoint point)
        {
            return point.Payload.TryGetValue(PayloadKeyId, out var idVal)
                && string.Equals(idVal.StringValue, SchemaMarkerId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Creates a schema marker <see cref="PointStruct"/> for persisting collection schema metadata.
        /// </summary>
        internal static PointStruct CreateSchemaMarkerPoint(int dimension)
        {
            var denseProto = new DenseVector();
            denseProto.Data.AddRange(new float[dimension]);

            var namedVectors = new NamedVectors();
            namedVectors.Vectors.Add(QdrantOptions.DenseVectorName, new Vector { Dense = denseProto });

            var marker = new PointStruct
            {
                Id = CreatePointId(SchemaMarkerId),
                Vectors = new Vectors { Vectors_ = namedVectors }
            };

            marker.Payload[PayloadKeyId] = SchemaMarkerId;
            marker.Payload[PayloadKeyContent] = string.Empty;
            marker.Payload[PayloadKeySchemaVersion] = CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
            marker.Payload[PayloadKeySchemaKind] = CurrentSchemaKind;

            return marker;
        }

        #endregion

        #region Payload Indexes

        /// <summary>
        /// Creates payload indexes required for filtering.
        /// Failures are silently ignored (indexes may already exist).
        /// </summary>
        internal static async Task CreatePayloadIndexesAsync(
            QdrantClient client,
            string collectionName,
            QdrantOptions options,
            CancellationToken cancellationToken)
        {
            var indexedFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var indexOption in options.GetAllPayloadIndexes())
            {
                if (!indexedFields.Add(indexOption.Field))
                    continue;

                try
                {
                    await client.CreatePayloadIndexAsync(
                        collectionName,
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
    }
}
