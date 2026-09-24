using System.Globalization;

namespace Mythosia.AI.Rag.Evaluation;

public sealed record EvaluationOptions
{
    public string Root { get; init; } = Environment.CurrentDirectory;
    public string? DataRoot { get; init; }
    public string Dataset { get; init; } = "";
    public string Output { get; init; } = "";
    public string ModelDirectory { get; init; } = "";
    public string[] Methods { get; init; } = ["bm25", "pixie"];
    public string Dense { get; init; } = "none";
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";
    public int Dimensions { get; init; } = 1536;
    public string EmbeddingCache { get; init; } = "";
    public int Threads { get; init; } = 4;
    public int ChunkSize { get; init; } = 450;
    public int ChunkOverlap { get; init; } = 50;
    public int CandidateLimit { get; init; } = 50;
    public int RecallK { get; init; } = 5;
    public int RankK { get; init; } = 10;
    public float VectorWeight { get; init; } = .5f;
    public int CandidateMultiplier { get; init; } = 2;
    public int RrfK { get; init; } = 60;
    public int Warmup { get; init; } = 1;
    public int Repeat { get; init; } = 1;
    public string? Baseline { get; init; }
    public double MaxRegression { get; init; } = .02;

    public static EvaluationOptions Parse(string[] args)
    {
        var allowed = new HashSet<string>("root data-root dataset cases output model-directory methods dense embedding-model dimensions embedding-cache threads chunk-size chunk-overlap candidate-limit recall-k rank-k vector-weight candidate-multiplier rrf-k warmup repeat baseline max-regression".Split(' '));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--") || !allowed.Contains(args[i][2..]) || !values.TryAdd(args[i][2..], args[i + 1]))
                throw new ArgumentException($"Unknown, duplicate or incomplete option: {args[i]}");
        }
        if (values.ContainsKey("dataset") && values.ContainsKey("cases")) throw new ArgumentException("Use either --dataset or --cases.");
        if (values.ContainsKey("max-regression") && !values.ContainsKey("baseline")) throw new ArgumentException("--max-regression requires --baseline.");
        string Str(string key, string fallback) => values.GetValueOrDefault(key, fallback);
        int Int(string key, int fallback) => int.Parse(Str(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        double Num(string key, double fallback) => double.Parse(Str(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        var root = Path.GetFullPath(Str("root", Environment.CurrentDirectory));
        string Full(string path) => Path.GetFullPath(path, root);
        var options = new EvaluationOptions
        {
            Root = root,
            DataRoot = Full(Str("data-root", root)),
            Dataset = Full(Str("dataset", Str("cases", "tests/Mythosia.AI.Rag.Evaluation/Datasets/repository-docs.json"))),
            Output = Full(Str("output", $"artifacts/retrieval-evaluation/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}")),
            ModelDirectory = Full(Str("model-directory", "src/rag/Mythosia.AI.Rag.Search.Pixie/models/pixie")),
            Methods = Str("methods", "bm25,pixie").Split(',', StringSplitOptions.TrimEntries),
            Dense = Str("dense", "none"), EmbeddingModel = Str("embedding-model", "text-embedding-3-small"),
            Dimensions = Int("dimensions", 1536), EmbeddingCache = Full(Str("embedding-cache", "artifacts/retrieval-evaluation/cache")),
            Threads = Int("threads", 4), ChunkSize = Int("chunk-size", 450), ChunkOverlap = Int("chunk-overlap", 50),
            CandidateLimit = Int("candidate-limit", 50), RecallK = Int("recall-k", 5), RankK = Int("rank-k", 10),
            VectorWeight = (float)Num("vector-weight", .5), CandidateMultiplier = Int("candidate-multiplier", 2), RrfK = Int("rrf-k", 60),
            Warmup = Int("warmup", 1), Repeat = Int("repeat", 1), MaxRegression = Num("max-regression", .02),
            Baseline = values.TryGetValue("baseline", out var baseline) ? Full(baseline) : null
        };
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (Methods.Length == 0 || Methods.Any(string.IsNullOrWhiteSpace) || Methods.Distinct(StringComparer.Ordinal).Count() != Methods.Length)
            throw new ArgumentException("Methods must be distinct nonempty IDs.");
        if (Dense is not ("none" or "local-hash" or "openai")) throw new ArgumentException("Unknown dense mode.");
        if (string.IsNullOrWhiteSpace(EmbeddingModel) || Dimensions < 1 || Dimensions > 65536 || Threads < 0 || Threads > 128
            || ChunkSize < 1 || ChunkOverlap < 0 || ChunkOverlap >= ChunkSize || CandidateLimit < 1 || CandidateLimit > 100000
            || RecallK < 1 || RankK < 1 || CandidateLimit < Math.Max(RecallK, RankK)
            || !float.IsFinite(VectorWeight) || VectorWeight < 0 || VectorWeight > 1
            || CandidateMultiplier < 1 || CandidateMultiplier > 100 || (long)CandidateLimit * CandidateMultiplier > int.MaxValue
            || RrfK < 1 || Warmup < 0 || Warmup > 100 || Repeat < 1 || Repeat > 1000
            || !double.IsFinite(MaxRegression) || MaxRegression < 0 || MaxRegression > 1)
            throw new ArgumentException("Invalid evaluation limits, chunking, metric cutoffs or regression tolerance.");
    }
}
