using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Embeddings
{
    /// <summary>Embeds ordered chunks with their enclosing document's context intact.</summary>
    /// <remarks>This provider intentionally does not implement IEmbeddingProvider: flattening document groups loses context.
    /// Embed queries with GetQueryEmbeddingAsync using the same model and dimensions as the stored document vectors.
    /// The caller owns HttpClient. A request supports at most 512 documents and 16,000 total chunks; token limits also apply.</remarks>
    public sealed class PerplexityContextualizedEmbeddingProvider
    {
        private readonly PerplexityEmbeddingTransport _transport;
        public string Model => _transport.Model;
        public int Dimensions => _transport.Dimensions;

        public PerplexityContextualizedEmbeddingProvider(string apiKey, HttpClient httpClient,
            string model = PerplexityEmbeddingModels.Context0_6B, int? dimensions = null)
        {
            _transport = new PerplexityEmbeddingTransport(apiKey, httpClient, model, dimensions, true);
        }

        public async Task<IReadOnlyList<IReadOnlyList<float[]>>> GetDocumentEmbeddingsAsync(
            IEnumerable<IEnumerable<string>> documentChunks, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = ValidateDocuments(documentChunks);
            if (inputs.Length == 0) return Array.Empty<IReadOnlyList<float[]>>();
            using var document = await _transport.SendAsync(inputs, true, false, cancellationToken).ConfigureAwait(false);
            return PerplexityEmbeddingTransport.ReadIndexed<IReadOnlyList<float[]>>(document.RootElement, inputs.Length,
                (item, index) => PerplexityEmbeddingTransport.ReadIndexed(item, inputs[index].Length, (chunk, _) => _transport.ReadNormalized(chunk)));
        }

        public async Task<float[]> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default)
            => (await GetDocumentEmbeddingsAsync(new[] { new[] { query } }, cancellationToken).ConfigureAwait(false))[0][0];

        public async Task<IReadOnlyList<IReadOnlyList<PerplexityBinaryEmbedding>>> GetBinaryDocumentEmbeddingsAsync(
            IEnumerable<IEnumerable<string>> documentChunks, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _transport.ValidateBinaryDimensions();
            var inputs = ValidateDocuments(documentChunks);
            if (inputs.Length == 0) return Array.Empty<IReadOnlyList<PerplexityBinaryEmbedding>>();
            using var document = await _transport.SendAsync(inputs, true, true, cancellationToken).ConfigureAwait(false);
            return PerplexityEmbeddingTransport.ReadIndexed<IReadOnlyList<PerplexityBinaryEmbedding>>(document.RootElement, inputs.Length,
                (item, index) => PerplexityEmbeddingTransport.ReadIndexed(item, inputs[index].Length, (chunk, _) => _transport.ReadBinary(chunk)));
        }

        public async Task<PerplexityBinaryEmbedding> GetBinaryQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default)
            => (await GetBinaryDocumentEmbeddingsAsync(new[] { new[] { query } }, cancellationToken).ConfigureAwait(false))[0][0];

        private static string[][] ValidateDocuments(IEnumerable<IEnumerable<string>> documentChunks)
        {
            if (documentChunks == null) throw new ArgumentNullException(nameof(documentChunks));
            var documents = documentChunks.Select(chunks => PerplexityEmbeddingTransport.ValidateTexts(chunks, nameof(documentChunks), 16000)).ToArray();
            if (documents.Length > 512 || documents.Any(chunks => chunks.Length == 0) || documents.Sum(chunks => (long)chunks.Length) > 16000)
                throw new ArgumentException("Provide at most 512 nonempty document groups and 16,000 total chunks, retaining each document's chunk order.", nameof(documentChunks));
            return documents;
        }
    }
}
