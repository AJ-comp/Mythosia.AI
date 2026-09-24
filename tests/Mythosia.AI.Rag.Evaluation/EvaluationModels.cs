namespace Mythosia.AI.Rag.Evaluation;

/// <summary>Versioned retrieval evaluation data. This project is test infrastructure, not a product package.</summary>
public sealed record EvaluationDataset
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = "";
    public string Version { get; init; } = "1";
    public string Description { get; init; } = "";
    public List<EvaluationDocument> Documents { get; init; } = [];
    public List<EvaluationCase> Cases { get; init; } = [];
}

public sealed record EvaluationDocument
{
    public string Id { get; init; } = "";
    public string? Path { get; init; }
    public string? Text { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}

public sealed record EvaluationCase
{
    public string Id { get; init; } = "";
    public string Query { get; init; } = "";
    public string Category { get; init; } = "uncategorized";
    public string? Language { get; init; }
    public Dictionary<string, int> Judgments { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string>? Filter { get; init; }
}

public sealed record LoadedDocument(string Id, string Text, IReadOnlyDictionary<string, string> Metadata, string? SourcePath);
public sealed record LoadedDataset(EvaluationDataset Dataset, IReadOnlyList<LoadedDocument> Documents, string Fingerprint);
public sealed record RankedDocument(string DocumentId, double Score = 0);
public sealed record EvaluationMetricOptions(int RecallK = 5, int RankK = 10);

/// <summary>Unanswerable cases have null relevance metrics, rather than artificially perfect scores.</summary>
public sealed record CaseMetrics
{
    public int RelevantDocumentCount { get; init; }
    public int RetrievedDocumentCount { get; init; }
    public double? RecallAtK { get; init; }
    public double? ReciprocalRank { get; init; }
    public double? NdcgAtK { get; init; }
    public double? HitAtK { get; init; }
    public bool? NoAnswerFalsePositive { get; init; }
}

public sealed record CaseEvaluation(string CaseId, string Category, string? Language, CaseMetrics Metrics);

/// <summary>Macro averages over answerable cases; false-positive rate over unanswerable cases only.</summary>
public sealed record MetricSummary
{
    public int CaseCount { get; init; }
    public int AnswerableCaseCount { get; init; }
    public int NoAnswerCaseCount { get; init; }
    public double? RecallAtK { get; init; }
    public double? MrrAtK { get; init; }
    public double? NdcgAtK { get; init; }
    public double? HitAtK { get; init; }
    public double? NoAnswerFalsePositiveRate { get; init; }
}

public sealed record MetricBaseline
{
    public string CompatibilityFingerprint { get; init; } = "";
    public Dictionary<string, MetricSummary> Methods { get; init; } = new(StringComparer.Ordinal);
}

public sealed record MetricDifference(string Method, string Metric, double Baseline, double Current, double Degradation, bool Passed);
public sealed record RegressionComparison(bool IsCompatible, bool Passed, string? IncompatibilityReason, IReadOnlyList<MetricDifference> Differences);
