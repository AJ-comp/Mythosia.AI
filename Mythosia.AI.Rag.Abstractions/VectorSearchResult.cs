namespace Mythosia.AI.Rag
{
    /// <summary>
    /// A single result from a vector similarity search.
    /// </summary>
    public class VectorSearchResult
    {
        /// <summary>
        /// The matched vector record.
        /// </summary>
        public VectorRecord Record { get; set; } = new VectorRecord();

        /// <summary>
        /// Similarity score (higher = more similar). Typically cosine similarity in [0, 1].
        /// </summary>
        public double Score { get; set; }

        public VectorSearchResult() { }

        public VectorSearchResult(VectorRecord record, double score)
        {
            Record = record;
            Score = score;
        }
    }
}
