using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Embeddings
{
    /// <summary>Embeds ordered chunks with their enclosing document's context intact.</summary>
    /// <remarks>RAG uses IRetrievalEmbeddingProvider to preserve document groups instead of flattening chunks.
    /// Embed queries with GetQueryEmbeddingAsync using the same model and dimensions as the stored document vectors.
    /// The caller owns HttpClient. A request supports at most 512 documents and 16,000 total chunks; token limits also apply.</remarks>
    public sealed class PerplexityContextualizedEmbeddingProvider : IRetrievalEmbeddingProvider
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
            var inputs = ValidateDocuments(documentChunks, cancellationToken);
            if (inputs.Length == 0) return Array.Empty<IReadOnlyList<float[]>>();
            using var document = await _transport.SendAsync(inputs, true, false, cancellationToken).ConfigureAwait(false);
            return PerplexityEmbeddingTransport.ReadIndexed<IReadOnlyList<float[]>>(document.RootElement, inputs.Length,
                (item, index) => PerplexityEmbeddingTransport.ReadIndexed(item, inputs[index].Length, (chunk, _) => _transport.ReadNormalized(chunk)));
        }

        public async Task<float[]> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default)
            => (await GetDocumentEmbeddingsAsync(new[] { new[] { query } }, cancellationToken).ConfigureAwait(false))[0][0];

        async Task<IReadOnlyList<float[]>> IRetrievalEmbeddingProvider.GetDocumentEmbeddingsAsync(
            EmbeddingDocument document, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (document.Chunks.Count == 0) return Array.Empty<float[]>();
            return (await GetDocumentEmbeddingsAsync(new[] { document.Chunks }, cancellationToken).ConfigureAwait(false))[0];
        }

        /// <summary>Embeds one independent text. Use retrieval methods to express document or query intent.</summary>
        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => (await GetEmbeddingsAsync(new[] { text }, cancellationToken).ConfigureAwait(false))[0];

        /// <summary>Embeds each legacy flat input as its own independent singleton group.</summary>
        /// <remarks>Use GetDocumentEmbeddingsAsync for chunks sharing document context.</remarks>
        public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
            IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = PerplexityEmbeddingTransport.ValidateTexts(texts, nameof(texts), 512, cancellationToken);
            var groups = await GetDocumentEmbeddingsAsync(inputs.Select(text => new[] { text }), cancellationToken).ConfigureAwait(false);
            return groups.Select(group => group[0]).ToArray();
        }

        public async Task<IReadOnlyList<IReadOnlyList<PerplexityBinaryEmbedding>>> GetBinaryDocumentEmbeddingsAsync(
            IEnumerable<IEnumerable<string>> documentChunks, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _transport.ValidateBinaryDimensions();
            var inputs = ValidateDocuments(documentChunks, cancellationToken);
            if (inputs.Length == 0) return Array.Empty<IReadOnlyList<PerplexityBinaryEmbedding>>();
            using var document = await _transport.SendAsync(inputs, true, true, cancellationToken).ConfigureAwait(false);
            return PerplexityEmbeddingTransport.ReadIndexed<IReadOnlyList<PerplexityBinaryEmbedding>>(document.RootElement, inputs.Length,
                (item, index) => PerplexityEmbeddingTransport.ReadIndexed(item, inputs[index].Length, (chunk, _) => _transport.ReadBinary(chunk)));
        }

        public async Task<PerplexityBinaryEmbedding> GetBinaryQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default)
            => (await GetBinaryDocumentEmbeddingsAsync(new[] { new[] { query } }, cancellationToken).ConfigureAwait(false))[0][0];

        private static string[][] ValidateDocuments(IEnumerable<IEnumerable<string>> documentChunks,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (documentChunks == null) throw new ArgumentNullException(nameof(documentChunks));
            const string message = "Provide at most 512 nonempty document groups and 16,000 total chunks, retaining each document's chunk order.";
            var documents = new List<string[]>();
            var totalChunks = 0;
            using (var iterator = documentChunks.GetEnumerator())
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var hasNext = iterator.MoveNext();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!hasNext) break;
                    // Every group must contain a chunk, so either exhausted budget
                    // rules out the next group before accessing user-supplied Current.
                    if (documents.Count == 512 || totalChunks == 16000)
                        throw new ArgumentException(message, nameof(documentChunks));
                    var chunks = iterator.Current;
                    cancellationToken.ThrowIfCancellationRequested();
                    var inputs = PerplexityEmbeddingTransport.ValidateTexts(chunks, nameof(documentChunks),
                        16000 - totalChunks, cancellationToken, message);
                    if (inputs.Length == 0) throw new ArgumentException(message, nameof(documentChunks));
                    totalChunks += inputs.Length;
                    documents.Add(inputs);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return documents.ToArray();
        }
    }
}
