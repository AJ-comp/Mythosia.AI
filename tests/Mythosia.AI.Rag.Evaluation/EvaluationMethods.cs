using System.Diagnostics;
using System.Collections.ObjectModel;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Evaluation;

/// <summary>Only searchable input reaches an adapter; relevance judgments stay inside the evaluator.</summary>
public sealed record EvaluationQuery(string Id, string Query, string Category, string? Language, IReadOnlyDictionary<string, string>? Filter)
{
    public static EvaluationQuery From(EvaluationCase item) => new(item.Id, item.Query, item.Category, item.Language,
        item.Filter == null ? null : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(item.Filter, StringComparer.Ordinal)));
}

/// <summary>Implement one adapter to evaluate another search engine using the same corpus and judgments.</summary>
public interface IEvaluationMethod : IAsyncDisposable
{
    string Identity { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(EvaluationQuery query, CancellationToken cancellationToken);
}

public sealed class EvaluationMethodRegistry
{
    private readonly Dictionary<string, (bool Dense, Func<EvaluationContext, IEvaluationMethod> Factory)> factories = new(StringComparer.Ordinal);
    public void Register(string id, Func<EvaluationContext, IEvaluationMethod> factory, bool requiresDense = false)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Method ID is required.");
        ArgumentNullException.ThrowIfNull(factory);
        if (!factories.TryAdd(id, (requiresDense, factory))) throw new ArgumentException($"Duplicate method: {id}");
    }
    public bool RequiresDense(string id) => Find(id).Dense;
    public IEvaluationMethod Create(string id, EvaluationContext context) => Find(id).Factory(context);
    private (bool Dense, Func<EvaluationContext, IEvaluationMethod> Factory) Find(string id)
        => factories.TryGetValue(id, out var entry) ? entry : throw new ArgumentException($"Unknown method '{id}'. Available: {string.Join(", ", factories.Keys)}");
    public static EvaluationMethodRegistry CreateDefault()
    {
        var registry = new EvaluationMethodRegistry();
        foreach (var id in new[] { "bm25", "pixie", "dense", "hybrid-bm25", "hybrid-pixie" })
            registry.Register(id, context => new StoreEvaluationMethod(context, id), id is "dense" or "hybrid-bm25" or "hybrid-pixie");
        return registry;
    }
}

public sealed class EvaluationContext : IDisposable
{
    private readonly EvaluationOptions options;
    public EvaluationOptions Options => options with { Methods = options.Methods.ToArray() };
    private readonly IReadOnlyList<VectorRecord> records;
    private readonly IReadOnlyDictionary<string, float[]> queryVectors;
    public IReadOnlyList<VectorRecord> Records => records.Select(Clone).ToArray();
    public IReadOnlyDictionary<string, float[]> QueryVectors => new ReadOnlyDictionary<string, float[]>(queryVectors.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal));
    public float[] GetQueryVector(string id) => queryVectors[id].ToArray();
    public Dictionary<string, double> PreparationMilliseconds { get; } = new();
    private InMemoryVectorStore? baseline;
    private PixieSparseEncoder? encoder;
    private PixieInMemoryStore? pixie;
    public EvaluationContext(EvaluationOptions options, IReadOnlyList<VectorRecord> records, IReadOnlyDictionary<string, float[]> queryVectors)
    {
        this.options = options with { Methods = options.Methods.ToArray() };
        this.records = records.Select(Clone).ToArray();
        this.queryVectors = queryVectors.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
    }
    private static VectorRecord Clone(VectorRecord record) => new(record.Id, record.Vector.ToArray(), record.Content)
        { Metadata = new Dictionary<string, string>(record.Metadata, StringComparer.Ordinal) };

    internal async Task<IVectorStore> GetStoreAsync(bool neural, CancellationToken cancellationToken)
    {
        if (neural)
        {
            if (pixie != null) return pixie;
            var clock = Stopwatch.StartNew();
            encoder = new PixieSparseEncoder(new PixieOptions { ModelDirectory = Options.ModelDirectory, IntraOpThreads = Options.Threads });
            PreparationMilliseconds["pixie-model-load"] = clock.Elapsed.TotalMilliseconds;
            var created = new PixieInMemoryStore(encoder);
            clock.Restart();
            await created.UpsertBatchAsync(Records, cancellationToken);
            PreparationMilliseconds["pixie-index"] = clock.Elapsed.TotalMilliseconds;
            return pixie = created;
        }
        if (baseline != null) return baseline;
        baseline = new InMemoryVectorStore();
        var watch = Stopwatch.StartNew();
        await baseline.UpsertBatchAsync(Records, cancellationToken);
        PreparationMilliseconds["bm25-dense-index"] = watch.Elapsed.TotalMilliseconds;
        return baseline;
    }
    public static VectorFilter? CreateFilter(EvaluationQuery query)
    {
        if (query.Filter == null || query.Filter.Count == 0) return null;
        var filter = new VectorFilter();
        foreach (var item in query.Filter) filter.Where(item.Key, item.Value);
        return filter;
    }
    public void Dispose() { baseline?.Dispose(); encoder?.Dispose(); }
}

internal sealed class StoreEvaluationMethod(EvaluationContext context, string id) : IEvaluationMethod
{
    private IVectorStore? store;
    public string Identity => id.Contains("pixie", StringComparison.Ordinal)
        ? $"{id}/PIXIE/{PixieSparseEncoder.ModelRevision}/uint8" : $"{id}/InMemoryVectorStore";
    public async Task InitializeAsync(CancellationToken cancellationToken)
        => store = await context.GetStoreAsync(id.Contains("pixie", StringComparison.Ordinal), cancellationToken);
    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(EvaluationQuery query, CancellationToken cancellationToken)
    {
        var options = context.Options;
        var filter = EvaluationContext.CreateFilter(query);
        return id switch
        {
            "bm25" or "pixie" => ((ITextSearchStore)store!).TextSearchAsync(query.Query, options.CandidateLimit, filter, cancellationToken),
            "dense" => store!.SearchAsync(context.GetQueryVector(query.Id), options.CandidateLimit, filter, cancellationToken),
            _ => ((IConfigurableHybridSearchStore)store!).HybridSearchAsync(context.GetQueryVector(query.Id), query.Query,
                new HybridSearchOptions { VectorWeight = options.VectorWeight, CandidateMultiplier = options.CandidateMultiplier, RrfK = options.RrfK },
                options.CandidateLimit, filter, cancellationToken)
        };
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask; // The context owns shared indexes and model.
}
