using Mythosia.VectorDb;

namespace Mythosia.AI.Rag.Search.Pixie;

/// <summary>
/// An experimental in-memory store combining dense cosine search with PIXIE sparse dot-product search.
/// Documents and their inverted index are published atomically; searches use one consistent snapshot.
/// This store has no persistence and does not own or dispose the supplied encoder.
/// </summary>
public sealed class PixieInMemoryStore : IVectorStore, ITextSearchStore, IConfigurableHybridSearchStore
{
    private readonly Func<string, CancellationToken, Task<PixieSparseVector>> _encodeQuery;
    private readonly Func<string, CancellationToken, Task<PixieSparseVector>> _encodeDocument;
    private readonly object _writeLock = new();
    private State _state = State.Empty;

    /// <summary>Creates a store using a caller-owned encoder for both documents and queries.</summary>
    public PixieInMemoryStore(PixieSparseEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        _encodeQuery = encoder.EncodeQueryAsync;
        _encodeDocument = encoder.EncodeDocumentAsync;
    }

    internal PixieInMemoryStore(
        Func<string, CancellationToken, Task<PixieSparseVector>> encodeQuery,
        Func<string, CancellationToken, Task<PixieSparseVector>> encodeDocument)
    {
        _encodeQuery = encodeQuery ?? throw new ArgumentNullException(nameof(encodeQuery));
        _encodeDocument = encodeDocument ?? throw new ArgumentNullException(nameof(encodeDocument));
    }

    /// <summary>Encodes and atomically inserts or replaces one record.</summary>
    public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
        => UpsertBatchAsync(new[] { record }, cancellationToken);

