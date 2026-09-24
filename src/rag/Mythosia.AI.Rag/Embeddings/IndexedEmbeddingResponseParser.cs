using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Mythosia.AI.Rag.Embeddings
{
    /// <summary>
    /// Validates the OpenAI-compatible embedding response before associating vectors with inputs.
    /// </summary>
    internal static class IndexedEmbeddingResponseParser
    {
        internal static IReadOnlyList<float[]> Parse(
            string json, int expectedCount, int dimensions, string provider, bool allowMissingIndices)
        {
            using (var document = EmbeddingResponseValidation.ParseDocument(json, provider))
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                    throw InvalidResponse(provider, "The response must contain a data array.");

                if (data.GetArrayLength() != expectedCount)
                    throw InvalidResponse(provider,
                        $"The response count does not match the input count: expected {expectedCount}, received {data.GetArrayLength()}.");

                var ordered = new float[expectedCount][];
                var seen = new bool[expectedCount];
                bool? indexed = null;
                var position = 0;
                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        throw InvalidResponse(provider, "Every response item must be an object.");

                    var hasIndex = item.TryGetProperty("index", out var indexValue);
                    if (!hasIndex && !allowMissingIndices)
                        throw InvalidResponse(provider, "Every response item must have an index.");
                    if (indexed.HasValue && indexed.Value != hasIndex)
                        throw InvalidResponse(provider, "Response items cannot mix present and missing indices.");
                    indexed = hasIndex;

                    var index = position;
                    if (hasIndex && (indexValue.ValueKind != JsonValueKind.Number
                        || !indexValue.TryGetInt32(out index) || index < 0 || index >= expectedCount))
                        throw InvalidResponse(provider, "Each index must be an integer within the input range.");
                    if (seen[index])
                        throw InvalidResponse(provider, "Response indices must be unique and cover every input.");
                    seen[index] = true;

                    if (!item.TryGetProperty("embedding", out var embedding)
                        || embedding.ValueKind != JsonValueKind.Array)
                        throw InvalidResponse(provider, "Every response item must contain an embedding array.");
                    ordered[index] = EmbeddingResponseValidation.ReadVector(embedding, dimensions, provider);
                    position++;
                }

                // Count, range and uniqueness guarantee complete coverage; an all-unindexed
                // response is accepted only for the legacy vLLM positional compatibility mode.
                return ordered;
            }
        }

        private static InvalidOperationException InvalidResponse(string provider, string message)
            => EmbeddingResponseValidation.InvalidResponse(provider, message);
    }
}
