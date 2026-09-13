using System;

namespace Mythosia.AI.Rag.Embeddings
{
    /// <summary>A packed one-bit embedding for Hamming-distance search, separate from cosine float vectors.</summary>
    /// <remarks>Binary requests require dimensions divisible by eight. No implicit conversion to float vectors is provided.</remarks>
    public sealed class PerplexityBinaryEmbedding
    {
        private readonly byte[] _data;

        public int Dimensions { get; }

        internal PerplexityBinaryEmbedding(byte[] data, int dimensions)
        {
            _data = (byte[])data.Clone();
            Dimensions = dimensions;
        }

        /// <summary>Returns a copy of the packed bits, retaining the provider's bit ordering.</summary>
        public byte[] ToArray() => (byte[])_data.Clone();

        /// <summary>Counts differing bits. Smaller distances indicate greater similarity.</summary>
        public int HammingDistance(PerplexityBinaryEmbedding other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (Dimensions != other.Dimensions) throw new ArgumentException("Binary embedding dimensions must match.", nameof(other));
            var distance = 0;
            for (var index = 0; index < _data.Length; index++)
            {
                var different = _data[index] ^ other._data[index];
                while (different != 0)
                {
                    different &= different - 1;
                    distance++;
                }
            }
            return distance;
        }
    }
}
