namespace Mythosia.VectorDb.Qdrant
{
    /// <summary>
    /// Distance function used for similarity search in Qdrant.
    /// </summary>
    public enum QdrantDistanceStrategy
    {
        /// <summary>
        /// Cosine similarity. Score range: [-1, 1]. Higher = more similar.
        /// </summary>
        Cosine,

        /// <summary>
        /// Euclidean (L2) distance. Lower distance = more similar.
        /// Qdrant returns negative distance so higher score = more similar.
        /// </summary>
        Euclidean,

        /// <summary>
        /// Dot product. Higher = more similar. Vectors should be normalized for meaningful results.
        /// </summary>
        DotProduct
    }
}
