using System.Collections.Generic;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Represents a text chunk produced by an ITextSplitter.
    /// </summary>
    public class RagChunk
    {
        /// <summary>
        /// Unique identifier for this chunk.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The parent document identifier.
        /// </summary>
        public string DocumentId { get; set; } = string.Empty;

        /// <summary>
        /// The text content of this chunk.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Zero-based index of this chunk within the parent document.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Inherited and chunk-specific metadata.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        public RagChunk() { }

        public RagChunk(string id, string documentId, string content, int index)
        {
            Id = id;
            DocumentId = documentId;
            Content = content;
            Index = index;
        }
    }
}
