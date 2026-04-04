namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Configuration options for the RAG pipeline.
    /// </summary>
    public class RagPipelineOptions
    {
        /// <summary>
        /// Default query policy applied when a per-request query policy is not supplied.
        /// </summary>
        public RagQueryOptions DefaultQuery { get; set; } = new RagQueryOptions();

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
