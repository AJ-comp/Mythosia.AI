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
        /// The dimensionality of the embedding vectors produced by this provider.
        /// </summary>
        int Dimensions { get; }

        /// <summary>
        /// Generates an embedding vector for a single text input.
        /// </summary>
        Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates embedding vectors for multiple text inputs in a single batch call.
        /// </summary>
        Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
    }
}
