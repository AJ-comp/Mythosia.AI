namespace Mythosia.AI.Rag.Evaluation;

public static class RetrievalMetrics
{
    public static CaseMetrics Evaluate(EvaluationCase item, IEnumerable<RankedDocument> ranking, EvaluationMetricOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(ranking);
        options ??= new EvaluationMetricOptions();
        if (options.RecallK <= 0 || options.RankK <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Metric cutoffs must be positive.");
        if (item.Judgments is null || item.Judgments.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0 || pair.Value > 30))
            throw new ArgumentException("Judgments require nonempty document ids and integer grades from 0 to 30.", nameof(item));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var documents = new List<RankedDocument>();
        foreach (var hit in ranking)
        {
            if (hit is null || string.IsNullOrWhiteSpace(hit.DocumentId) || !double.IsFinite(hit.Score))
                throw new ArgumentException("Ranking entries require a document id and finite score.", nameof(ranking));
            if (seen.Add(hit.DocumentId)) documents.Add(hit);
        }
        var relevantCount = item.Judgments.Count(pair => pair.Value > 0);
        if (relevantCount == 0)
            return new CaseMetrics { RelevantDocumentCount = 0, RetrievedDocumentCount = documents.Count, NoAnswerFalsePositive = documents.Count > 0 };

        int Grade(RankedDocument hit) => item.Judgments.GetValueOrDefault(hit.DocumentId);
        var recallHits = documents.Take(options.RecallK).Count(hit => Grade(hit) > 0);
        var firstRelevantRank = documents.Take(options.RankK).FindIndex(hit => Grade(hit) > 0);
        var dcg = documents.Take(options.RankK).Select((hit, index) => Gain(Grade(hit)) / Math.Log2(index + 2)).Sum();
        var idealDcg = item.Judgments.Values.OrderDescending().Take(options.RankK).Select((grade, index) => Gain(grade) / Math.Log2(index + 2)).Sum();
        return new CaseMetrics
        {
            RelevantDocumentCount = relevantCount,
            RetrievedDocumentCount = documents.Count,
            RecallAtK = (double)recallHits / relevantCount,
            ReciprocalRank = firstRelevantRank < 0 ? 0 : 1.0 / (firstRelevantRank + 1),
            NdcgAtK = dcg / idealDcg,
            HitAtK = recallHits > 0 ? 1 : 0
        };
    }

    public static MetricSummary Aggregate(IEnumerable<CaseEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(evaluations);
        var cases = evaluations.ToArray();
        if (cases.Any(item => item is null || item.Metrics is null)) throw new ArgumentException("Evaluations cannot contain null cases or metrics.", nameof(evaluations));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cases)
        {
            if (string.IsNullOrWhiteSpace(item.CaseId) || !ids.Add(item.CaseId))
                throw new ArgumentException("Each aggregate must contain a case id at most once.", nameof(evaluations));
            var metric = item.Metrics;
            var quality = new[] { metric.RecallAtK, metric.ReciprocalRank, metric.NdcgAtK, metric.HitAtK };
            if (metric.RelevantDocumentCount < 0 || metric.RetrievedDocumentCount < 0 ||
                quality.Any(value => value.HasValue != (metric.RelevantDocumentCount > 0) ||
                    value is { } number && (!double.IsFinite(number) || number < 0 || number > 1)) ||
                metric.NoAnswerFalsePositive.HasValue != (metric.RelevantDocumentCount == 0))
                throw new ArgumentException($"Case '{item.CaseId}' contains incomplete or invalid metrics.", nameof(evaluations));
        }
        var answerable = cases.Where(item => item.Metrics.RelevantDocumentCount > 0).ToArray();
        var noAnswer = cases.Where(item => item.Metrics.RelevantDocumentCount == 0).ToArray();
        return new MetricSummary
        {
            CaseCount = cases.Length,
            AnswerableCaseCount = answerable.Length,
            NoAnswerCaseCount = noAnswer.Length,
            RecallAtK = Mean(answerable.Select(item => item.Metrics.RecallAtK)),
            MrrAtK = Mean(answerable.Select(item => item.Metrics.ReciprocalRank)),
            NdcgAtK = Mean(answerable.Select(item => item.Metrics.NdcgAtK)),
            HitAtK = Mean(answerable.Select(item => item.Metrics.HitAtK)),
            NoAnswerFalsePositiveRate = Mean(noAnswer.Select(item => item.Metrics.NoAnswerFalsePositive is { } value ? value ? 1.0 : 0.0 : (double?)null))
        };
    }

    public static IReadOnlyDictionary<string, MetricSummary> AggregateByCategory(IEnumerable<CaseEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(evaluations);
        return evaluations.GroupBy(item => item.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Aggregate(group), StringComparer.Ordinal);
    }

    private static double Gain(int grade) => Math.Pow(2, grade) - 1;
    private static double? Mean(IEnumerable<double?> values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return present.Length == 0 ? null : present.Average();
    }

    private static int FindIndex<T>(this IEnumerable<T> items, Func<T, bool> predicate)
    {
        var index = 0;
        foreach (var item in items)
        {
            if (predicate(item)) return index;
            index++;
        }
        return -1;
    }
}
