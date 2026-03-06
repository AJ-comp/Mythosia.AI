using System.Collections.Generic;

namespace Mythosia.VectorDb
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
        /// Filter by scope. Null means no scope filter.
        /// </summary>
        public string? Scope { get; set; }

        /// <summary>
        /// Filter by metadata key-value pairs. All pairs must match (AND).
        /// </summary>
        public Dictionary<string, string>? MetadataMatch { get; set; }

        /// <summary>
        /// Minimum similarity score threshold. Results below this score are excluded.
        /// </summary>
        public double? MinScore { get; set; }

        public VectorFilter() { }

        public static VectorFilter ByNamespace(string @namespace) => new VectorFilter { Namespace = @namespace };

        public static VectorFilter ByScope(string scope) => new VectorFilter { Scope = scope };

        public static VectorFilter ByMetadata(string key, string value)
            => new VectorFilter { MetadataMatch = new Dictionary<string, string> { { key, value } } };
    }
}
