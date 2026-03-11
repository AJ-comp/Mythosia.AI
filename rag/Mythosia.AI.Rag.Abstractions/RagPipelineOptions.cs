namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Configuration options for the RAG pipeline.
    /// </summary>
    public class RagPipelineOptions
    {
        /// <summary>
        /// Default namespace used when none is specified.
        /// </summary>
        public string DefaultNamespace { get; set; } = "default";

        /// <summary>
        /// Default scope for vector records.
        /// </summary>
        public string? DefaultScope { get; set; }

        /// <summary>
        /// Number of top results to retrieve during search. Default is 5.
        /// </summary>
        public int TopK { get; set; } = 5;

        /// <summary>
        /// Minimum similarity score threshold. Results below this are discarded.
        /// </summary>
        public double? MinScore { get; set; }

        /// <summary>
        /// Multiplier applied to <see cref="TopK"/> when a reranker is configured.
        /// The retrieval stage fetches TopK × RetrievalMultiplier candidates,
        /// then the reranker selects the best TopK from that wider pool.
        /// Ignored when no reranker is present. Default is 3.
        /// </summary>
        public int RetrievalMultiplier { get; set; } = 3;

        /// <summary>
        /// Optional prompt template with {context} and {question} placeholders.
        /// When set, overrides the default context builder at query time.
        /// When null or whitespace, the default context builder is used.
        /// </summary>
        public string? PromptTemplate { get; set; }

        /// <summary>
        /// Maximum number of texts to embed in a single batch call.
        /// </summary>
        public int EmbeddingBatchSize { get; set; } = 100;
    }
}
