using System;
using System.Text.Json;

namespace Mythosia.AI.Rag.Embeddings
{
    internal static class EmbeddingResponseValidation
    {
        internal static JsonDocument ParseDocument(string json, string provider)
        {
            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                // Parser diagnostics can contain remote payload fragments. Expose neither
                // those fragments nor an inner exception carrying the original diagnostics.
                throw InvalidResponse(provider, "The response was not valid JSON.");
            }
        }

        internal static float[] ReadVector(JsonElement embedding, int dimensions, string provider)
        {
            if (embedding.ValueKind != JsonValueKind.Array)
                throw InvalidResponse(provider, "Every embedding must be an array.");
            if (embedding.GetArrayLength() != dimensions)
                throw InvalidResponse(provider,
                    $"Embedding dimension mismatch: expected {dimensions}, received {embedding.GetArrayLength()}.");

            var vector = new float[dimensions];
            var coordinate = 0;
            foreach (var value in embedding.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number)
                    || float.IsNaN(number) || float.IsInfinity(number))
                    throw InvalidResponse(provider, "Embedding coordinates must be finite floating-point numbers.");
                vector[coordinate++] = number;
            }
            return vector;
        }

        internal static InvalidOperationException InvalidResponse(string provider, string message)
            => new InvalidOperationException($"{provider} embeddings response is invalid. {message}");
    }
}