    /// <summary>
    /// Encodes the entire batch before committing any changes. Duplicate IDs use the last record.
    /// Input vectors and metadata are copied before encoding. Failure or cancellation preserves existing records.
    /// </summary>
    public async Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        var entries = await PrepareAsync(records, cancellationToken).ConfigureAwait(false);
        Commit(entries, null, cancellationToken);
    }

    /// <summary>
    /// Atomically replaces records matching the metadata filter, including when the replacement is empty.
    /// Replacement records must match that filter and cannot overwrite an ID outside it.
    /// </summary>
    public async Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var selection = FilterSnapshot.Create(filter);
        var entries = await PrepareAsync(records, cancellationToken).ConfigureAwait(false);
        if (entries.Values.Any(entry => !selection.Matches(entry.Record)))
            throw new ArgumentException("Replacement records must match the replacement metadata filter.", nameof(records));
        Commit(entries, selection, cancellationToken);
    }

    /// <summary>Returns an independent copy of the record, if it matches the metadata filter.</summary>
    public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(id);
        var selection = FilterSnapshot.Create(filter);
        var state = Volatile.Read(ref _state);
        return Task.FromResult(state.Entries.TryGetValue(id, out var entry) && selection.Matches(entry.Record)
            ? Copy(entry.Record) : null);
    }

    /// <summary>Returns independent copies of matching records from a single snapshot.</summary>
    public Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids, VectorFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(ids);
        var selection = FilterSnapshot.Create(filter);
        var state = Volatile.Read(ref _state);
        var result = new List<VectorRecord>();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(id);
            if (state.Entries.TryGetValue(id, out var entry) && selection.Matches(entry.Record))
                result.Add(Copy(entry.Record));
        }
        return Task.FromResult<IReadOnlyList<VectorRecord>>(result);
    }

    /// <summary>Atomically deletes a matching record and its sparse postings.</summary>
    public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        var selection = FilterSnapshot.Create(filter);
        Mutate(entries =>
        {
            if (entries.TryGetValue(id, out var entry) && selection.Matches(entry.Record))
                entries.Remove(id);
        }, cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>Atomically deletes all records matching the metadata filter.</summary>
    public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var selection = FilterSnapshot.Create(filter);
        Mutate(entries => RemoveMatching(entries, selection, cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>Counts matching records. MinScore is not meaningful for counting and is ignored.</summary>
    public Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = FilterSnapshot.Create(filter);
        long count = 0;
        foreach (var entry in Volatile.Read(ref _state).Entries.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selection.Matches(entry.Record)) count++;
        }
        return Task.FromResult(count);
    }

    /// <summary>Validates cancellation; there is no external connection.</summary>
    public Task VerifyConnectionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Searches nonempty dense vectors by cosine similarity. Empty stored vectors are sparse-only records.
    /// Nonempty vectors must share one dimension; invalid query dimensions are rejected.
    /// </summary>
    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 5,
        VectorFilter? filter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTopK(topK);
        var query = CopyDenseQuery(queryVector);
        var selection = FilterSnapshot.Create(filter);
        return Task.FromResult(DenseSearch(Volatile.Read(ref _state), query, topK, selection,
            selection.MinScore, cancellationToken));
    }

    /// <summary>Searches the inverted index by sparse dot product without a dense query embedding.</summary>
    public async Task<IReadOnlyList<VectorSearchResult>> TextSearchAsync(string query, int topK = 5,
        VectorFilter? filter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);
        ValidateTopK(topK);
        var selection = FilterSnapshot.Create(filter);
        var state = Volatile.Read(ref _state);
        if (string.IsNullOrWhiteSpace(query) || state.Entries.Count == 0)
            return Array.Empty<VectorSearchResult>();
        var sparse = await _encodeQuery(query, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return SparseSearch(state, sparse, topK, selection, selection.MinScore, cancellationToken);
    }

    /// <summary>Combines dense and PIXIE search with default weighted reciprocal rank fusion.</summary>
    public Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(float[] denseVector, string query,
        int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        => HybridSearchAsync(denseVector, query, new HybridSearchOptions(), topK, filter, cancellationToken);

    /// <summary>
    /// Combines active search legs using normalized weighted RRF against the same snapshot.
    /// Zero dense weight skips dense validation; full dense weight skips PIXIE query encoding.
    /// MinScore applies to the final fusion score, including when only one leg is active.
    /// </summary>
    public async Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(float[] denseVector, string query,
        HybridSearchOptions options, int topK = 5, VectorFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Snapshot();
        var count = settings.GetCandidateCount(topK);
        var selection = FilterSnapshot.Create(filter);
        var state = Volatile.Read(ref _state);
        IReadOnlyList<VectorSearchResult> dense = Array.Empty<VectorSearchResult>();
        IReadOnlyList<VectorSearchResult> sparse = Array.Empty<VectorSearchResult>();
        if (settings.VectorWeight > 0)
            dense = DenseSearch(state, CopyDenseQuery(denseVector), count, selection, null, cancellationToken);
        if (settings.VectorWeight < 1)
        {
            ArgumentNullException.ThrowIfNull(query);
            if (!string.IsNullOrWhiteSpace(query) && state.Entries.Count != 0)
            {
                var encoded = await _encodeQuery(query, cancellationToken).ConfigureAwait(false);
                sparse = SparseSearch(state, encoded, count, selection, null, cancellationToken);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return HybridSearchFusion.Merge(dense, sparse, settings, topK, selection.MinScore);
    }

    private async Task<Dictionary<string, Entry>> PrepareAsync(IEnumerable<VectorRecord> records, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(records);
        var copies = new Dictionary<string, VectorRecord>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(record);
            if (string.IsNullOrEmpty(record.Id)) throw new ArgumentException("A record ID must not be empty.", nameof(records));
            ArgumentNullException.ThrowIfNull(record.Vector);
            ArgumentNullException.ThrowIfNull(record.Content);
            ArgumentNullException.ThrowIfNull(record.Metadata);
            var copy = Copy(record);
            ValidateFinite(copy.Vector);
            copies[copy.Id] = copy;
        }
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var record in copies.Values)
        {
            ct.ThrowIfCancellationRequested();
            var vector = string.IsNullOrWhiteSpace(record.Content) ? PixieSparseVector.Empty
                : await _encodeDocument(record.Content, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            entries.Add(record.Id, new Entry(record, vector ?? throw new InvalidOperationException("The encoder returned null.")));
        }
        return entries;
    }

    private void Commit(Dictionary<string, Entry> additions, FilterSnapshot? replacement, CancellationToken ct)
        => Mutate(entries =>
        {
            if (replacement != null)
            {
                foreach (var id in additions.Keys)
                    if (entries.TryGetValue(id, out var existing) && !replacement.Matches(existing.Record))
                        throw new InvalidOperationException("A replacement ID belongs to a record outside the metadata filter.");
                RemoveMatching(entries, replacement, ct);
            }
            foreach (var entry in additions)
            {
                ct.ThrowIfCancellationRequested();
                entries[entry.Key] = entry.Value;
            }
        }, ct);

    private void Mutate(Action<Dictionary<string, Entry>> mutate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_writeLock)
        {
            ct.ThrowIfCancellationRequested();
            var entries = new Dictionary<string, Entry>(_state.Entries, StringComparer.Ordinal);
            mutate(entries);
            var next = State.Create(entries, ct);
            ct.ThrowIfCancellationRequested();
            Volatile.Write(ref _state, next);
        }
    }

    private static void RemoveMatching(Dictionary<string, Entry> entries, FilterSnapshot filter, CancellationToken ct)
    {
        foreach (var id in entries.Keys.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            if (filter.Matches(entries[id].Record)) entries.Remove(id);
        }
    }

    private static IReadOnlyList<VectorSearchResult> DenseSearch(State state, float[] query, int topK,
        FilterSnapshot filter, double? minScore, CancellationToken ct)
    {
        if (state.DenseDimensions.HasValue && query.Length != state.DenseDimensions)
            throw new ArgumentException("The query dimension does not match the stored dense vectors.", nameof(query));
        var results = new List<VectorSearchResult>();
        foreach (var entry in state.Entries.Values)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Record.Vector.Length == 0 || !filter.Matches(entry.Record)) continue;
            double dot = 0, normQ = 0, normD = 0;
            for (var i = 0; i < query.Length; i++)
            {
                dot += (double)query[i] * entry.Record.Vector[i];
                normQ += (double)query[i] * query[i];
                normD += (double)entry.Record.Vector[i] * entry.Record.Vector[i];
            }
            var score = normQ == 0 || normD == 0 ? 0 : dot / Math.Sqrt(normQ * normD);
            if (!minScore.HasValue || score >= minScore.Value)
                results.Add(new VectorSearchResult(entry.Record, score));
        }
        return Rank(results, topK, ct);
    }

    private static IReadOnlyList<VectorSearchResult> SparseSearch(State state, PixieSparseVector query, int topK,
        FilterSnapshot filter, double? minScore, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var eligibility = new Dictionary<string, bool>(StringComparer.Ordinal);
        for (var i = 0; i < query.Indices.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!state.Postings.TryGetValue(query.Indices[i], out var posting)) continue;
            foreach (var match in posting)
            {
                ct.ThrowIfCancellationRequested();
                if (!eligibility.TryGetValue(match.Key, out var eligible))
                    eligibility[match.Key] = eligible = filter.Matches(state.Entries[match.Key].Record);
                if (!eligible) continue;
                scores.TryGetValue(match.Key, out var oldScore);
                scores[match.Key] = oldScore + (double)query.Values[i] * match.Value;
            }
        }
        return Rank(scores.Where(pair => !minScore.HasValue || pair.Value >= minScore.Value)
            .Select(pair => new VectorSearchResult(state.Entries[pair.Key].Record, pair.Value)), topK, ct);
    }

    private static IReadOnlyList<VectorSearchResult> Rank(IEnumerable<VectorSearchResult> results, int topK, CancellationToken ct)
    {
        var ranked = results.OrderByDescending(result => result.Score)
            .ThenBy(result => result.Record.Id, StringComparer.Ordinal).Take(topK)
            .Select(result => new VectorSearchResult(Copy(result.Record), result.Score)).ToArray();
        ct.ThrowIfCancellationRequested();
        return ranked;
    }

    private static float[] CopyDenseQuery(float[] query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0) throw new ArgumentException("A dense query vector must not be empty.", nameof(query));
        var copy = (float[])query.Clone();
        ValidateFinite(copy);
        return copy;
    }

    private static void ValidateFinite(float[] vector)
    {
        if (vector.Any(value => !float.IsFinite(value)))
            throw new ArgumentException("Dense vectors must contain only finite values.", nameof(vector));
    }

    private static void ValidateTopK(int topK)
    {
        if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK));
    }

    private static VectorRecord Copy(VectorRecord record) => new(record.Id, (float[])record.Vector.Clone(), record.Content)
    {
        Metadata = new Dictionary<string, string>(record.Metadata, StringComparer.Ordinal)
    };

    private sealed record Entry(VectorRecord Record, PixieSparseVector Sparse);

    private sealed record State(Dictionary<string, Entry> Entries,
        Dictionary<int, Dictionary<string, float>> Postings, int? DenseDimensions)
    {
        internal static readonly State Empty = new(new(StringComparer.Ordinal), new(), null);

        internal static State Create(Dictionary<string, Entry> entries, CancellationToken ct)
        {
            int? dimensions = null;
            var postings = new Dictionary<int, Dictionary<string, float>>();
            foreach (var entry in entries.Values)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Record.Vector.Length != 0)
                {
                    dimensions ??= entry.Record.Vector.Length;
                    if (dimensions != entry.Record.Vector.Length)
                        throw new ArgumentException("All nonempty dense vectors in a store must have the same dimension.");
                }
                for (var i = 0; i < entry.Sparse.Indices.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var token = entry.Sparse.Indices[i];
                    if (!postings.TryGetValue(token, out var posting))
                        postings[token] = posting = new Dictionary<string, float>(StringComparer.Ordinal);
                    posting.Add(entry.Record.Id, entry.Sparse.Values[i]);
                }
            }
            return new State(entries, postings, dimensions);
        }
    }

    // Compile filters before the first await: caller-owned value arrays and condition lists
    // cannot change which tenant a pending operation selects.
    private sealed record FilterSnapshot(Func<VectorRecord, bool> Matches, double? MinScore)
    {
        internal static FilterSnapshot Create(VectorFilter? filter)
            => filter == null ? new(_ => true, null) : new(CompileGroup(filter.Conditions, FilterLogic.And), filter.MinScore);

        private static Func<VectorRecord, bool> CompileGroup(IReadOnlyList<FilterCondition> conditions, FilterLogic logic)
        {
            var predicates = conditions.Select(Compile).ToArray();
            return logic switch
            {
                FilterLogic.And => record => predicates.All(predicate => predicate(record)),
                FilterLogic.Or => record => predicates.Any(predicate => predicate(record)),
                _ => throw new NotSupportedException("Unknown filter group logic.")
            };
        }

        private static Func<VectorRecord, bool> Compile(FilterCondition condition)
        {
            if (condition is FilterGroup group) return CompileGroup(group.Conditions, group.Logic);
            if (condition is not MetadataCondition leaf) throw new NotSupportedException("Unknown metadata filter condition.");
            ArgumentNullException.ThrowIfNull(leaf.Key);
            var key = leaf.Key;
            var value = leaf.Value;
            var values = leaf.Values?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
            if (!Enum.IsDefined(leaf.Operator)) throw new NotSupportedException("Unknown metadata filter operator.");
            return record =>
            {
                var exists = record.Metadata.TryGetValue(key, out var actual);
                if (leaf.Operator == FilterOperator.Exists) return exists;
                if (leaf.Operator == FilterOperator.NotExists) return !exists;
                if (!exists) return false;
                return leaf.Operator switch
                {
                    FilterOperator.Eq => string.Equals(actual, value, StringComparison.Ordinal),
                    FilterOperator.Ne => !string.Equals(actual, value, StringComparison.Ordinal),
                    FilterOperator.In => values.Contains(actual!),
                    FilterOperator.NotIn => !values.Contains(actual!),
                    FilterOperator.Gt => string.Compare(actual, value, StringComparison.Ordinal) > 0,
                    FilterOperator.Gte => string.Compare(actual, value, StringComparison.Ordinal) >= 0,
                    FilterOperator.Lt => string.Compare(actual, value, StringComparison.Ordinal) < 0,
                    FilterOperator.Lte => string.Compare(actual, value, StringComparison.Ordinal) <= 0,
                    FilterOperator.Like => actual != null && Like(actual, value ?? string.Empty),
                    _ => false
                };
            };
        }

        private static bool Like(string text, string pattern)
        {
            int t = 0, p = 0, wildcard = -1, retry = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length && pattern[p] == '%') { wildcard = p++; retry = t; }
                else if (p < pattern.Length && (pattern[p] == '_' || pattern[p] == text[t])) { p++; t++; }
                else if (wildcard >= 0) { p = wildcard + 1; t = ++retry; }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '%') p++;
            return p == pattern.Length;
        }
    }
}
