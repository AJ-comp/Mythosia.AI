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
        /// <returns>
        /// A non-null ordered list of non-null chunks, each with non-null content and metadata
        /// and a nonblank ID unique across the target store. Include the document identity in
        /// chunk IDs to avoid collisions between documents. An empty list is a successful empty split.
        /// </returns>
        /// <remarks>
        /// RAG indexing rejects blank or duplicate IDs within a document before embedding or storage.
        /// It snapshots the returned chunks and metadata before awaiting embeddings. Do not mutate
        /// the result concurrently while it is being read. Different document calls are not a transaction.
        /// </remarks>
        IReadOnlyList<RagChunk> Split(RagDocument document);
    }
}
