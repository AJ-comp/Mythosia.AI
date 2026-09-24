using Mythosia.AI.Rag.Evaluation;

namespace Mythosia.AI.Rag.Evaluation.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RetrievalMetricsTests
{
    [TestMethod]
    public void GradedMetrics_DeduplicateDocumentsBeforeApplyingCutoffs()
    {
        var item = Case(("a", 3), ("b", 2), ("c", 1));
        var actual = RetrievalMetrics.Evaluate(item,
            [new("wrong"), new("b"), new("b"), new("a"), new("c")], new(2, 3));

        Assert.AreEqual(1.0 / 3, actual.RecallAtK!.Value, 1e-12);
        Assert.AreEqual(.5, actual.ReciprocalRank!.Value, 1e-12);
        Assert.AreEqual((3 / Math.Log2(3) + 7.0 / 2) / (7 + 3 / Math.Log2(3) + .5), actual.NdcgAtK!.Value, 1e-12);
        Assert.AreEqual(1, actual.HitAtK);
        Assert.AreEqual(4, actual.RetrievedDocumentCount);
        Assert.IsNull(actual.NoAnswerFalsePositive);
    }

    [TestMethod]
    public void UnjudgedAndGradeZeroHits_DoNotReceiveCredit()
    {
        var actual = RetrievalMetrics.Evaluate(Case(("a", 1), ("zero", 0)), [new("unjudged"), new("zero")]);
        Assert.AreEqual(0, actual.RecallAtK);
        Assert.AreEqual(0, actual.ReciprocalRank);
        Assert.AreEqual(0, actual.NdcgAtK);
        Assert.AreEqual(0, actual.HitAtK);
    }

    [TestMethod]
    public void IdealGradedOrder_ScoresOne_AndBadOrderScoresLess()
    {
        var item = Case(("low", 1), ("high", 3));
        var ideal = RetrievalMetrics.Evaluate(item, [new("high"), new("low")]);
        var reversed = RetrievalMetrics.Evaluate(item, [new("low"), new("high")]);
        Assert.AreEqual(1, ideal.NdcgAtK);
        Assert.IsTrue(reversed.NdcgAtK < ideal.NdcgAtK);
        Assert.AreEqual(ideal.RecallAtK, reversed.RecallAtK);
    }

    [TestMethod]
    public void FirstRelevantBeyondRankCutoff_DoesNotReceiveMrrCredit()
    {
        var actual = RetrievalMetrics.Evaluate(Case(("a", 1)), [new("x"), new("y"), new("a")], new(3, 2));
        Assert.AreEqual(1, actual.RecallAtK);
        Assert.AreEqual(0, actual.ReciprocalRank);
        Assert.AreEqual(0, actual.NdcgAtK);
    }

    [TestMethod]
    public void UnanswerableCases_HaveSeparateFalsePositiveMetric()
    {
        var empty = RetrievalMetrics.Evaluate(Case(), []);
        var returned = RetrievalMetrics.Evaluate(Case(("zero", 0)), [new("zero")]);
        Assert.IsNull(empty.RecallAtK);
        Assert.IsNull(empty.NdcgAtK);
        Assert.IsNull(returned.ReciprocalRank);
        Assert.IsFalse(empty.NoAnswerFalsePositive);
        Assert.IsTrue(returned.NoAnswerFalsePositive);
    }

    [TestMethod]
    public void Aggregation_IsMacroAndExcludesUnanswerableCasesFromQuality()
    {
        var cases = new[]
        {
            Row("a", "easy", Case(("1", 1)), [new("1")]),
            Row("b", "hard", Case(("1", 1), ("2", 1), ("3", 1), ("4", 1)), []),
            Row("c", "no-answer", Case(), []),
            Row("d", "no-answer", Case(), [new("wrong")])
        };
        var aggregate = RetrievalMetrics.Aggregate(cases);
        Assert.AreEqual(4, aggregate.CaseCount);
        Assert.AreEqual(2, aggregate.AnswerableCaseCount);
        Assert.AreEqual(2, aggregate.NoAnswerCaseCount);
        Assert.AreEqual(.5, aggregate.RecallAtK);
        Assert.AreEqual(.5, aggregate.MrrAtK);
        Assert.AreEqual(.5, aggregate.NoAnswerFalsePositiveRate);
        var categories = RetrievalMetrics.AggregateByCategory(cases);
        Assert.AreEqual(3, categories.Count);
        Assert.AreEqual(1, categories["easy"].RecallAtK);
        Assert.AreEqual(0, categories["hard"].RecallAtK);
        Assert.IsNull(categories["no-answer"].RecallAtK);
    }

    [TestMethod]
    public void EmptyAggregation_HasNoInventedPerfectMetrics()
    {
        var actual = RetrievalMetrics.Aggregate([]);
        Assert.AreEqual(0, actual.CaseCount);
        Assert.IsNull(actual.RecallAtK);
        Assert.IsNull(actual.NoAnswerFalsePositiveRate);
    }

    [TestMethod]
    public void DuplicateCasesOrPartiallyMissingMetrics_CannotBiasMacroAverages()
    {
        var row = Row("a", "category", Case(("a", 1)), [new("a")]);
        Assert.ThrowsExactly<ArgumentException>(() => RetrievalMetrics.Aggregate([row, row]));
        var incomplete = row with { Metrics = row.Metrics with { RecallAtK = null } };
        Assert.ThrowsExactly<ArgumentException>(() => RetrievalMetrics.Aggregate([incomplete]));
    }

    [TestMethod]
    public void RankingOrder_IsPreservedRatherThanResortedByScores()
    {
        var actual = RetrievalMetrics.Evaluate(Case(("a", 1)), [new("x", 0), new("a", 100)]);
        Assert.AreEqual(.5, actual.ReciprocalRank);
    }

    [TestMethod]
    public void InvalidCutoffsGradesAndNonfiniteScores_AreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RetrievalMetrics.Evaluate(Case(), [], new(0, 10)));
        Assert.ThrowsExactly<ArgumentException>(() => RetrievalMetrics.Evaluate(Case(("a", -1)), []));
        Assert.ThrowsExactly<ArgumentException>(() => RetrievalMetrics.Evaluate(Case(("a", 31)), []));
        Assert.ThrowsExactly<ArgumentException>(() => RetrievalMetrics.Evaluate(Case(), [new("a", double.NaN)]));
    }

    private static EvaluationCase Case(params (string Id, int Grade)[] judgments) => new()
    {
        Id = "case", Query = "query", Judgments = judgments.ToDictionary(item => item.Id, item => item.Grade)
    };
    private static CaseEvaluation Row(string id, string category, EvaluationCase item, RankedDocument[] hits) =>
        new(id, category, null, RetrievalMetrics.Evaluate(item, hits));
}
