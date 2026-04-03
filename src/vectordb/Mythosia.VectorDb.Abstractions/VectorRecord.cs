using System.Collections.Generic;

namespace Mythosia.VectorDb
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
        /// Use <c>Metadata["namespace"]</c> and <c>Metadata["scope"]</c> for logical isolation.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        public VectorRecord() { }

        public VectorRecord(string id, float[] vector, string content)
        {
            Id = id;
            Vector = vector;
            Content = content;
        }
    }
}
