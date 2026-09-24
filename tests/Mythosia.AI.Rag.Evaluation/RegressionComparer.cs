namespace Mythosia.AI.Rag.Evaluation;

public static class RegressionComparer
{
    /// <summary>Uses absolute metric points, not percentages. Different conditions cannot pass a regression gate.</summary>
    public static RegressionComparison Compare(MetricBaseline baseline, MetricBaseline current, double maxRegression = .02)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        if (!double.IsFinite(maxRegression) || maxRegression < 0 || maxRegression > 1)
            throw new ArgumentOutOfRangeException(nameof(maxRegression), "The maximum absolute regression must be between 0 and 1.");
        if (string.IsNullOrWhiteSpace(baseline.CompatibilityFingerprint) ||
            !string.Equals(baseline.CompatibilityFingerprint, current.CompatibilityFingerprint, StringComparison.Ordinal))
            return Incompatible("The run compatibility fingerprints differ or are missing. Dataset content, judgments, methods, and retrieval settings must match.");
        if (baseline.Methods is null || current.Methods is null || baseline.Methods.Count == 0 ||
            !baseline.Methods.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(current.Methods.Keys))
            return Incompatible("The evaluated method sets differ or are empty.");

        var differences = new List<MetricDifference>();
        foreach (var method in baseline.Methods.Keys.Order(StringComparer.Ordinal))
        {
            var before = baseline.Methods[method];
            var after = current.Methods[method];
            if (!ValidSummary(before) || !ValidSummary(after)) return Incompatible($"Method '{method}' contains invalid or incomplete metrics.");
            if (before.CaseCount != after.CaseCount || before.AnswerableCaseCount != after.AnswerableCaseCount || before.NoAnswerCaseCount != after.NoAnswerCaseCount)
                return Incompatible($"Method '{method}' has different evaluated case counts.");

            var pairs = new (string Name, double? Before, double? After, bool LowerIsBetter)[]
            {
                (nameof(MetricSummary.RecallAtK), before.RecallAtK, after.RecallAtK, false),
                (nameof(MetricSummary.MrrAtK), before.MrrAtK, after.MrrAtK, false),
                (nameof(MetricSummary.NdcgAtK), before.NdcgAtK, after.NdcgAtK, false),
                (nameof(MetricSummary.HitAtK), before.HitAtK, after.HitAtK, false),
                (nameof(MetricSummary.NoAnswerFalsePositiveRate), before.NoAnswerFalsePositiveRate, after.NoAnswerFalsePositiveRate, true)
            };
            foreach (var pair in pairs)
            {
                if (pair.Before is null && pair.After is null) continue;
                if (pair.Before is null || pair.After is null) return Incompatible($"Method '{method}' is missing comparable '{pair.Name}' metrics.");
                var degradation = pair.LowerIsBetter ? pair.After.Value - pair.Before.Value : pair.Before.Value - pair.After.Value;
                differences.Add(new MetricDifference(method, pair.Name, pair.Before.Value, pair.After.Value, degradation, degradation <= maxRegression + 1e-12));
            }
        }
        return new RegressionComparison(true, differences.All(item => item.Passed), null, differences);
    }

    private static bool ValidSummary(MetricSummary? summary)
    {
        if (summary is null || summary.CaseCount <= 0 || summary.AnswerableCaseCount < 0 || summary.NoAnswerCaseCount < 0 ||
            (long)summary.AnswerableCaseCount + summary.NoAnswerCaseCount != summary.CaseCount) return false;
        var quality = new[] { summary.RecallAtK, summary.MrrAtK, summary.NdcgAtK, summary.HitAtK };
        if (quality.Any(value => !ValidValue(value) || value.HasValue != (summary.AnswerableCaseCount > 0))) return false;
        return ValidValue(summary.NoAnswerFalsePositiveRate) && summary.NoAnswerFalsePositiveRate.HasValue == (summary.NoAnswerCaseCount > 0);
    }

    private static bool ValidValue(double? value) => value is null || double.IsFinite(value.Value) && value.Value >= 0 && value.Value <= 1;
    private static RegressionComparison Incompatible(string reason) => new(false, false, reason, []);
}
