using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Generates embedding vectors for text inputs.
    /// Implement this interface to wrap any embedding API (OpenAI, Azure, local models, etc.).
    /// </summary>
    public interface IEmbeddingProvider
    {
        /// <summary>
        /// The positive dimensionality of every embedding vector produced by this provider.
        /// Keep this value stable while an indexing or retrieval operation is using the provider.
        /// </summary>
        int Dimensions { get; }

        /// <summary>
        /// Generates an embedding vector for a single text input.
        /// </summary>
        /// <returns>A non-null vector containing exactly <see cref="Dimensions"/> finite values.</returns>
        /// <remarks>
        /// The vector must remain stable while the caller reads it. Built-in RAG retrievers
        /// validate and copy the completed result before invoking retrieval progress callbacks
        /// or stores, so a later sequential embedding call can safely reuse the original buffer.
        /// Do not mutate returned data concurrently while it is being read or copied.
        /// </remarks>
        Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates embedding vectors for multiple text inputs in a single batch call.
        /// </summary>
        /// <returns>
        /// One non-null vector per input, in input order. Each vector must contain exactly
        /// <see cref="Dimensions"/> finite values. The returned list must not be null.
        /// </returns>
        /// <remarks>
        /// Returned vectors must remain stable while the caller reads them. RAG indexing
        /// validates and copies each completed batch before requesting the next batch, so
        /// reusing buffers in a later sequential call cannot alter already accepted vectors.
        /// Direct consumers that retain provider-owned buffers should copy them as needed.
        /// Do not mutate returned data concurrently while it is being read or copied.
        /// </remarks>
        Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
    }
}
