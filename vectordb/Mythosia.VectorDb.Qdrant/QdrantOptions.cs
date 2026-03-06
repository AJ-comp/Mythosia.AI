using Qdrant.Client.Grpc;
using System;
using System.Collections.Generic;

namespace Mythosia.VectorDb.Qdrant
{
    /// <summary>
    /// Configuration options for <see cref="QdrantStore"/>.
    /// </summary>
    public class QdrantOptions
    {
        private const string ReservedPayloadKeyNamespace = "_namespace";
        private const string ReservedPayloadKeyScope = "_scope";

        /// <summary>
        /// Qdrant server host. Default: "localhost".
        /// </summary>
        public string Host { get; set; } = "localhost";

        /// <summary>
        /// Qdrant gRPC port. Default: 6334.
        /// </summary>
        public int Port { get; set; } = 6334;

        /// <summary>
        /// Whether to use TLS for the gRPC connection. Default: false.
        /// </summary>
        public bool UseTls { get; set; } = false;

        /// <summary>
        /// Optional API key for authentication.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Qdrant collection name. This is the physical container for all vector data.
        /// Namespaces and scopes provide logical isolation within this single collection.
        /// Required.
        /// </summary>
        public string CollectionName { get; set; } = string.Empty;

        /// <summary>
        /// Embedding vector dimension. Must match the dimension used by the embedding provider.
        /// Required (must be greater than 0).
        /// </summary>
        public int Dimension { get; set; }

        /// <summary>
        /// Distance function for similarity search. Default: <see cref="QdrantDistanceStrategy.Cosine"/>.
        /// </summary>
        public QdrantDistanceStrategy DistanceStrategy { get; set; } = QdrantDistanceStrategy.Cosine;

        /// <summary>
        /// When true (default), the collection is automatically created on first upsert
        /// if it does not already exist.
        /// When false, the collection must already exist or an exception is thrown.
        /// </summary>
        public bool AutoCreateCollection { get; set; } = true;

        /// <summary>
        /// Additional payload fields to index when the collection is ensured.
        /// Reserved fields (<c>_namespace</c>, <c>_scope</c>) are always indexed.
        /// </summary>
        public IList<QdrantIndexOption> AdditionalPayloadIndexes { get; set; }
            = new List<QdrantIndexOption>();

        internal IEnumerable<QdrantIndexOption> GetAllPayloadIndexes()
        {
            yield return new QdrantIndexOption(ReservedPayloadKeyNamespace, PayloadSchemaType.Keyword);
            yield return new QdrantIndexOption(ReservedPayloadKeyScope, PayloadSchemaType.Keyword);

            if (AdditionalPayloadIndexes == null)
                yield break;

            foreach (var index in AdditionalPayloadIndexes)
            {
                if (index == null || string.IsNullOrWhiteSpace(index.Field))
                    continue;

                yield return index;
            }
        }

        /// <summary>
        /// Validates the options and throws <see cref="ArgumentException"/> if invalid.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Host))
                throw new ArgumentException("Host must not be empty.", nameof(Host));

            if (Port <= 0 || Port > 65535)
                throw new ArgumentException("Port must be between 1 and 65535.", nameof(Port));

            if (string.IsNullOrWhiteSpace(CollectionName))
                throw new ArgumentException("CollectionName must not be empty.", nameof(CollectionName));

            if (Dimension <= 0)
                throw new ArgumentException("Dimension must be greater than 0.", nameof(Dimension));

            if (!Enum.IsDefined(typeof(QdrantDistanceStrategy), DistanceStrategy))
                throw new ArgumentException("DistanceStrategy is invalid.", nameof(DistanceStrategy));
        }
    }

    /// <summary>
    /// Payload index option for a specific field.
    /// </summary>
    public class QdrantIndexOption
    {
        public QdrantIndexOption()
        {
        }

        public QdrantIndexOption(string field, PayloadSchemaType schemaType)
        {
            Field = field;
            SchemaType = schemaType;
        }

        /// <summary>
        /// Payload field name (for example, <c>meta.author</c>).
        /// </summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>
        /// Payload index schema type.
        /// </summary>
        public PayloadSchemaType SchemaType { get; set; } = PayloadSchemaType.Keyword;
    }
}
