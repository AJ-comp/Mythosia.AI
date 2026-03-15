using System.Collections.Generic;

namespace Mythosia.AI.Loaders
{
    /// <summary>
    /// Represents a loaded document with its text content and associated metadata.
    /// </summary>
    public class RagDocument
    {
        /// <summary>
        /// Unique identifier for the document.
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
