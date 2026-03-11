using System;

namespace Mythosia.VectorDb.Pinecone
{
    /// <summary>
    /// Configuration options for <see cref="PineconeStore"/>.
    /// </summary>
    public class PineconeOptions
    {
        /// <summary>
        /// Pinecone data plane host for a specific index.
        /// Examples:
        /// - https://my-index-xxxx.svc.us-east1-gcp.pinecone.io
        /// - my-index-xxxx.svc.us-east1-gcp.pinecone.io
        /// Required.
        /// </summary>
        public string IndexHost { get; set; } = string.Empty;

        /// <summary>
        /// Pinecone API key. Required.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Optional default namespace applied when a record/filter namespace is null.
        /// </summary>
        public string? DefaultNamespace { get; set; }

        /// <summary>
        /// Max vectors per upsert request. Default: 100.
        /// </summary>
        public int UpsertBatchSize { get; set; } = 100;

        /// <summary>
        /// Timeout for requests when the store creates its own <see cref="System.Net.Http.HttpClient"/>.
        /// Default: 100 seconds.
        /// </summary>
        public int RequestTimeoutSeconds { get; set; } = 100;

        /// <summary>
        /// When true, the store will automatically create the Pinecone index
        /// via the Control Plane API if it does not already exist.
        /// The index is created with <c>dotproduct</c> metric (required for hybrid search).
        /// Requires <see cref="IndexName"/>, <see cref="Dimension"/>,
        /// <see cref="Cloud"/>, and <see cref="Region"/> to be set.
        /// Default: false.
        /// </summary>
        public bool AutoCreateIndex { get; set; }

        /// <summary>
        /// Name of the Pinecone index. Required when <see cref="AutoCreateIndex"/> is true.
        /// Used to create or discover the index via the Control Plane API.
        /// </summary>
        public string? IndexName { get; set; }

        /// <summary>
        /// Vector dimension. Required when <see cref="AutoCreateIndex"/> is true.
        /// </summary>
        public int Dimension { get; set; }

        /// <summary>
        /// Cloud provider for serverless index (e.g. "aws", "gcp", "azure").
        /// Required when <see cref="AutoCreateIndex"/> is true.
        /// </summary>
        public string? Cloud { get; set; }

        /// <summary>
        /// Cloud region for serverless index (e.g. "us-east-1").
        /// Required when <see cref="AutoCreateIndex"/> is true.
        /// </summary>
        public string? Region { get; set; }

        /// <summary>
        /// Pinecone Control Plane API host.
        /// Default: <c>https://api.pinecone.io</c>.
        /// </summary>
        public string ControlPlaneHost { get; set; } = "https://api.pinecone.io";


        /// <summary>
        /// Validates the options and throws <see cref="ArgumentException"/> if invalid.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new ArgumentException("ApiKey must not be empty.", nameof(ApiKey));

            if (AutoCreateIndex)
            {
                if (string.IsNullOrWhiteSpace(IndexName))
                    throw new ArgumentException("IndexName must not be empty when AutoCreateIndex is true.", nameof(IndexName));

                if (Dimension <= 0)
                    throw new ArgumentException("Dimension must be greater than 0 when AutoCreateIndex is true.", nameof(Dimension));

                if (string.IsNullOrWhiteSpace(Cloud))
                    throw new ArgumentException("Cloud must not be empty when AutoCreateIndex is true.", nameof(Cloud));

                if (string.IsNullOrWhiteSpace(Region))
                    throw new ArgumentException("Region must not be empty when AutoCreateIndex is true.", nameof(Region));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(IndexHost))
                    throw new ArgumentException("IndexHost must not be empty.", nameof(IndexHost));
            }

            if (UpsertBatchSize <= 0)
                throw new ArgumentException("UpsertBatchSize must be greater than 0.", nameof(UpsertBatchSize));

            if (RequestTimeoutSeconds <= 0)
                throw new ArgumentException("RequestTimeoutSeconds must be greater than 0.", nameof(RequestTimeoutSeconds));
        }
    }
}
