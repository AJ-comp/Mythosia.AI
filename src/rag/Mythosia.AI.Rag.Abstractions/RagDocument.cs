using System.Collections.Generic;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Represents a loaded document with its text content and associated metadata.
    /// </summary>
    public class RagDocument
    {
        /// <summary>
        /// Unique identifier for the document. RAG indexing requires a nonblank value,
        /// including for an empty document that clears its previous index.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The text content of the document.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// The source path, URL, or identifier from which this document was loaded.
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Arbitrary key-value metadata associated with the document (e.g., title, author, date).
        /// The document_id key is reserved by the RAG pipeline: persisted chunks use <see cref="Id"/>
        /// for this key even if the input specifies another value. The input dictionary is not changed.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        public RagDocument() { }

        public RagDocument(string id, string content, string source)
        {
            Id = id;
            Content = content;
            Source = source;
        }
    }
}
