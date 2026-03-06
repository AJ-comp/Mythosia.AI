namespace Mythosia.VectorDb.Postgres;

/// <summary>
/// Optional per-request runtime tuning for vector search.
/// </summary>
public abstract class VectorSearchRuntimeOptions
{
    /// <summary>
    /// Optional preset profile for this request.
    /// </summary>
    public SearchProfile? Profile { get; init; }
}

/// <summary>
/// HNSW runtime search options.
/// </summary>
public sealed class HnswSearchRuntimeOptions : VectorSearchRuntimeOptions
{
    /// <summary>
    /// Optional HNSW ef_search override for this request.
    /// </summary>
    public int? EfSearch { get; init; }

    /// <summary>
    /// Fast profile preset for HNSW.
    /// </summary>
    public static HnswSearchRuntimeOptions Fast => new() { Profile = SearchProfile.Fast };

    /// <summary>
    /// Balanced profile preset for HNSW.
    /// </summary>
    public static HnswSearchRuntimeOptions Balanced => new() { Profile = SearchProfile.Balanced };

    /// <summary>
    /// HighRecall profile preset for HNSW.
    /// </summary>
    public static HnswSearchRuntimeOptions HighRecall => new() { Profile = SearchProfile.HighRecall };
}

/// <summary>
/// IVFFlat runtime search options.
/// </summary>
public sealed class IvfFlatSearchRuntimeOptions : VectorSearchRuntimeOptions
{
    /// <summary>
    /// Optional IVFFlat probes override for this request.
    /// </summary>
    public int? Probes { get; init; }

    /// <summary>
    /// Fast profile preset for IVFFlat.
    /// </summary>
    public static IvfFlatSearchRuntimeOptions Fast => new() { Profile = SearchProfile.Fast };

    /// <summary>
    /// Balanced profile preset for IVFFlat.
    /// </summary>
    public static IvfFlatSearchRuntimeOptions Balanced => new() { Profile = SearchProfile.Balanced };

    /// <summary>
    /// HighRecall profile preset for IVFFlat.
    /// </summary>
    public static IvfFlatSearchRuntimeOptions HighRecall => new() { Profile = SearchProfile.HighRecall };
}
