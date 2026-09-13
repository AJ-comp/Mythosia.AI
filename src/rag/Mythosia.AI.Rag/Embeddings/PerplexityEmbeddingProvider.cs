using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Embeddings
{
    /// <summary>Standard Perplexity embeddings decoded from signed int8 and normalized for cosine retrieval.</summary>
    /// <remarks>The caller owns HttpClient. Each batch contains at most 512 texts; provider token limits also apply.
    /// Binary embeddings use explicit methods and a separate Hamming-distance representation.</remarks>
    public sealed class PerplexityEmbeddingProvider : IEmbeddingProvider
    {
        private readonly PerplexityEmbeddingTransport _transport;
        public string Model => _transport.Model;
        public int Dimensions => _transport.Dimensions;

        public PerplexityEmbeddingProvider(string apiKey, HttpClient httpClient,
            string model = PerplexityEmbeddingModels.Standard0_6B, int? dimensions = null)
        {
            _transport = new PerplexityEmbeddingTransport(apiKey, httpClient, model, dimensions, false);
        }

        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => (await GetEmbeddingsAsync(new[] { text }, cancellationToken).ConfigureAwait(false))[0];

        public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = PerplexityEmbeddingTransport.ValidateTexts(texts, nameof(texts), 512);
            if (inputs.Length == 0) return Array.Empty<float[]>();
            using var document = await _transport.SendAsync(inputs, false, false, cancellationToken).ConfigureAwait(false);
            return PerplexityEmbeddingTransport.ReadIndexed(document.RootElement, inputs.Length, (item, _) => _transport.ReadNormalized(item));
        }

        public async Task<PerplexityBinaryEmbedding> GetBinaryEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => (await GetBinaryEmbeddingsAsync(new[] { text }, cancellationToken).ConfigureAwait(false))[0];

        public async Task<IReadOnlyList<PerplexityBinaryEmbedding>> GetBinaryEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _transport.ValidateBinaryDimensions();
            var inputs = PerplexityEmbeddingTransport.ValidateTexts(texts, nameof(texts), 512);
            if (inputs.Length == 0) return Array.Empty<PerplexityBinaryEmbedding>();
            using var document = await _transport.SendAsync(inputs, false, true, cancellationToken).ConfigureAwait(false);
            return PerplexityEmbeddingTransport.ReadIndexed(document.RootElement, inputs.Length, (item, _) => _transport.ReadBinary(item));
        }
    }
}
