using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Embeddings
{
    /// <summary>
    /// Local embedding provider using feature hashing (hashing trick).
    /// Works without any external API key. Suitable for prototyping and small-scale use.
    /// Quality is lower than neural embeddings (OpenAI, etc.) but requires zero configuration.
    /// </summary>
    public class LocalEmbeddingProvider : IEmbeddingProvider
    {
        /// <inheritdoc />
        public int Dimensions { get; }

        /// <summary>
        /// Creates a local embedding provider with the specified vector dimensionality.
        /// Higher dimensions capture more information but use more memory.
        /// </summary>
        /// <param name="dimensions">Vector dimensionality. Default is 1024.</param>
        public LocalEmbeddingProvider(int dimensions = 1024)
        {
            if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions));
            Dimensions = dimensions;
        }

        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            var vector = ComputeVector(text);
            return Task.FromResult(vector);
        }

        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var results = new List<float[]>();
            foreach (var text in texts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(ComputeVector(text));
            }
            return Task.FromResult<IReadOnlyList<float[]>>(results);
        }

        private float[] ComputeVector(string text)
        {
            var vector = new float[Dimensions];
            var tokens = Tokenize(text);

            // Unigrams
            foreach (var token in tokens)
            {
                AddToVector(vector, token, 1.0f);
            }

            // Bigrams for basic context awareness
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                var bigram = tokens[i] + " " + tokens[i + 1];
                AddToVector(vector, bigram, 0.5f);
            }

            Normalize(vector);
            return vector;
        }

        private void AddToVector(float[] vector, string token, float weight)
        {
            uint hash = FnvHash(token);
            int index = (int)(hash % (uint)Dimensions);
            float sign = ((hash >> 31) & 1) == 0 ? 1f : -1f;
            vector[index] += sign * weight;
        }

        private static string[] Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            var lower = text.ToLowerInvariant();
            var chars = lower.ToCharArray();

            // Replace non-alphanumeric (keeping unicode letters/digits) with spaces
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                    chars[i] = ' ';
            }

            return new string(chars).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// FNV-1a hash for consistent, well-distributed hashing.
        /// </summary>
        private static uint FnvHash(string key)
        {
            const uint FnvPrime = 16777619u;
            const uint FnvOffsetBasis = 2166136261u;

            uint hash = FnvOffsetBasis;
            for (int i = 0; i < key.Length; i++)
            {
                hash ^= key[i];
                hash *= FnvPrime;
            }
            return hash;
        }

        private static void Normalize(float[] vector)
        {
            double norm = 0.0;
            for (int i = 0; i < vector.Length; i++)
                norm += vector[i] * (double)vector[i];

            if (norm == 0.0) return;

            norm = Math.Sqrt(norm);
            for (int i = 0; i < vector.Length; i++)
                vector[i] = (float)(vector[i] / norm);
        }
    }
}
