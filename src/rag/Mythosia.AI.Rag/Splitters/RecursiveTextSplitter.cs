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
        /// <summary>Maximum UTF-16 code units per chunk. A surrogate pair is kept intact even with a size of one.</summary>
        public int ChunkSize { get; set; } = 1000;

        /// <summary>Target overlap in UTF-16 code units, aligned to split boundaries. Values at least as large as ChunkSize disable overlap.</summary>
        public int ChunkOverlap { get; set; } = 200;

        /// <summary>
        /// Ordered list of separators to try when splitting.
        /// The splitter picks the first separator found in the text.
        /// Duplicate entries are ignored after their first occurrence; the supplied array is not modified.
        /// An empty string as the last entry enables character-level splitting as a last resort.
        /// </summary>
        public string[] Separators { get; set; } = new[] { "\n\n", "\n", ". ", " ", "" };

        /// <summary>
        /// When true the separator is kept at the start of the next split so that
        /// paragraph / sentence boundaries are preserved in the chunk text.
        /// When false, separators are inserted only between pieces merged into the same chunk.
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
            if (document == null) throw new ArgumentNullException(nameof(document));
            ValidateConfiguration();
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
            if (text == null) throw new ArgumentNullException(nameof(text));
            ValidateConfiguration();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var separators = new List<string>();
            foreach (var separator in Separators)
            {
                if (seen.Add(separator))
                    separators.Add(separator);
            }
            return SplitTextRecursive(text, separators);
        }

        private void ValidateConfiguration()
        {
            SplitterGuards.ValidateSize(ChunkSize, nameof(ChunkSize));
            SplitterGuards.ValidateOverlap(ChunkOverlap, nameof(ChunkOverlap));
            if (Separators == null || Separators.Any(s => s == null))
                throw new ArgumentException("Separators must be a non-null array of non-null strings.", nameof(Separators));
            foreach (var separator in Separators)
                SplitterGuards.ValidateSeparator(separator, nameof(Separators));
        }

        /// <summary>
        /// 1. Pick the first separator that exists in the text.
        /// 2. Split by that separator.
        /// 3. Merge small splits up to ChunkSize (with overlap).
        /// 4. Recurse on oversized splits with the remaining separators.
        /// </summary>
        private List<string> SplitTextRecursive(string text, IReadOnlyList<string> separators)
        {
            var finalChunks = new List<string>();
            // A negative separator index marks a completed chunk. Explicit work items
            // retain recursive depth-first ordering without consuming the call stack or
            // copying the remaining separator array for each oversized piece.
            var work = new Stack<(string Text, int SeparatorIndex)>();
            work.Push((text, 0));
            while (work.Count > 0)
            {
                var item = work.Pop();
                if (item.SeparatorIndex < 0)
                {
                    finalChunks.Add(item.Text);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(item.Text))
                    continue;
                if (item.Text.Length <= ChunkSize)
                {
                    finalChunks.Add(item.Text);
                    continue;
                }

                string chosenSep = "";
                int nextSeparator = separators.Count;
                for (int i = item.SeparatorIndex; i < separators.Count; i++)
                {
                    var sep = separators[i];
                    if (sep == "" || item.Text.IndexOf(sep, StringComparison.Ordinal) >= 0)
                    {
                        chosenSep = sep;
                        nextSeparator = i + 1;
                        break;
                    }
                }

                var nextWork = new List<(string Text, int SeparatorIndex)>();
                var pending = new List<string>();
                foreach (var piece in SplitBySeparator(item.Text, chosenSep))
                {
                    if (piece.Length <= ChunkSize)
                    {
                        pending.Add(piece);
                        continue;
                    }

                    if (pending.Count > 0)
                    {
                        foreach (var chunk in MergeSplits(pending, KeepSeparator ? "" : chosenSep))
                            nextWork.Add((chunk, -1));
                        pending.Clear();
                    }

                    if (nextSeparator < separators.Count)
                        nextWork.Add((piece, nextSeparator));
                    else
                        foreach (var chunk in SplitByLength(piece))
                            nextWork.Add((chunk, -1));
                }

                if (pending.Count > 0)
                    foreach (var chunk in MergeSplits(pending, KeepSeparator ? "" : chosenSep))
                        nextWork.Add((chunk, -1));
                for (int i = nextWork.Count - 1; i >= 0; i--)
                    work.Push(nextWork[i]);
            }

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
                // Character-level fallback, preserving complete Unicode scalar values.
                var chars = new List<string>(text.Length);
                for (int start = 0; start < text.Length;)
                {
                    int end = SplitterGuards.SafeEnd(text, start, 1);
                    chars.Add(text.Substring(start, end - start));
                    start = end;
                }
                return chars;
            }

            // Splitting one leading occurrence and then keeping the separator would
            // reconstruct exactly the same string. Reuse it, especially when a long
            // list of distinct prefixes otherwise copies the same large body at each
            // level. Like string.Split, look for the next non-overlapping occurrence.
            if (KeepSeparator && text.StartsWith(separator, StringComparison.Ordinal)
                && text.IndexOf(separator, separator.Length, StringComparison.Ordinal) < 0)
                return new List<string> { text };

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
        private List<string> MergeSplits(List<string> splits, string separator)
        {
            var chunks = new List<string>();
            var current = new Queue<string>();
            int currentLen = 0;
            int effectiveOverlap = ChunkOverlap >= ChunkSize ? 0 : ChunkOverlap;

            foreach (var split in splits)
            {
                // Would adding this split exceed ChunkSize?
                if ((long)currentLen + separator.Length + split.Length > ChunkSize && current.Count > 0)
                {
                    // Emit current chunk
                    var chunkText = string.Join(separator, current).Trim();
                    if (chunkText.Length > 0)
                        chunks.Add(chunkText);

                    // Retain tail splits for overlap (aligned to split boundaries)
                    while (current.Count > 0 && (currentLen > effectiveOverlap
                        || (long)currentLen + separator.Length + split.Length > ChunkSize))
                    {
                        currentLen -= current.Dequeue().Length;
                        if (current.Count > 0) currentLen -= separator.Length;
                    }
                }

                if (current.Count > 0) currentLen += separator.Length;
                current.Enqueue(split);
                currentLen += split.Length;
            }

            // Emit final chunk
            if (current.Count > 0)
            {
                var chunkText = string.Join(separator, current).Trim();
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
            int overlap = ChunkOverlap >= ChunkSize ? 0 : ChunkOverlap;
            for (int start = 0; start < text.Length;)
            {
                int end = SplitterGuards.SafeEnd(text, start, ChunkSize);
                var segment = text.Substring(start, end - start);
                if (segment.Length > 0)
                    parts.Add(segment);
                if (end == text.Length) break;
                int next = SplitterGuards.SafeStart(text, Math.Max(start, end - overlap));
                start = next > start ? next : end;
            }
            return parts;
        }
    }
}
