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
        /// Validates the options and throws <see cref="ArgumentException"/> if invalid.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(IndexHost))
                throw new ArgumentException("IndexHost must not be empty.", nameof(IndexHost));

            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new ArgumentException("ApiKey must not be empty.", nameof(ApiKey));

            if (UpsertBatchSize <= 0)
                throw new ArgumentException("UpsertBatchSize must be greater than 0.", nameof(UpsertBatchSize));

            if (RequestTimeoutSeconds <= 0)
                throw new ArgumentException("RequestTimeoutSeconds must be greater than 0.", nameof(RequestTimeoutSeconds));
        }
    }
}
