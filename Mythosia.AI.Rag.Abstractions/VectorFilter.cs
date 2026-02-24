using System.Collections.Generic;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Filter criteria for vector store queries and deletions.
    /// All specified criteria are combined with AND logic.
    /// </summary>
    public class VectorFilter
    {
        /// <summary>
        /// Filter by namespace. Null means no namespace filter.
        /// </summary>
        public string? Namespace { get; set; }

        /// <summary>
        /// Filter by metadata key-value pairs. All pairs must match (AND).
        /// </summary>
        public Dictionary<string, string>? MetadataMatch { get; set; }

        /// <summary>
        /// Minimum similarity score threshold. Results below this score are excluded.
        /// </summary>
        public double? MinScore { get; set; }

        public VectorFilter() { }

        public static VectorFilter ByNamespace(string ns) => new VectorFilter { Namespace = ns };

        public static VectorFilter ByMetadata(string key, string value)
            => new VectorFilter { MetadataMatch = new Dictionary<string, string> { { key, value } } };
    }
}
