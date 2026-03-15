using System.Collections.Generic;

namespace Mythosia.AI.Loaders
{
    /// <summary>
    /// Represents parser output before it is converted into a RagDocument.
    /// </summary>
    public class ParsedDocument
    {
        /// <summary>
        /// The extracted text content.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Metadata extracted by the parser (title, page count, etc.).
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        public ParsedDocument() { }

        public ParsedDocument(string content)
        {
            Content = content;
        }
    }
}
