namespace Mythosia.VectorDb
{
    /// <summary>
    /// Extension methods for <see cref="IVectorStore"/> providing the fluent builder API.
    /// </summary>
    public static class VectorStoreExtensions
    {
        /// <summary>
        /// Creates a fluent context scoped to the specified namespace.
        /// <para>
        /// Usage:
        /// <code>
        /// await store.InNamespace("docs").UpsertAsync(record);
        /// await store.InNamespace("docs").InScope("tenant-1").SearchAsync(vector);
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="store">The vector store instance.</param>
        /// <param name="namespace">The namespace to scope operations to.</param>
        /// <returns>A namespace-scoped context.</returns>
        [System.Obsolete("InNamespace will be removed in a future major version. Use VectorFilter.Where(\"namespace\", value) and Metadata for logical isolation instead.")]
        public static INamespaceContext InNamespace(this IVectorStore store, string @namespace)
            => new NamespaceContext(store, @namespace);
    }
}
