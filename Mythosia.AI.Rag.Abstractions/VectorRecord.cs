using System.Collections.Generic;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// A record stored in a vector store, containing the embedding vector, content, and metadata.
    /// </summary>
    public class VectorRecord
    {
        /// <summary>
        /// Unique identifier for this record.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The embedding vector.
        /// </summary>
        public float[] Vector { get; set; } = System.Array.Empty<float>();

        /// <summary>
        /// The original text content associated with this vector.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Arbitrary key-value metadata for filtering and display.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Logical namespace for multi-tenant or scoped isolation.
        /// </summary>
        public string? Namespace { get; set; }

        public VectorRecord() { }

        public VectorRecord(string id, float[] vector, string content)
        {
            Id = id;
            Vector = vector;
            Content = content;
        }
    }
}
