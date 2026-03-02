using Mythosia.AI.Loaders;
using Mythosia.AI.VectorDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Diagnostics
{
    /// <summary>
    /// Diagnostic tool for debugging RAG pipeline quality issues.
    /// Provides visibility into each pipeline stage: chunking → embedding → search → context.
    /// </summary>
    /// <example>
    /// <code>
    /// var diag = new RagDiagnostics(ragPipeline);
    /// 
    /// // 1. Inspect how a document is chunked
    /// var chunks = diag.PreviewChunks(document);
    /// foreach (var c in chunks.Chunks)
    ///     Console.WriteLine($"[{c.Index}] ({c.Content.Length} chars) {c.Content.Substring(0, 80)}...");
    /// 
    /// // 2. Find which chunk contains specific text
    /// var matches = await diag.FindChunksContainingAsync("6,300만원");
    /// 
    /// // 3. Full diagnostic: see ALL scores, not just TopK
    /// var result = await diag.DiagnoseQueryAsync("최근 연봉이 얼마야?");
    /// foreach (var r in result.AllScoredResults)
    ///     Console.WriteLine($"  [{r.Rank}] score={r.Score:F4} contains_target={r.ContainsText} | {r.Preview}");
    /// </code>
    /// </example>
    public class RagDiagnostics
    {
        private readonly RagPipeline _pipeline;
        private readonly IVectorStore _vectorStore;
        private readonly ITextSplitter _textSplitter;
        private readonly IEmbeddingProvider _embeddingProvider;

        /// <summary>
        /// Creates diagnostics from a RagPipeline instance.
        /// </summary>
        public RagDiagnostics(RagPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _vectorStore = pipeline.VectorStore;
            _textSplitter = pipeline.TextSplitter;
            _embeddingProvider = pipeline.EmbeddingProvider;
        }

        /// <summary>
        /// Creates diagnostics from a RagStore instance.
        /// </summary>
        public RagDiagnostics(RagStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            _pipeline = store.Pipeline as RagPipeline
                ?? throw new InvalidOperationException("RagDiagnostics requires the underlying pipeline to be a RagPipeline.");
            _vectorStore = store.VectorStore;
            _textSplitter = _pipeline.TextSplitter;
            _embeddingProvider = _pipeline.EmbeddingProvider;
        }

        #region 1. Chunk Preview — See what the splitter produces

        /// <summary>
        /// Preview how a document would be split into chunks WITHOUT indexing.
        /// Use this to verify chunk boundaries and overlap quality.
        /// </summary>
        public ChunkPreviewResult PreviewChunks(RagDocument document, ITextSplitter? splitter = null)
        {
            var effectiveSplitter = splitter ?? _textSplitter;
            var chunks = effectiveSplitter.Split(document);

            var overlaps = new List<ChunkOverlapInfo>();
            for (int i = 1; i < chunks.Count; i++)
            {
                string prev = chunks[i - 1].Content;
                string curr = chunks[i].Content;
                int overlapLength = ComputeOverlapLength(prev, curr);
                overlaps.Add(new ChunkOverlapInfo(i - 1, i, overlapLength));
            }

            return new ChunkPreviewResult(
                chunks: chunks,
                documentLength: document.Content?.Length ?? 0,
                overlaps: overlaps,
                splitterType: effectiveSplitter.GetType().Name);
        }

        #endregion

        #region 2. Find Chunks Containing Text — Verify target text was indexed

        /// <summary>
        /// Searches all stored chunks for a specific text substring.
        /// Use this to verify that the target information (e.g., "6,300만원") was actually indexed.
        /// </summary>
        public Task<IReadOnlyList<ChunkSearchMatch>> FindChunksContainingAsync(
            string text,
            string? collection = null,
            CancellationToken cancellationToken = default)
        {
            return FindChunksContainingInternalAsync(text, collection, cancellationToken);
        }

        private async Task<IReadOnlyList<ChunkSearchMatch>> FindChunksContainingInternalAsync(
            string text,
            string? collection,
            CancellationToken cancellationToken)
        {
            var col = collection ?? _pipeline.Options.DefaultCollection;
            var inMemory = _vectorStore as InMemoryVectorStore;
            if (inMemory == null)
                throw new InvalidOperationException(
                    "FindChunksContainingAsync requires InMemoryVectorStore. " +
                    "For other vector stores, use DiagnoseQueryAsync instead.");

            var allRecords = await inMemory.ListAllRecordsAsync(col, cancellationToken);
            var matches = new List<ChunkSearchMatch>();

            foreach (var record in allRecords)
            {
                int index = record.Content.IndexOf(text, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    matches.Add(new ChunkSearchMatch(
                        record: record,
                        matchIndex: index,
                        preview: BuildPreview(record.Content, index, text.Length)));
                }
            }

            return matches;
        }

        #endregion

        #region 3. Diagnose Query — Full pipeline trace with all scores

        /// <summary>
        /// Runs the full RAG query pipeline with diagnostics enabled.
        /// Returns ALL chunk scores (not just TopK) so you can see exactly why a chunk was missed.
        /// </summary>
        /// <param name="query">The user query to diagnose.</param>
        /// <param name="targetText">Optional: text you expect to find in results (e.g., "6,300만원"). 
        /// When provided, results are annotated with whether they contain this text.</param>
        /// <param name="collection">Optional collection name override.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<QueryDiagnosticResult> DiagnoseQueryAsync(
            string query,
            string? targetText = null,
            string? collection = null,
            CancellationToken cancellationToken = default)
        {
            var col = collection ?? _pipeline.Options.DefaultCollection;
            var topK = _pipeline.Options.TopK;
            var minScore = _pipeline.Options.MinScore;

            // Step 1: Embed query
            var queryVector = await _embeddingProvider.GetEmbeddingAsync(query, cancellationToken);

            // Step 2: Get ALL scores (not just TopK)
            IReadOnlyList<VectorSearchResult> allScored;
            var inMemory = _vectorStore as InMemoryVectorStore;
            if (inMemory != null)
            {
                allScored = await inMemory.ScoredListAsync(col, queryVector, cancellationToken);
            }
            else
            {
                // Fallback: use SearchAsync with max TopK
                allScored = await _vectorStore.SearchAsync(col, queryVector, int.MaxValue, null, cancellationToken);
            }

            // Step 3: Annotate results
            var scoredResults = new List<ScoredChunkInfo>();
            for (int i = 0; i < allScored.Count; i++)
            {
                var r = allScored[i];
                bool isInTopK = i < topK;
                bool passesMinScore = !minScore.HasValue || r.Score >= minScore.Value;
                bool containsTarget = targetText != null &&
                    r.Record.Content.IndexOf(targetText, StringComparison.OrdinalIgnoreCase) >= 0;

                scoredResults.Add(new ScoredChunkInfo(
                    rank: i + 1,
                    record: r.Record,
                    score: r.Score,
                    isInTopK: isInTopK,
                    passesMinScore: passesMinScore,
                    containsTarget: containsTarget,
                    preview: Truncate(r.Record.Content, 120)));
            }

            // Step 4: Identify the target chunk
            ScoredChunkInfo? targetChunk = targetText != null
                ? scoredResults.FirstOrDefault(r => r.ContainsTarget)
                : null;

            return new QueryDiagnosticResult(
                query: query,
                targetText: targetText,
                topK: topK,
                minScore: minScore,
                totalChunks: allScored.Count,
                allScoredResults: scoredResults,
                targetChunkInfo: targetChunk);
        }

        #endregion

        #region Private Helpers

        private static int ComputeOverlapLength(string prev, string curr)
        {
            int maxCheck = Math.Min(prev.Length, curr.Length);
            for (int len = maxCheck; len > 0; len--)
            {
                if (prev.Length >= len &&
                    curr.Length >= len &&
                    prev.Substring(prev.Length - len) == curr.Substring(0, len))
                {
                    return len;
                }
            }
            return 0;
        }

        private static string BuildPreview(string content, int matchIndex, int matchLength, int contextChars = 60)
        {
            int start = Math.Max(0, matchIndex - contextChars);
            int end = Math.Min(content.Length, matchIndex + matchLength + contextChars);
            var preview = content.Substring(start, end - start);
            return (start > 0 ? "..." : "") + preview + (end < content.Length ? "..." : "");
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
    /// Result of PreviewChunks — shows exactly how a document was split.
    /// </summary>
    public class ChunkPreviewResult
    {
        public IReadOnlyList<RagChunk> Chunks { get; }
        public int DocumentLength { get; }
        public int TotalChunks => Chunks.Count;
        public int AverageChunkSize => Chunks.Count > 0 ? Chunks.Sum(c => c.Content.Length) / Chunks.Count : 0;
        public IReadOnlyList<ChunkOverlapInfo> Overlaps { get; }
        public string SplitterType { get; }

        public ChunkPreviewResult(
            IReadOnlyList<RagChunk> chunks, int documentLength,
            IReadOnlyList<ChunkOverlapInfo> overlaps, string splitterType)
        {
            Chunks = chunks;
            DocumentLength = documentLength;
            Overlaps = overlaps;
            SplitterType = splitterType;
        }

        /// <summary>
        /// Returns a human-readable summary of the chunking result.
        /// </summary>
        public string ToSummary()
        {
            var lines = new List<string>
            {
                $"=== Chunk Preview ({SplitterType}) ===",
                $"Document length: {DocumentLength} chars → {TotalChunks} chunks (avg {AverageChunkSize} chars)",
                ""
            };

            for (int i = 0; i < Chunks.Count; i++)
            {
                var c = Chunks[i];
                var preview = c.Content.Length > 80 ? c.Content.Substring(0, 80) + "..." : c.Content;
                preview = preview.Replace("\n", "\\n").Replace("\r", "");
                lines.Add($"  [{i}] {c.Content.Length,5} chars | {preview}");

                if (i < Overlaps.Count)
                    lines.Add($"       ↕ overlap: {Overlaps[i].OverlapLength} chars");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// Overlap information between two adjacent chunks.
    /// </summary>
    public class ChunkOverlapInfo
    {
        public int ChunkIndexA { get; }
        public int ChunkIndexB { get; }
        public int OverlapLength { get; }

        public ChunkOverlapInfo(int chunkIndexA, int chunkIndexB, int overlapLength)
        {
            ChunkIndexA = chunkIndexA;
            ChunkIndexB = chunkIndexB;
            OverlapLength = overlapLength;
        }
    }

    /// <summary>
    /// A stored chunk that matched a text search.
    /// </summary>
    public class ChunkSearchMatch
    {
        public VectorRecord Record { get; }
        public int MatchIndex { get; }
        public string Preview { get; }

        public ChunkSearchMatch(VectorRecord record, int matchIndex, string preview)
        {
            Record = record;
            MatchIndex = matchIndex;
            Preview = preview;
        }
    }

    /// <summary>
    /// Full diagnostic result for a query — shows ALL chunks with scores and why each was included/excluded.
    /// </summary>
    public class QueryDiagnosticResult
    {
        public string Query { get; }
        public string? TargetText { get; }
        public int TopK { get; }
        public double? MinScore { get; }
        public int TotalChunks { get; }
        public IReadOnlyList<ScoredChunkInfo> AllScoredResults { get; }
        public ScoredChunkInfo? TargetChunkInfo { get; }

        public QueryDiagnosticResult(
            string query, string? targetText, int topK, double? minScore,
            int totalChunks, IReadOnlyList<ScoredChunkInfo> allScoredResults,
            ScoredChunkInfo? targetChunkInfo)
        {
            Query = query;
            TargetText = targetText;
            TopK = topK;
            MinScore = minScore;
            TotalChunks = totalChunks;
            AllScoredResults = allScoredResults;
            TargetChunkInfo = targetChunkInfo;
        }

        /// <summary>
        /// Returns a human-readable diagnostic report.
        /// </summary>
        public string ToReport()
        {
            var lines = new List<string>
            {
                $"=== Query Diagnostic Report ===",
                $"Query: \"{Query}\"",
                $"Target text: \"{TargetText ?? "(none)"}\"",
                $"Settings: TopK={TopK}, MinScore={MinScore?.ToString("F3") ?? "null"}",
                $"Total chunks in store: {TotalChunks}",
                ""
            };

            if (TargetChunkInfo != null)
            {
                lines.Add($"★ TARGET CHUNK found at rank #{TargetChunkInfo.Rank} (score={TargetChunkInfo.Score:F4})");
                lines.Add($"  In TopK: {TargetChunkInfo.IsInTopK} | Passes MinScore: {TargetChunkInfo.PassesMinScore}");
                if (!TargetChunkInfo.IsInTopK)
                    lines.Add($"  ⚠ MISSED: Target chunk is rank #{TargetChunkInfo.Rank} but TopK={TopK}. Increase TopK or improve chunking.");
                if (!TargetChunkInfo.PassesMinScore)
                    lines.Add($"  ⚠ FILTERED: Target chunk score {TargetChunkInfo.Score:F4} < MinScore {MinScore:F4}.");
                lines.Add("");
            }
            else if (TargetText != null)
            {
                lines.Add("⚠ TARGET NOT FOUND in any stored chunk! The text may not have been indexed.");
                lines.Add("");
            }

            lines.Add("--- All Results (by score) ---");
            int showCount = Math.Min(AllScoredResults.Count, 20);
            for (int i = 0; i < showCount; i++)
            {
                var r = AllScoredResults[i];
                string marker = r.ContainsTarget ? " ★" : "";
                string topKMarker = r.IsInTopK ? " [TopK]" : "";
                lines.Add($"  #{r.Rank,3} score={r.Score:F4}{topKMarker}{marker} | {r.Preview}");
            }

            if (AllScoredResults.Count > showCount)
                lines.Add($"  ... and {AllScoredResults.Count - showCount} more chunks");

            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// A single chunk with its diagnostic annotations.
    /// </summary>
    public class ScoredChunkInfo
    {
        public int Rank { get; }
        public VectorRecord Record { get; }
        public double Score { get; }
        public bool IsInTopK { get; }
        public bool PassesMinScore { get; }
        public bool ContainsTarget { get; }
        public string Preview { get; }

        public ScoredChunkInfo(
            int rank, VectorRecord record, double score,
            bool isInTopK, bool passesMinScore, bool containsTarget, string preview)
        {
            Rank = rank;
            Record = record;
            Score = score;
            IsInTopK = isInTopK;
            PassesMinScore = passesMinScore;
            ContainsTarget = containsTarget;
            Preview = preview;
        }
    }

    #endregion
}
