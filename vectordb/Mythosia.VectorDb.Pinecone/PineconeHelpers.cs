using System.Collections.Generic;
using System.Text;

namespace Mythosia.VectorDb.Pinecone
{
    /// <summary>
    /// Shared helper methods for <see cref="PineconeStore"/>.
    /// </summary>
    internal static class PineconeHelpers
    {
        #region Sparse Vector

        /// <summary>
        /// Builds a sparse vector from text using BM25 tokenizer.
        /// Token hash -> index, term frequency -> value.
        /// </summary>
        internal static (uint[] indices, float[] values) BuildSparseVector(string text)
        {
            var tf = Bm25Tokenizer.Analyze(text).TermFrequencies;

            var indices = new uint[tf.Count];
            var values = new float[tf.Count];
            int i = 0;

            foreach (var kvp in tf)
            {
                indices[i] = StableHash(kvp.Key);
                values[i] = kvp.Value;
                i++;
            }

            return (indices, values);
        }

        /// <summary>
        /// Deterministic hash via <see cref="System.IO.Hashing.XxHash32"/>.
        /// Unlike <see cref="string.GetHashCode"/>, this produces the same value
        /// across process restarts and .NET runtime versions — critical for
        /// sparse vector indices persisted in Pinecone.
        /// </summary>
        internal static uint StableHash(string term)
        {
            var bytes = Encoding.UTF8.GetBytes(term);
            return System.IO.Hashing.XxHash32.HashToUInt32(bytes);
        }

        #endregion
    }
}
