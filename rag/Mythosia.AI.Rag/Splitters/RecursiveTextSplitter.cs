using Mythosia.AI.Loaders;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Rag.Splitters
{
    /// <summary>
    /// Recursively splits text using an ordered list of separators (LangChain-style).
    /// At each level the best separator is chosen, small pieces are merged up to
    /// <see cref="ChunkSize"/>, and only oversized pieces recurse to the next separator.
    /// </summary>
    public class RecursiveTextSplitter : ITextSplitter
    {
        /// <summary>Maximum number of characters per chunk.</summary>
        public int ChunkSize { get; set; } = 1000;

        /// <summary>Number of overlapping characters between consecutive chunks.</summary>
        public int ChunkOverlap { get; set; } = 200;

        /// <summary>
        /// Ordered list of separators to try when splitting.
        /// The splitter picks the first separator found in the text.
        /// An empty string as the last entry enables character-level splitting as a last resort.
        /// </summary>
        public string[] Separators { get; set; } = new[] { "\n\n", "\n", ". ", " ", "" };

        /// <summary>
        /// When true the separator is kept at the start of the next split so that
        /// paragraph / sentence boundaries are preserved in the chunk text.
        /// Default: true.
        /// </summary>
        public bool KeepSeparator { get; set; } = true;

        public RecursiveTextSplitter() { }

        public RecursiveTextSplitter(int chunkSize, int chunkOverlap = 200, IEnumerable<string>? separators = null)
        {
            ChunkSize = chunkSize;
            ChunkOverlap = chunkOverlap;
            if (separators != null)
                Separators = separators.ToArray();
        }

        // =================================================================
        //  ITextSplitter
        // =================================================================

        public IReadOnlyList<RagChunk> Split(RagDocument document)
        {
            if (string.IsNullOrEmpty(document.Content))
                return Array.Empty<RagChunk>();

            var textChunks = SplitText(document.Content);
            var result = new List<RagChunk>();
            int index = 0;

            foreach (var text in textChunks)
            {
                var content = text.Trim();
                if (content.Length == 0) continue;

                var chunk = new RagChunk
                {
                    Id = $"{document.Id}_chunk_{index}",
                    DocumentId = document.Id,
                    Content = content,
                    Index = index,
                    Metadata = new Dictionary<string, string>(document.Metadata)
                };
                chunk.Metadata["source"] = document.Source;
                chunk.Metadata["chunk_index"] = index.ToString();

                result.Add(chunk);
                index++;
            }

            return result;
        }

        // =================================================================
        //  Core recursive algorithm
        // =================================================================

        /// <summary>
        /// Split text into chunks respecting <see cref="ChunkSize"/> and
        /// <see cref="ChunkOverlap"/>.
        /// </summary>
        public List<string> SplitText(string text)
        {
            return SplitTextRecursive(text, Separators);
        }

        /// <summary>
        /// 1. Pick the first separator that exists in the text.
        /// 2. Split by that separator.
        /// 3. Merge small splits up to ChunkSize (with overlap).
        /// 4. Recurse on oversized splits with the remaining separators.
        /// </summary>
        private List<string> SplitTextRecursive(string text, string[] separators)
        {
            // Base case: already small enough
            if (text.Trim().Length == 0)
                return new List<string>();
            if (text.Length <= ChunkSize)
                return new List<string> { text };

            // Pick the best (first matching) separator
            string chosenSep = "";
            string[] remaining = Array.Empty<string>();

            for (int i = 0; i < separators.Length; i++)
            {
                var sep = separators[i];
                if (sep == "" || text.IndexOf(sep, StringComparison.Ordinal) >= 0)
                {
                    chosenSep = sep;
                    if (i + 1 < separators.Length)
                    {
                        remaining = new string[separators.Length - i - 1];
                        Array.Copy(separators, i + 1, remaining, 0, remaining.Length);
                    }
                    break;
                }
            }

            // Split by the chosen separator
            var splits = SplitBySeparator(text, chosenSep);

            // Partition into small (≤ ChunkSize) and large (> ChunkSize) pieces.
            // Small pieces are accumulated and merged; large pieces are recursed.
            var finalChunks = new List<string>();
            var pending = new List<string>(); // small pieces waiting to be merged

            foreach (var piece in splits)
            {
                if (piece.Length <= ChunkSize)
                {
                    pending.Add(piece);
                }
                else
                {
                    // Flush pending small pieces first
                    if (pending.Count > 0)
                    {
                        finalChunks.AddRange(MergeSplits(pending));
                        pending.Clear();
                    }

                    // Recurse on the oversized piece
                    if (remaining.Length > 0)
                    {
                        finalChunks.AddRange(SplitTextRecursive(piece, remaining));
                    }
                    else
                    {
                        // No more separators — hard split by length
                        finalChunks.AddRange(SplitByLength(piece));
                    }
                }
            }

            // Flush remaining small pieces
            if (pending.Count > 0)
                finalChunks.AddRange(MergeSplits(pending));

            return finalChunks;
        }

        // =================================================================
        //  Separator splitting
        // =================================================================

        /// <summary>
        /// Splits <paramref name="text"/> by <paramref name="separator"/>.
        /// When <see cref="KeepSeparator"/> is true the separator is prepended
        /// to the next piece (preserving paragraph / line boundaries).
        /// </summary>
        private List<string> SplitBySeparator(string text, string separator)
        {
            if (string.IsNullOrEmpty(separator))
            {
                // Character-level: each char is a split
                var chars = new List<string>(text.Length);
                foreach (var c in text)
                    chars.Add(c.ToString());
                return chars;
            }

            var rawParts = text.Split(new[] { separator }, StringSplitOptions.None);
            var result = new List<string>();

            for (int i = 0; i < rawParts.Length; i++)
            {
                string piece;
                if (KeepSeparator && i > 0)
                    piece = separator + rawParts[i];
                else
                    piece = rawParts[i];

                if (piece.Length > 0)
                    result.Add(piece);
            }

            return result;
        }

        // =================================================================
        //  Merge + Overlap
        // =================================================================

        /// <summary>
        /// Merges small splits into chunks up to <see cref="ChunkSize"/>.
        /// When a chunk is emitted, overlap is retained by keeping trailing
        /// splits from the previous chunk (aligned to split boundaries so
        /// words are never cut).
        /// </summary>
        private List<string> MergeSplits(List<string> splits)
        {
            var chunks = new List<string>();
            var current = new List<string>();
            int currentLen = 0;
            int effectiveOverlap = ChunkOverlap >= ChunkSize ? 0 : ChunkOverlap;

            foreach (var split in splits)
            {
                // Would adding this split exceed ChunkSize?
                if (currentLen + split.Length > ChunkSize && current.Count > 0)
                {
                    // Emit current chunk
                    var chunkText = string.Concat(current).Trim();
                    if (chunkText.Length > 0)
                        chunks.Add(chunkText);

                    // Retain tail splits for overlap (aligned to split boundaries)
                    while (currentLen > effectiveOverlap && current.Count > 1)
                    {
                        currentLen -= current[0].Length;
                        current.RemoveAt(0);
                    }
                }

                current.Add(split);
                currentLen += split.Length;
            }

            // Emit final chunk
            if (current.Count > 0)
            {
                var chunkText = string.Concat(current).Trim();
                if (chunkText.Length > 0)
                    chunks.Add(chunkText);
            }

            return chunks;
        }

        // =================================================================
        //  Hard length split (last resort)
        // =================================================================

        private List<string> SplitByLength(string text)
        {
            var parts = new List<string>();
            int step = Math.Max(1, ChunkSize - ChunkOverlap);
            for (int start = 0; start < text.Length; start += step)
            {
                int length = Math.Min(ChunkSize, text.Length - start);
                var segment = text.Substring(start, length);
                if (segment.Length > 0)
                    parts.Add(segment);
            }
            return parts;
        }
    }
}
