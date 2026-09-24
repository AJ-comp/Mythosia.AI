using System.Collections.ObjectModel;

namespace Mythosia.AI.Rag.Search.Pixie;

/// <summary>An immutable sorted sparse vector in the PIXIE token vocabulary.</summary>
public sealed class PixieSparseVector
{
    /// <summary>The empty vector.</summary>
    public static PixieSparseVector Empty { get; } = new(Array.Empty<int>(), Array.Empty<float>());

    /// <summary>Sorted unique token IDs.</summary>
    public IReadOnlyList<int> Indices { get; }

    /// <summary>Positive finite weights corresponding to <see cref="Indices"/>.</summary>
    public IReadOnlyList<float> Values { get; }

    /// <summary>Constructs a vector, defensively copying both sequences.</summary>
    public PixieSparseVector(IEnumerable<int> indices, IEnumerable<float> values)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(values);
        var ids = indices.ToArray();
        var weights = values.ToArray();
        if (ids.Length != weights.Length) throw new ArgumentException("Indices and values must have equal lengths.");
        for (var i = 0; i < ids.Length; i++)
        {
            if (ids[i] < 0 || ids[i] >= 50000 || (i > 0 && ids[i] <= ids[i - 1]))
                throw new ArgumentException("PIXIE token IDs must be unique, ascending and between 0 and 49999.", nameof(indices));
            if (!float.IsFinite(weights[i]) || weights[i] <= 0)
                throw new ArgumentException("Sparse weights must be finite and positive.", nameof(values));
        }
        Indices = new ReadOnlyCollection<int>(ids);
        Values = new ReadOnlyCollection<float>(weights);
    }
}
