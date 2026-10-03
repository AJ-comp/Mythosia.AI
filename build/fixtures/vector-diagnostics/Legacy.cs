// Compiled against the published Rag.Abstractions 6.4.0 contract, never against project references.
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LegacyDiagnosticsFixture;

public abstract class Probe
{
    public CancellationToken LastToken { get; private set; }
    public float[]? LastVector { get; private set; }
    private readonly VectorRecord _record = new() { Id = "legacy", Content = "free parking", Vector = new[] { 1f, 0f } };

    protected Task<IReadOnlyList<VectorRecord>> Records(CancellationToken token)
    {
        LastToken = token;
        token.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<VectorRecord>>(new[] { _record });
    }

    protected Task<IReadOnlyList<VectorSearchResult>> Scores(float[] vector, CancellationToken token)
    {
        LastVector = vector;
        LastToken = token;
        token.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(new[] { new VectorSearchResult(_record, 0.75) });
    }
}

public sealed class ExplicitStore : Probe, IRagDiagnosticsStore
{
    Task<IReadOnlyList<VectorRecord>> IRagDiagnosticsStore.ListAllRecordsAsync(CancellationToken cancellationToken)
        => Records(cancellationToken);
    Task<IReadOnlyList<VectorSearchResult>> IRagDiagnosticsStore.ScoredListAsync(float[] queryVector, CancellationToken cancellationToken)
        => Scores(queryVector, cancellationToken);
}

public sealed class ImplicitStore : Probe, IRagDiagnosticsStore
{
    public Task<IReadOnlyList<VectorRecord>> ListAllRecordsAsync(CancellationToken cancellationToken = default)
        => Records(cancellationToken);
    public Task<IReadOnlyList<VectorSearchResult>> ScoredListAsync(float[] queryVector, CancellationToken cancellationToken = default)
        => Scores(queryVector, cancellationToken);
}

public sealed class MixedStore : Probe, IVectorStore, IRagDiagnosticsStore
{
    public Task<IReadOnlyList<VectorRecord>> ListAllRecordsAsync(CancellationToken token = default)
        => throw new InvalidOperationException("The public helper must not replace legacy diagnostic dispatch.");
    public Task<IReadOnlyList<VectorSearchResult>> ScoredListAsync(float[] vector, CancellationToken token = default)
        => throw new InvalidOperationException("The public helper must not replace legacy diagnostic dispatch.");

    Task<IReadOnlyList<VectorRecord>> IRagDiagnosticsStore.ListAllRecordsAsync(CancellationToken token) => Records(token);
    Task<IReadOnlyList<VectorSearchResult>> IRagDiagnosticsStore.ScoredListAsync(float[] vector, CancellationToken token) => Scores(vector, token);

    public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public static class OldCaller
{
    // These call sites retain references to the old interface's original declared members.
    public static Task<IReadOnlyList<VectorRecord>> Read(IRagDiagnosticsStore store, CancellationToken token)
        => store.ListAllRecordsAsync(token);
    public static Task<IReadOnlyList<VectorSearchResult>> Score(IRagDiagnosticsStore store, float[] vector, CancellationToken token)
        => store.ScoredListAsync(vector, token);
}
