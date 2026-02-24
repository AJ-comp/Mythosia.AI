namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Configuration options for the RAG pipeline.
    /// </summary>
    public class RagPipelineOptions
    {
        /// <summary>
        /// Default collection name used when none is specified.
        /// </summary>
        public string DefaultCollection { get; set; } = "default";

        /// <summary>
        /// Default namespace for vector records.
        /// </summary>
        public string? DefaultNamespace { get; set; }

        /// <summary>
        /// Number of top results to retrieve during search. Default is 5.
        /// </summary>
        public int TopK { get; set; } = 5;

        /// <summary>
        /// Minimum similarity score threshold. Results below this are discarded.
        /// </summary>
        public double? MinScore { get; set; }

        /// <summary>
        /// Maximum number of texts to embed in a single batch call.
        /// </summary>
        public int EmbeddingBatchSize { get; set; } = 100;
    }
}
