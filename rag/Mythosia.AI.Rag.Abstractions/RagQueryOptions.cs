namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Per-request query overrides for RAG retrieval.
    /// Values not provided fall back to <see cref="RagPipelineOptions"/> defaults.
    /// </summary>
    public sealed class RagQueryOptions
    {
        /// <summary>
        /// Override for the number of top results to retrieve.
        /// </summary>
        public int? TopK { get; set; }

        /// <summary>
        /// Override for minimum similarity score threshold.
        /// </summary>
        public double? MinScore { get; set; }

        /// <summary>
        /// Override for target namespace used during search.
        /// </summary>
        public string? Namespace { get; set; }
    }
}
