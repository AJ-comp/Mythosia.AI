namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Controls how the pipeline chooses final references after optional re-ranking.
    /// </summary>
    public enum RagFinalSelectionMode
    {
        /// <summary>
        /// Preserve the current behavior: trust reranker scores as the final decision when reranking is enabled.
        /// </summary>
        RerankerOnly = 0,

        /// <summary>
        /// Preserve retrieval evidence by blending retrieval and reranker scores.
        /// </summary>
        WeightedBlend = 1
    }

    /// <summary>
    /// Final selection policy applied after retrieval and optional re-ranking.
    /// </summary>
    public sealed class RagFinalSelectionOptions
    {
        public const double DefaultRetrievalWeight = 0.65d;

        /// <summary>
        /// Selection mode. Defaults to <see cref="RagFinalSelectionMode.RerankerOnly"/>
        /// to preserve backward compatibility.
        /// </summary>
        public RagFinalSelectionMode Mode { get; set; } = RagFinalSelectionMode.RerankerOnly;

        /// <summary>
        /// Retrieval-score weight used when <see cref="Mode"/> is
        /// <see cref="RagFinalSelectionMode.WeightedBlend"/>.
        /// </summary>
        public double RetrievalWeight { get; set; } = DefaultRetrievalWeight;

        public double GetClampedRetrievalWeight()
        {
            if (RetrievalWeight < 0d) return 0d;
            if (RetrievalWeight > 1d) return 1d;
            return RetrievalWeight;
        }

        /// <summary>
        /// Returns a copy of this <see cref="RagFinalSelectionOptions"/> with the same field values.
        /// </summary>
        public RagFinalSelectionOptions Clone()
        {
            return new RagFinalSelectionOptions
            {
                Mode = Mode,
                RetrievalWeight = RetrievalWeight
            };
        }
    }
}
