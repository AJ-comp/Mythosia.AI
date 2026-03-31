using System;

namespace Mythosia.VectorDb.Postgres;

/// <summary>
/// Base type for vector index settings.
/// </summary>
public abstract class VectorIndexOptions
{
    /// <summary>
    /// Index algorithm represented by this settings object.
    /// </summary>
    public abstract IndexType IndexType { get; }

    internal abstract void Validate();
}

/// <summary>
/// HNSW index settings.
/// </summary>
public sealed class HnswIndexOptions : VectorIndexOptions
{
    public override IndexType IndexType => IndexType.Hnsw;

    /// <summary>
    /// HNSW: max connections per node. Default: 16.
    /// </summary>
    public int M { get; set; } = 16;

    /// <summary>
    /// HNSW: search scope during index build. Default: 64.
    /// </summary>
    public int EfConstruction { get; set; } = 64;

    /// <summary>
    /// HNSW runtime ef_search default for search. Default: 40.
    /// </summary>
    public int EfSearch { get; set; } = 40;

    internal override void Validate()
    {
        if (M <= 0)
            throw new ArgumentException("HnswIndexOptions.M must be greater than 0.", nameof(M));

        if (EfConstruction <= 0)
            throw new ArgumentException("HnswIndexOptions.EfConstruction must be greater than 0.", nameof(EfConstruction));

        if (EfSearch <= 0)
            throw new ArgumentException("HnswIndexOptions.EfSearch must be greater than 0.", nameof(EfSearch));
    }
}

/// <summary>
/// IVFFlat index settings.
/// </summary>
public sealed class IvfFlatIndexOptions : VectorIndexOptions
{
    public override IndexType IndexType => IndexType.IvfFlat;

    /// <summary>
    /// Number of IVF lists for the ivfflat index. Default: 100.
    /// </summary>
    public int Lists { get; set; } = 100;

    /// <summary>
    /// IVFFlat runtime probes default for search. Default: 10.
    /// </summary>
    public int Probes { get; set; } = 10;

    internal override void Validate()
    {
        if (Lists <= 0)
            throw new ArgumentException("IvfFlatIndexOptions.Lists must be greater than 0.", nameof(Lists));

        if (Probes <= 0)
            throw new ArgumentException("IvfFlatIndexOptions.Probes must be greater than 0.", nameof(Probes));
    }
}

/// <summary>
/// Disables vector index creation and runtime index tuning.
/// </summary>
public sealed class NoIndexOptions : VectorIndexOptions
{
    public override IndexType IndexType => IndexType.None;

    internal override void Validate()
    {
    }
}
