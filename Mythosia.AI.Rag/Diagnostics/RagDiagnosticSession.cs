using Mythosia.AI.Loaders;
using Mythosia.AI.Rag.Splitters;
using Mythosia.AI.VectorDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Diagnostics
{
    /// <summary>
    /// Fluent diagnostic session for debugging RAG search quality issues.
    /// Access via extension method: <c>ragStore.Diagnose()</c> or <c>ragPipeline.Diagnose()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// // One-liner: "Why didn't my query find this text?"
    /// var report = await ragStore.Diagnose()
    ///     .WhyMissingAsync("최근 연봉이 얼마?", "$1");
    /// Console.WriteLine(report.ToReport());
    ///
    /// // Health check on the entire index
    /// var health = await ragStore.Diagnose().HealthCheckAsync();
    /// Console.WriteLine(health.ToReport());
    ///
    /// // Compare splitter configurations
    /// var comparison = ragStore.Diagnose().CompareSplitters(document, "$1",
    ///     new RecursiveTextSplitter(500, 100),
    ///     new RecursiveTextSplitter(1000, 200));
    /// Console.WriteLine(comparison.ToReport());
    /// </code>
    /// </example>
    public class RagDiagnosticSession
    {
        private readonly RagDiagnostics _diag;
        private readonly RagPipeline _pipeline;

        internal RagDiagnosticSession(RagDiagnostics diagnostics, RagPipeline pipeline)
        {
            _diag = diagnostics;
            _pipeline = pipeline;
        }

        #region WhyMissing — Root cause analysis for missed results

        /// <summary>
        /// Analyzes exactly WHY a specific text was not found (or ranked low) for a given query.
        /// Traces through every pipeline stage and reports the root cause with actionable suggestions.
        /// </summary>
        /// <param name="query">The query that produced incorrect/incomplete results.</param>
        /// <param name="expectedText">The text you expected to find in results (e.g., "6,300만원").</param>
        /// <param name="collection">Optional collection name override.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<MissingAnalysis> WhyMissingAsync(
            string query,
            string expectedText,
            string? collection = null,
            CancellationToken cancellationToken = default)
        {
            var steps = new List<AnalysisStep>();
            var suggestions = new List<string>();
            var topK = _pipeline.Options.TopK;
            var minScore = _pipeline.Options.MinScore;

            // ── Step 1: Was the text ever indexed? ──
            bool canSearchChunks = _pipeline.VectorStore is InMemoryVectorStore;
            ChunkSearchMatch? targetMatch = null;

            if (canSearchChunks)
            {
                var matches = await _diag.FindChunksContainingAsync(expectedText, collection, cancellationToken);

                if (matches.Count == 0)
                {
                    steps.Add(AnalysisStep.Fail("Indexing",
                        $"\"{Truncate(expectedText, 40)}\" was NOT found in any stored chunk.",
                        "The text was never indexed, or the splitter cut through it."));
                    suggestions.Add("Verify the document was included in indexing.");
                    suggestions.Add("Use PreviewChunks() to check if the splitter breaks this text across chunks.");
                    return new MissingAnalysis(query, expectedText, steps, suggestions, null);
                }

                targetMatch = matches[0];
                steps.Add(AnalysisStep.Pass("Indexing",
                    $"Found in {matches.Count} chunk(s). Primary: \"{targetMatch.Record.Id}\" ({targetMatch.Record.Content.Length} chars)."));
            }
            else
            {
                steps.Add(AnalysisStep.Info("Indexing",
                    "Cannot verify (requires InMemoryVectorStore). Skipping to search analysis."));
            }

            // ── Step 2: Score all chunks against the query ──
            var queryResult = await _diag.DiagnoseQueryAsync(query, expectedText, collection, cancellationToken);
            var target = queryResult.TargetChunkInfo;

            if (target == null)
            {
                steps.Add(AnalysisStep.Fail("Scoring",
                    "Target chunk was not found in search results.",
                    "The chunk may be filtered by namespace/metadata, or the text was split across chunk boundaries."));
                return new MissingAnalysis(query, expectedText, steps, suggestions, queryResult);
            }

            // ── Step 3: Keyword density ──
            double density = (double)expectedText.Length / target.Record.Content.Length * 100;
            int chunkSize = GetChunkSize();

            if (density < 3.0)
            {
                steps.Add(AnalysisStep.Fail("Keyword Density",
                    $"{density:F1}% — \"{Truncate(expectedText, 30)}\" ({expectedText.Length} chars) is buried in a {target.Record.Content.Length}-char chunk. The embedding is dominated by surrounding content.",
                    "Reduce ChunkSize to isolate the target, or extract it as structured metadata."));
                if (chunkSize > 0)
                    suggestions.Add($"Reduce ChunkSize (current: {chunkSize}). Try {Math.Max(200, chunkSize / 2)}.");
            }
            else if (density < 10.0)
            {
                steps.Add(AnalysisStep.Warn("Keyword Density",
                    $"{density:F1}% — moderate. Target is a relatively small part of the chunk."));
            }
            else
            {
                steps.Add(AnalysisStep.Pass("Keyword Density",
                    $"{density:F1}% — good coverage."));
            }

            // ── Step 4: Rank vs TopK ──
            if (target.IsInTopK)
            {
                steps.Add(AnalysisStep.Pass("TopK Ranking",
                    $"Rank #{target.Rank} is within TopK={topK}. Score: {target.Score:F4}."));
            }
            else
            {
                double cutoff = queryResult.AllScoredResults.Count >= topK
                    ? queryResult.AllScoredResults[topK - 1].Score
                    : 0.0;

                steps.Add(AnalysisStep.Fail("TopK Ranking",
                    $"Rank #{target.Rank} is OUTSIDE TopK={topK}. Score: {target.Score:F4}, cutoff: {cutoff:F4} (gap: {cutoff - target.Score:F4}).",
                    $"Increase TopK to at least {target.Rank}, or improve chunking to raise the score."));
                suggestions.Add($"Increase TopK from {topK} to at least {target.Rank}.");
                if (density < 5.0)
                    suggestions.Add("Low keyword density is likely causing the low score. Smaller chunks would help.");
            }

            // ── Step 5: MinScore filter ──
            if (minScore.HasValue)
            {
                if (target.PassesMinScore)
                {
                    steps.Add(AnalysisStep.Pass("MinScore Filter",
                        $"Score {target.Score:F4} >= MinScore {minScore.Value:F3}."));
                }
                else
                {
                    steps.Add(AnalysisStep.Fail("MinScore Filter",
                        $"Score {target.Score:F4} < MinScore {minScore.Value:F3}. Filtered out even if within TopK.",
                        $"Lower MinScore to {Math.Max(0, target.Score - 0.01):F3} or below."));
                    suggestions.Add($"Lower MinScore from {minScore.Value:F3} to {Math.Max(0, target.Score - 0.01):F3}.");
                }
            }

            // ── Step 6: What outranked it? ──
            if (!target.IsInTopK && queryResult.AllScoredResults.Count > 0)
            {
                var competitors = queryResult.AllScoredResults.Take(topK).ToList();
                var sb = new StringBuilder();
                foreach (var c in competitors)
                    sb.AppendLine($"    #{c.Rank} (score={c.Score:F4}): \"{Truncate(c.Preview, 60)}\"");

                steps.Add(AnalysisStep.Info("Competing Chunks",
                    $"These {competitors.Count} chunks outranked your target:\n{sb}"));
            }

            return new MissingAnalysis(query, expectedText, steps, suggestions, queryResult);
        }

        #endregion

        #region HealthCheck — Proactive index quality check

        /// <summary>
        /// Runs a health check on the stored index, detecting common quality issues:
        /// chunk size variance, oversized chunks, very small chunks, and potential duplicates.
        /// </summary>
        public async Task<HealthCheckResult> HealthCheckAsync(
            string? collection = null,
            CancellationToken cancellationToken = default)
        {
            var items = new List<HealthCheckItem>();
            var col = collection ?? _pipeline.Options.DefaultCollection;

            var inMemory = _pipeline.VectorStore as InMemoryVectorStore;
            if (inMemory == null)
            {
                items.Add(HealthCheckItem.Info("Store Type",
                    "HealthCheck requires InMemoryVectorStore for full analysis."));
                return new HealthCheckResult(col, 0, items);
            }

            var allRecords = await inMemory.ListAllRecordsAsync(col, cancellationToken);
            int count = allRecords.Count;

            if (count == 0)
            {
                items.Add(HealthCheckItem.Fail("Chunk Count", "No chunks found. Index is empty."));
                return new HealthCheckResult(col, 0, items);
            }

            // ── Chunk count ──
            items.Add(HealthCheckItem.Pass("Chunk Count", $"{count} chunks indexed."));

            // ── Size statistics ──
            var sizes = allRecords.Select(r => r.Content.Length).ToList();
            int min = sizes.Min();
            int max = sizes.Max();
            double avg = sizes.Average();
            double variance = sizes.Sum(s => (s - avg) * (s - avg)) / sizes.Count;
            double stddev = Math.Sqrt(variance);

            items.Add(HealthCheckItem.Info("Chunk Sizes",
                $"Min={min}, Max={max}, Avg={avg:F0}, StdDev={stddev:F0} chars."));

            if (stddev > avg * 0.5)
            {
                items.Add(HealthCheckItem.Warn("Size Variance",
                    $"High variance (StdDev={stddev:F0} vs Avg={avg:F0}). Very uneven chunk sizes hurt search consistency."));
            }

            // ── Oversized chunks ──
            int chunkSize = GetChunkSize();
            if (chunkSize > 0)
            {
                int oversized = sizes.Count(s => s > chunkSize);
                if (oversized > 0)
                {
                    items.Add(HealthCheckItem.Warn("Oversized Chunks",
                        $"{oversized} chunk(s) exceed ChunkSize={chunkSize}. These may lose information at the tail due to embedding model token limits."));
                }
            }

            // ── Very small chunks ──
            int tiny = sizes.Count(s => s < 50);
            if (tiny > 0)
            {
                items.Add(HealthCheckItem.Warn("Tiny Chunks",
                    $"{tiny} chunk(s) are under 50 chars. These embed poorly and add noise to search results."));
            }

            // ── Near-duplicates (simple check: exact content match) ──
            var contentSet = new HashSet<string>(StringComparer.Ordinal);
            int duplicates = 0;
            foreach (var record in allRecords)
            {
                if (!contentSet.Add(record.Content))
                    duplicates++;
            }
            if (duplicates > 0)
            {
                items.Add(HealthCheckItem.Warn("Duplicates",
                    $"{duplicates} exact duplicate chunk(s) found. These waste storage and can skew search results."));
            }
            else
            {
                items.Add(HealthCheckItem.Pass("Duplicates", "No exact duplicates detected."));
            }

            return new HealthCheckResult(col, count, items);
        }

        #endregion

        #region CompareSplitters — A/B test splitter configurations

        /// <summary>
        /// Compares how different splitter configurations handle the same document.
        /// Shows where the target text ends up in each configuration and the resulting keyword density.
        /// </summary>
        /// <param name="document">The document to split.</param>
        /// <param name="targetText">Text to track across splitter outputs (e.g., "6,300만원").</param>
        /// <param name="splitters">Two or more splitter configurations to compare.</param>
        public SplitterComparison CompareSplitters(
            RagDocument document,
            string targetText,
            params ITextSplitter[] splitters)
        {
            var results = new List<SplitterComparisonEntry>();

            foreach (var splitter in splitters)
            {
                var preview = _diag.PreviewChunks(document, splitter);
                var chunks = preview.Chunks;

                // Find the chunk containing target text
                RagChunk? targetChunk = null;
                int targetIndex = -1;
                for (int i = 0; i < chunks.Count; i++)
                {
                    if (chunks[i].Content.IndexOf(targetText, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        targetChunk = chunks[i];
                        targetIndex = i;
                        break;
                    }
                }

                double density = targetChunk != null
                    ? (double)targetText.Length / targetChunk.Content.Length * 100
                    : 0;

                results.Add(new SplitterComparisonEntry(
                    splitterName: FormatSplitterName(splitter),
                    totalChunks: chunks.Count,
                    avgChunkSize: chunks.Count > 0 ? (int)chunks.Average(c => c.Content.Length) : 0,
                    targetFound: targetChunk != null,
                    targetChunkIndex: targetIndex,
                    targetChunkSize: targetChunk?.Content.Length ?? 0,
                    targetDensity: density));
            }

            return new SplitterComparison(targetText, document.Content?.Length ?? 0, results);
        }

        #endregion

        #region Private Helpers

        private int GetChunkSize()
        {
            var splitter = _pipeline.TextSplitter;
            if (splitter is RecursiveTextSplitter recursive)
                return recursive.ChunkSize;
            if (splitter is CharacterTextSplitter character)
                return character.ChunkSize;
            return -1;
        }

        private static string FormatSplitterName(ITextSplitter splitter)
        {
            if (splitter is RecursiveTextSplitter r)
                return $"Recursive({r.ChunkSize}, overlap={r.ChunkOverlap})";
            if (splitter is CharacterTextSplitter c)
                return $"Character({c.ChunkSize}, overlap={c.ChunkOverlap})";
            return splitter.GetType().Name;
        }

        private static string Truncate(string text, int maxLength)
        {
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }

        #endregion
    }

    #region Result Types

    /// <summary>
    /// Diagnostic status for an analysis step.
    /// </summary>
    public enum DiagnosticStatus
    {
        Pass,
        Warning,
        Fail,
        Info
    }

    /// <summary>
    /// A single step in the WhyMissing analysis pipeline.
    /// </summary>
    public class AnalysisStep
    {
        public DiagnosticStatus Status { get; }
        public string StepName { get; }
        public string Message { get; }
        public string? Suggestion { get; }

        public AnalysisStep(DiagnosticStatus status, string stepName, string message, string? suggestion)
        {
            Status = status;
            StepName = stepName;
            Message = message;
            Suggestion = suggestion;
        }

        public static AnalysisStep Pass(string step, string message) =>
            new AnalysisStep(DiagnosticStatus.Pass, step, message, null);

        public static AnalysisStep Warn(string step, string message, string? suggestion = null) =>
            new AnalysisStep(DiagnosticStatus.Warning, step, message, suggestion);

        public static AnalysisStep Fail(string step, string message, string? suggestion = null) =>
            new AnalysisStep(DiagnosticStatus.Fail, step, message, suggestion);

        public static AnalysisStep Info(string step, string message) =>
            new AnalysisStep(DiagnosticStatus.Info, step, message, null);

        internal string StatusMarker
        {
            get
            {
                switch (Status)
                {
                    case DiagnosticStatus.Pass: return "[PASS]";
                    case DiagnosticStatus.Warning: return "[WARN]";
                    case DiagnosticStatus.Fail: return "[FAIL]";
                    default: return "[INFO]";
                }
            }
        }
    }

    /// <summary>
    /// Result of WhyMissingAsync — complete root-cause analysis with actionable suggestions.
    /// </summary>
    public class MissingAnalysis
    {
        public string Query { get; }
        public string ExpectedText { get; }
        public IReadOnlyList<AnalysisStep> Steps { get; }
        public IReadOnlyList<string> Suggestions { get; }
        public QueryDiagnosticResult? QueryDetail { get; }

        public MissingAnalysis(
            string query, string expectedText,
            IReadOnlyList<AnalysisStep> steps, IReadOnlyList<string> suggestions,
            QueryDiagnosticResult? queryDetail)
        {
            Query = query;
            ExpectedText = expectedText;
            Steps = steps;
            Suggestions = suggestions;
            QueryDetail = queryDetail;
        }

        /// <summary>
        /// Whether the analysis found issues. True if any step failed or warned.
        /// </summary>
        public bool HasIssues => Steps.Any(s => s.Status == DiagnosticStatus.Fail || s.Status == DiagnosticStatus.Warning);

        /// <summary>
        /// Returns a human-readable diagnostic report.
        /// </summary>
        public string ToReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine("  WhyMissing Analysis Report");
            sb.AppendLine("========================================");
            sb.AppendLine($"  Query:    \"{Query}\"");
            sb.AppendLine($"  Expected: \"{ExpectedText}\"");
            sb.AppendLine("----------------------------------------");

            foreach (var step in Steps)
            {
                sb.AppendLine($"  {step.StatusMarker} {step.StepName}");
                sb.AppendLine($"           {step.Message}");
                if (step.Suggestion != null)
                    sb.AppendLine($"           -> {step.Suggestion}");
                sb.AppendLine();
            }

            if (Suggestions.Count > 0)
            {
                sb.AppendLine("========================================");
                sb.AppendLine("  Suggested Actions (priority order)");
                sb.AppendLine("========================================");
                for (int i = 0; i < Suggestions.Count; i++)
                    sb.AppendLine($"  {i + 1}. {Suggestions[i]}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Result of HealthCheckAsync — proactive index quality report.
    /// </summary>
    public class HealthCheckResult
    {
        public string Collection { get; }
        public int TotalChunks { get; }
        public IReadOnlyList<HealthCheckItem> Items { get; }

        public HealthCheckResult(string collection, int totalChunks, IReadOnlyList<HealthCheckItem> items)
        {
            Collection = collection;
            TotalChunks = totalChunks;
            Items = items;
        }

        public bool HasWarnings => Items.Any(i => i.Status == DiagnosticStatus.Warning || i.Status == DiagnosticStatus.Fail);

        public string ToReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine("  RAG Index Health Check");
            sb.AppendLine("========================================");
            sb.AppendLine($"  Collection: \"{Collection}\" ({TotalChunks} chunks)");
            sb.AppendLine("----------------------------------------");

            foreach (var item in Items)
                sb.AppendLine($"  {item.StatusMarker} {item.Category}: {item.Message}");

            return sb.ToString();
        }
    }

    /// <summary>
    /// A single item in the health check report.
    /// </summary>
    public class HealthCheckItem
    {
        public DiagnosticStatus Status { get; }
        public string Category { get; }
        public string Message { get; }

        public HealthCheckItem(DiagnosticStatus status, string category, string message)
        {
            Status = status;
            Category = category;
            Message = message;
        }

        public static HealthCheckItem Pass(string category, string message) =>
            new HealthCheckItem(DiagnosticStatus.Pass, category, message);

        public static HealthCheckItem Warn(string category, string message) =>
            new HealthCheckItem(DiagnosticStatus.Warning, category, message);

        public static HealthCheckItem Fail(string category, string message) =>
            new HealthCheckItem(DiagnosticStatus.Fail, category, message);

        public static HealthCheckItem Info(string category, string message) =>
            new HealthCheckItem(DiagnosticStatus.Info, category, message);

        internal string StatusMarker
        {
            get
            {
                switch (Status)
                {
                    case DiagnosticStatus.Pass: return "[PASS]";
                    case DiagnosticStatus.Warning: return "[WARN]";
                    case DiagnosticStatus.Fail: return "[FAIL]";
                    default: return "[INFO]";
                }
            }
        }
    }

    /// <summary>
    /// Result of CompareSplitters — side-by-side comparison of different splitter configurations.
    /// </summary>
    public class SplitterComparison
    {
        public string TargetText { get; }
        public int DocumentLength { get; }
        public IReadOnlyList<SplitterComparisonEntry> Entries { get; }

        public SplitterComparison(string targetText, int documentLength, IReadOnlyList<SplitterComparisonEntry> entries)
        {
            TargetText = targetText;
            DocumentLength = documentLength;
            Entries = entries;
        }

        public string ToReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine("  Splitter Comparison");
            sb.AppendLine("========================================");
            sb.AppendLine($"  Document: {DocumentLength} chars");
            sb.AppendLine($"  Target:   \"{TargetText}\"");
            sb.AppendLine("----------------------------------------");
            sb.AppendLine($"  {"Splitter",-35} | {"Chunks",6} | {"Avg",5} | {"Target Chunk",12} | {"Density",7}");
            sb.AppendLine($"  {new string('-', 35)} | {new string('-', 6)} | {new string('-', 5)} | {new string('-', 12)} | {new string('-', 7)}");

            foreach (var e in Entries)
            {
                string targetInfo = e.TargetFound ? $"{e.TargetChunkSize,5} chars" : "NOT FOUND";
                string densityInfo = e.TargetFound ? $"{e.TargetDensity,5:F1}%" : "  N/A";
                string marker = "";
                if (e.TargetFound && e.TargetDensity >= 5.0) marker = " <-- best";
                else if (!e.TargetFound) marker = " <-- MISS";

                sb.AppendLine($"  {e.SplitterName,-35} | {e.TotalChunks,6} | {e.AvgChunkSize,5} | {targetInfo,12} | {densityInfo}{marker}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// A single splitter's results in a comparison.
    /// </summary>
    public class SplitterComparisonEntry
    {
        public string SplitterName { get; }
        public int TotalChunks { get; }
        public int AvgChunkSize { get; }
        public bool TargetFound { get; }
        public int TargetChunkIndex { get; }
        public int TargetChunkSize { get; }
        public double TargetDensity { get; }

        public SplitterComparisonEntry(
            string splitterName, int totalChunks, int avgChunkSize,
            bool targetFound, int targetChunkIndex, int targetChunkSize, double targetDensity)
        {
            SplitterName = splitterName;
            TotalChunks = totalChunks;
            AvgChunkSize = avgChunkSize;
            TargetFound = targetFound;
            TargetChunkIndex = targetChunkIndex;
            TargetChunkSize = targetChunkSize;
            TargetDensity = targetDensity;
        }
    }

    #endregion
}
