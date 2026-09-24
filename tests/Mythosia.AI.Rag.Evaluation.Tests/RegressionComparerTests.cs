using Mythosia.AI.Rag.Evaluation;

namespace Mythosia.AI.Rag.Evaluation.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RegressionComparerTests
{
    [TestMethod]
    public void AbsoluteTolerance_AcceptsBoundaryAndRejectsLargerDegradation()
    {
        var baseline = Baseline(.9);
        Assert.IsTrue(RegressionComparer.Compare(baseline, Baseline(.88)).Passed);
        var failed = RegressionComparer.Compare(baseline, Baseline(.879));
        Assert.IsTrue(failed.IsCompatible);
        Assert.IsFalse(failed.Passed);
        Assert.AreEqual(4, failed.Differences.Count(item => !item.Passed));
    }

    [TestMethod]
    public void Improvements_PassEvenWithZeroRegressionTolerance()
    {
        Assert.IsTrue(RegressionComparer.Compare(Baseline(.5), Baseline(.9), 0).Passed);
    }

    [TestMethod]
    public void FalsePositiveRate_HasOppositeRegressionDirection()
    {
        var baseline = WithFalsePositives(.1);
        Assert.IsTrue(RegressionComparer.Compare(baseline, WithFalsePositives(.05)).Passed);
        var failed = RegressionComparer.Compare(baseline, WithFalsePositives(.13));
        Assert.IsFalse(failed.Passed);
        Assert.AreEqual(nameof(MetricSummary.NoAnswerFalsePositiveRate), failed.Differences.Single(item => !item.Passed).Metric);
    }

    [TestMethod]
    public void ChangedOrMissingFingerprint_FailsClosedEvenWhenAllMetricsImprove()
    {
        var changed = Baseline(1) with { CompatibilityFingerprint = "different" };
        var result = RegressionComparer.Compare(Baseline(.2), changed);
        Assert.IsFalse(result.IsCompatible);
        Assert.IsFalse(result.Passed);
        Assert.AreEqual(0, result.Differences.Count);
        Assert.IsFalse(RegressionComparer.Compare(Baseline(.2) with { CompatibilityFingerprint = "" }, Baseline(1) with { CompatibilityFingerprint = "" }).Passed);
    }

    [TestMethod]
    public void ChangedMethodSetsAndCaseCounts_FailClosed()
    {
        var renamed = Baseline(.9) with { Methods = new() { ["new-method"] = Summary(.9) } };
        Assert.IsFalse(RegressionComparer.Compare(Baseline(.9), renamed).IsCompatible);
        var fewerCases = Baseline(.9) with { Methods = new() { ["method"] = Summary(.9) with { CaseCount = 1, AnswerableCaseCount = 1 } } };
        Assert.IsFalse(RegressionComparer.Compare(Baseline(.9), fewerCases).IsCompatible);
    }

    [TestMethod]
    public void IncompleteOrNonfiniteMetrics_CannotSilentlyPass()
    {
        foreach (var broken in new[] { Summary(.9) with { RecallAtK = null }, Summary(.9) with { MrrAtK = double.NaN }, Summary(.9) with { NdcgAtK = 1.1 } })
        {
            var current = Baseline(.9) with { Methods = new() { ["method"] = broken } };
            Assert.IsFalse(RegressionComparer.Compare(Baseline(.9), current).Passed);
        }
    }

    [TestMethod]
    public void EntirelyUnanswerableRuns_CanCompareFalsePositivesWithoutQualityScores()
    {
        var summary = new MetricSummary { CaseCount = 2, NoAnswerCaseCount = 2, NoAnswerFalsePositiveRate = .5 };
        var baseline = new MetricBaseline { CompatibilityFingerprint = "same", Methods = new() { ["method"] = summary } };
        Assert.IsTrue(RegressionComparer.Compare(baseline, baseline).Passed);
        Assert.AreEqual(1, RegressionComparer.Compare(baseline, baseline).Differences.Count);
    }

    [TestMethod]
    public void InvalidTolerance_IsRejected()
    {
        foreach (var tolerance in new[] { -.1, 1.1, double.NaN })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RegressionComparer.Compare(Baseline(.9), Baseline(.9), tolerance));
    }

    private static MetricSummary Summary(double value) => new()
    {
        CaseCount = 2, AnswerableCaseCount = 2, RecallAtK = value, MrrAtK = value, NdcgAtK = value, HitAtK = value
    };
    private static MetricBaseline Baseline(double value) => new()
    {
        CompatibilityFingerprint = "same", Methods = new() { ["method"] = Summary(value) }
    };
    private static MetricBaseline WithFalsePositives(double value) => Baseline(.9) with
    {
        Methods = new() { ["method"] = Summary(.9) with { CaseCount = 3, NoAnswerCaseCount = 1, NoAnswerFalsePositiveRate = value } }
    };
}
