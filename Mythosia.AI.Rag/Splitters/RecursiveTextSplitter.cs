using Mythosia.AI.Loaders;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Rag.Splitters
{
    /// <summary>
    /// Recursively splits text using an ordered list of separators (LangChain-style).
    /// </summary>
    public class RecursiveTextSplitter : ITextSplitter
    {
        /// <summary>
        /// Maximum number of characters per chunk.
        /// </summary>
        public int ChunkSize { get; set; } = 1000;

        /// <summary>
        /// Number of overlapping characters between consecutive chunks.
        /// </summary>
        public int ChunkOverlap { get; set; } = 200;

        /// <summary>
        /// Ordered list of separators to try when splitting.
        /// </summary>
        public string[] Separators { get; set; } = new[] { "\n\n", "\n", " ", "" };

        public RecursiveTextSplitter() { }

        public RecursiveTextSplitter(int chunkSize, int chunkOverlap = 200, IEnumerable<string>? separators = null)
        {
            ChunkSize = chunkSize;
            ChunkOverlap = chunkOverlap;
            if (separators != null)
                Separators = separators.ToArray();
        }

        public IReadOnlyList<RagChunk> Split(RagDocument document)
        {
            if (string.IsNullOrEmpty(document.Content))
                return Array.Empty<RagChunk>();

            var splits = SplitText(document.Content, 0);
            var merged = MergeSplits(splits);

            var chunks = new List<RagChunk>();
            int index = 0;

            foreach (var text in merged)
            {
                var content = text.Trim();
                if (content.Length == 0)
                    continue;

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

                chunks.Add(chunk);
                index++;
            }

            return chunks;
        }

        private List<string> SplitText(string text, int separatorIndex)
        {
            if (text.Length <= ChunkSize)
                return new List<string> { text };

            if (separatorIndex >= Separators.Length)
                return SplitByLength(text);

            var separator = Separators[separatorIndex];
            if (string.IsNullOrEmpty(separator))
                return SplitByLength(text);

            var splits = SplitBySeparator(text, separator);
            var result = new List<string>();

            foreach (var split in splits)
            {
                if (split.Length <= ChunkSize)
                    result.Add(split);
                else
                    result.AddRange(SplitText(split, separatorIndex + 1));
            }

            return result;
        }

        private List<string> SplitBySeparator(string text, string separator)
        {
            var parts = new List<string>();
            int start = 0;

            while (start < text.Length)
            {
                int index = text.IndexOf(separator, start, StringComparison.Ordinal);
                if (index < 0)
                {
                    var tail = text.Substring(start);
                    if (tail.Length > 0)
                        parts.Add(tail);
                    break;
                }

                int end = index + separator.Length;
                var segment = text.Substring(start, end - start);
                if (segment.Length > 0)
                    parts.Add(segment);
                start = end;
            }

            return parts;
        }

        private List<string> SplitByLength(string text)
        {
            var parts = new List<string>();
            for (int start = 0; start < text.Length; start += ChunkSize)
            {
                int length = Math.Min(ChunkSize, text.Length - start);
                var segment = text.Substring(start, length);
                if (segment.Length > 0)
                    parts.Add(segment);
            }

            return parts;
        }

        private List<string> MergeSplits(List<string> splits)
        {
            var docs = new List<string>();
            var current = new List<string>();
            int currentLength = 0;
            int overlapTarget = ChunkOverlap >= ChunkSize ? 0 : ChunkOverlap;

            foreach (var split in splits)
            {
                if (currentLength + split.Length > ChunkSize && current.Count > 0)
                {
                    docs.Add(string.Concat(current).Trim());
                    current = BuildOverlap(current, overlapTarget);
                    currentLength = current.Sum(segment => segment.Length);
                }

                current.Add(split);
                currentLength += split.Length;
            }

            if (current.Count > 0)
                docs.Add(string.Concat(current).Trim());

            return docs;
        }

        private static List<string> BuildOverlap(List<string> current, int overlapTarget)
        {
            if (overlapTarget <= 0 || current.Count == 0)
                return new List<string>();

            var overlap = new List<string>();
            int length = 0;

            for (int i = current.Count - 1; i >= 0 && length < overlapTarget; i--)
            {
                overlap.Insert(0, current[i]);
                length += current[i].Length;
            }

            return overlap;
        }
    }
}
