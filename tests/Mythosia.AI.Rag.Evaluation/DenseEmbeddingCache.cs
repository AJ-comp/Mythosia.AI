using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mythosia.AI.Rag;

namespace Mythosia.AI.Rag.Evaluation;

/// <summary>Content-addressed, provider-specific cache; shared vectors are never regenerated for each method.</summary>
public sealed class DenseEmbeddingCache(string directory)
{
    private sealed record Entry(int SchemaVersion, string Identity, string TextHash, float[] Vector);
    public int CacheHits { get; private set; }
    public int GeneratedVectors { get; private set; }
    public async Task<Dictionary<string, float[]>> GetAsync(IEnumerable<string> texts, string identity,
        IEmbeddingProvider provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Directory.CreateDirectory(directory);
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var text in texts.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = CachePath(identity, text);
            if (!File.Exists(path)) { missing.Add(text); continue; }
            var entry = JsonSerializer.Deserialize<Entry>(await File.ReadAllTextAsync(path, cancellationToken))
                ?? throw new InvalidDataException("Empty embedding cache entry.");
            if (entry.SchemaVersion != 1 || entry.Identity != identity || entry.TextHash != Hash(text))
                throw new InvalidDataException("Embedding cache identity mismatch.");
            Validate(entry.Vector, provider.Dimensions);
            result.Add(text, entry.Vector);
            CacheHits++;
        }
        for (var start = 0; start < missing.Count; start += 32)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = missing.Skip(start).Take(32).ToArray();
            var vectors = await provider.GetEmbeddingsAsync(batch, cancellationToken);
            if (vectors.Count != batch.Length) throw new InvalidDataException("Embedding count mismatch.");
            foreach (var vector in vectors) Validate(vector, provider.Dimensions);
            for (var i = 0; i < batch.Length; i++)
            {
                var vector = vectors[i].ToArray();
                var path = CachePath(identity, batch[i]);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Entry(1, identity, Hash(batch[i]), vector)), cancellationToken);
                    File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                result.Add(batch[i], vector);
                GeneratedVectors++;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
    private string CachePath(string identity, string text) => Path.Combine(directory, Hash(identity + "\0" + Hash(text)) + ".json");
    private static void Validate(float[]? vector, int dimensions)
    {
        if (vector == null || vector.Length != dimensions || vector.Any(v => !float.IsFinite(v)) || !vector.Any(v => v != 0))
            throw new InvalidDataException("Invalid dense embedding (dimensions, finite values and nonzero norm are required).");
    }
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
