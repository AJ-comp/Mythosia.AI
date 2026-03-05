namespace Mythosia.VectorDb.Postgres
{
    /// <summary>
    /// Vector index algorithm for approximate nearest neighbor (ANN) search.
    /// </summary>
    public enum IndexType
    {
        /// <summary>
        /// HNSW (Hierarchical Navigable Small World).
        /// Better recall, works on empty tables, recommended default.
        /// </summary>
        Hnsw,

        /// <summary>
        /// IVFFlat (Inverted File with Flat compression).
        /// Faster index build, but requires existing data and lower recall than HNSW.
        /// </summary>
        IvfFlat,

        /// <summary>
        /// No vector index. Performs exact (brute-force) sequential scan.
        /// Suitable for small datasets only.
        /// </summary>
        None
    }
}
