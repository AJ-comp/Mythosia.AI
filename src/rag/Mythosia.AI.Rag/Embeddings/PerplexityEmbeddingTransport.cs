using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Embeddings
{
    internal sealed class PerplexityEmbeddingTransport
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        internal string Model { get; }
        internal int Dimensions { get; }

        internal PerplexityEmbeddingTransport(string apiKey, HttpClient httpClient, string model, int? dimensions, bool contextualized)
        {
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? throw new ArgumentException("An API key is required.", nameof(apiKey)) : apiKey;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            var small = contextualized ? PerplexityEmbeddingModels.Context0_6B : PerplexityEmbeddingModels.Standard0_6B;
            var large = contextualized ? PerplexityEmbeddingModels.Context4B : PerplexityEmbeddingModels.Standard4B;
            if (model != small && model != large)
                throw new ArgumentException(contextualized ? "Choose a contextualized Perplexity embedding model." : "Choose a standard Perplexity embedding model.", nameof(model));
            Model = model;
            var maximum = model == small ? 1024 : 2560;
            Dimensions = dimensions ?? maximum;
            if (Dimensions < 128 || Dimensions > maximum)
                throw new ArgumentOutOfRangeException(nameof(dimensions), $"Dimensions must be between 128 and {maximum} for this model.");
        }

        internal static string[] ValidateTexts(IEnumerable<string> texts, string parameter, int maximum)
        {
            if (texts == null) throw new ArgumentNullException(parameter);
            var inputs = texts.ToArray();
            if (inputs.Length > maximum || inputs.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException($"Provide at most {maximum} nonempty texts.", parameter);
            return inputs;
        }

        internal void ValidateBinaryDimensions()
        {
            if (Dimensions % 8 != 0)
                throw new NotSupportedException("Packed binary embeddings require dimensions divisible by eight; use normalized float embeddings for other dimensions.");
        }

        internal async Task<JsonDocument> SendAsync(object input, bool contextualized, bool binary, CancellationToken cancellationToken)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = Model, ["input"] = input, ["dimensions"] = Dimensions,
                ["encoding_format"] = binary ? "base64_binary" : "base64_int8"
            };
            var endpoint = contextualized ? "contextualizedembeddings" : "embeddings";
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.perplexity.ai/v1/" + endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            // Buffer success and error bodies under cancellation; the caller continues to own HttpClient.
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Perplexity embeddings request failed (HTTP {(int)response.StatusCode}, {response.ReasonPhrase}).");
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            JsonDocument document;
            try { document = JsonDocument.Parse(json); }
            catch (JsonException exception) { throw InvalidResponse("The response was not valid JSON.", exception); }
            try
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("model", out var model) ||
                    model.ValueKind != JsonValueKind.String || model.GetString() != Model)
                    throw InvalidResponse("The response model does not match the requested model.");
                return document;
            }
            catch { document.Dispose(); throw; }
        }

        internal static T[] ReadIndexed<T>(JsonElement parent, int count, Func<JsonElement, int, T> read)
        {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != count)
                throw InvalidResponse("The response count does not match the input count.");
            var ordered = new T[count];
            var seen = new bool[count];
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("index", out var indexValue) ||
                    indexValue.ValueKind != JsonValueKind.Number || !indexValue.TryGetInt32(out var index) ||
                    index < 0 || index >= count || seen[index])
                    throw InvalidResponse("Every response item must have a unique index matching an input.");
                seen[index] = true;
                ordered[index] = read(item, index);
            }
            return ordered;
        }

        internal float[] ReadNormalized(JsonElement item)
        {
            var bytes = ReadBytes(item, Dimensions);
            var vector = new float[Dimensions];
            double squaredNorm = 0;
            for (var index = 0; index < bytes.Length; index++)
            {
                var value = unchecked((sbyte)bytes[index]);
                vector[index] = value;
                squaredNorm += value * value;
            }
            if (squaredNorm == 0) throw InvalidResponse("A zero vector cannot be normalized for cosine search.");
            var norm = Math.Sqrt(squaredNorm);
            for (var index = 0; index < vector.Length; index++) vector[index] = (float)(vector[index] / norm);
            return vector;
        }

        internal PerplexityBinaryEmbedding ReadBinary(JsonElement item)
            => new PerplexityBinaryEmbedding(ReadBytes(item, Dimensions / 8), Dimensions);

        private static byte[] ReadBytes(JsonElement item, int expectedBytes)
        {
            if (!item.TryGetProperty("embedding", out var embedding) || embedding.ValueKind != JsonValueKind.String)
                throw InvalidResponse("An embedding must be a base64 string.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(embedding.GetString()!); }
            catch (FormatException exception) { throw InvalidResponse("An embedding was not valid base64.", exception); }
            if (bytes.Length != expectedBytes) throw InvalidResponse("An embedding's decoded dimensions do not match the requested dimensions.");
            return bytes;
        }

        private static InvalidOperationException InvalidResponse(string detail, Exception? inner = null)
            => new InvalidOperationException("Perplexity embeddings returned an invalid response. " + detail, inner);
    }
}
