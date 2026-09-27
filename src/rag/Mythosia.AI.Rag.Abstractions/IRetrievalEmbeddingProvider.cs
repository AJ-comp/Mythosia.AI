using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Optional embedding capability that distinguishes indexed documents from search queries.
    /// Existing <see cref="IEmbeddingProvider"/> implementations remain supported by RAG.
    /// </summary>
    public interface IRetrievalEmbeddingProvider : IEmbeddingProvider
    {
        /// <summary>
        /// Embeds every ordered chunk of one document. RAG passes the entire document,
        /// independently of <see cref="RagPipelineOptions.EmbeddingBatchSize"/>.
        /// </summary>
        /// <remarks>
        /// Return one vector per chunk in input order, with Dimensions finite coordinates.
        /// Contextual providers must not split the document's context or combine it with
        /// another document. Independent providers may batch or parallelize chunks internally.
        /// Reject unsupported input sizes rather than silently truncating content.
        /// Providers format their HTTP input without modifying the document's stored text.
        /// Returned vectors must remain stable while the caller validates and copies them.
        /// </remarks>
        Task<IReadOnlyList<float[]>> GetDocumentEmbeddingsAsync(
            EmbeddingDocument document, CancellationToken cancellationToken = default);

        /// <summary>Embeds one retrieval query using the same model and dimensions as its documents.</summary>
        /// <remarks>The returned vector has the same validity and ownership contract as document vectors.</remarks>
        Task<float[]> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default);
    }
}
