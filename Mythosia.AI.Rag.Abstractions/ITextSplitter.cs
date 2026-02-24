using Mythosia.AI.Loaders;
using System.Collections.Generic;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Splits a document into smaller chunks suitable for embedding and retrieval.
    /// </summary>
    public interface ITextSplitter
    {
        /// <summary>
        /// Splits a document into chunks.
        /// Implementations may split by character count, token count, sentence boundary, etc.
        /// </summary>
        /// <param name="document">The document to split.</param>
        /// <returns>An ordered list of chunks.</returns>
        IReadOnlyList<RagChunk> Split(RagDocument document);
    }
}
