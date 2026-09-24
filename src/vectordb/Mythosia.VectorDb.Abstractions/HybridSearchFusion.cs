using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.VectorDb
{
    /// <summary>Shared normalized weighted reciprocal rank fusion for backend adapters.</summary>
    public static class HybridSearchFusion
    {
        /// <summary>
        /// Merges lists already sorted in descending relevance order. The first rank is one;
        /// the final score is (k + 1) times the sum of weight / (k + rank), bounded by one.
        /// Duplicate IDs in a leg contribute only once. Ties use ordinal record ID ordering.
        /// </summary>
        public static IReadOnlyList<VectorSearchResult> Merge(
            IReadOnlyList<VectorSearchResult> vectorResults,
            IReadOnlyList<VectorSearchResult> textResults,
            HybridSearchOptions options, int topK, double? minScore = null)
        {
            if (vectorResults == null) throw new ArgumentNullException(nameof(vectorResults));
            if (textResults == null) throw new ArgumentNullException(nameof(textResults));
            if (options == null) throw new ArgumentNullException(nameof(options));
            var snapshot = options.Snapshot();
            snapshot.GetCandidateCount(topK);
            var scores = new Dictionary<string, (VectorRecord record, double score)>(StringComparer.Ordinal);

            Add(vectorResults, snapshot.VectorWeight);
            Add(textResults, 1.0 - snapshot.VectorWeight);

            return scores.Values
                .Select(item => new VectorSearchResult(item.record, item.score * (snapshot.RrfK + 1.0)))
                .Where(item => !minScore.HasValue || item.Score >= minScore.Value)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Record.Id, StringComparer.Ordinal)
                .Take(topK).ToList();

            void Add(IReadOnlyList<VectorSearchResult> results, double weight)
            {
                if (weight == 0) return;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var rank = 0;
                foreach (var result in results)
                {
                    if (!seen.Add(result.Record.Id)) continue;
                    rank++;
                    var score = weight / (snapshot.RrfK + (double)rank);
                    if (scores.TryGetValue(result.Record.Id, out var existing))
                        scores[result.Record.Id] = (existing.record, existing.score + score);
                    else
                        scores[result.Record.Id] = (result.Record, score);
                }
            }
        }
    }
}
