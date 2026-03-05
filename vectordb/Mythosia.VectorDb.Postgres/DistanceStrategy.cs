namespace Mythosia.VectorDb.Postgres
{
    /// <summary>
    /// Distance function used for vector similarity search.
    /// </summary>
    public enum DistanceStrategy
    {
        /// <summary>
        /// Cosine distance (1 − cosine similarity).
        /// Best for text embeddings (e.g., OpenAI, Cohere).
        /// pgvector operator: <c>&lt;=&gt;</c>
        /// </summary>
        Cosine,

        /// <summary>
        /// L2 (Euclidean) distance.
        /// Best for image or spatial embeddings.
        /// pgvector operator: <c>&lt;-&gt;</c>
        /// </summary>
        Euclidean,

        /// <summary>
        /// Negative inner product.
        /// Best for pre-normalized vectors where higher dot product means more similar.
        /// pgvector operator: <c>&lt;#&gt;</c>
        /// </summary>
        InnerProduct
    }
}
