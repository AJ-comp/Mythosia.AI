using System;
using System.Collections.Generic;

namespace Mythosia.AI.Rag.Splitters
{
    /// <summary>
    /// Splits documents into chunks based on character count with configurable overlap.
    /// </summary>
    public class CharacterTextSplitter : ITextSplitter
    {
        /// <summary>
        /// Maximum UTF-16 code units per chunk. Surrogate pairs are kept intact;
        /// a single pair may exceed a chunk size of one.
        /// </summary>
        public int ChunkSize { get; set; } = 1000;

        /// <summary>
        /// Target number of overlapping UTF-16 code units, adjusted to separator boundaries.
        /// Values at least as large as ChunkSize disable overlap.
        /// </summary>
        public int ChunkOverlap { get; set; } = 200;

        /// <summary>
        /// Separator string to attempt to split on (e.g., "\n\n", "\n", " ").
        /// If null, splits at exact character boundaries.
        /// </summary>
        public string? Separator { get; set; } = "\n\n";

        public CharacterTextSplitter() { }

        public CharacterTextSplitter(int chunkSize, int chunkOverlap = 200, string? separator = "\n\n")
        {
            ChunkSize = chunkSize;
            ChunkOverlap = chunkOverlap;
            Separator = separator;
        }

        public IReadOnlyList<RagChunk> Split(RagDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            SplitterGuards.ValidateSize(ChunkSize, nameof(ChunkSize));
            SplitterGuards.ValidateOverlap(ChunkOverlap, nameof(ChunkOverlap));
            if (Separator != null) SplitterGuards.ValidateSeparator(Separator, nameof(Separator));
            if (string.IsNullOrEmpty(document.Content))
                return Array.Empty<RagChunk>();

            var chunks = new List<RagChunk>();
            var text = document.Content;
            int index = 0;
            int position = 0;
            int previousEnd = 0;
            int overlap = ChunkOverlap >= ChunkSize ? 0 : ChunkOverlap;

            while (position < text.Length)
            {
                int end = SplitterGuards.SafeEnd(text, position, ChunkSize);
                string chunkText;

                if (end < text.Length && !string.IsNullOrEmpty(Separator))
                {
                    // Try to find the last separator within the chunk range
                    int lastSep = text.LastIndexOf(Separator, end - 1, end - position, StringComparison.Ordinal);
                    int separatorEnd = lastSep + Separator!.Length;
                    // Never emit a chunk consisting entirely of a previous overlap.
                    if (lastSep >= position && separatorEnd > previousEnd
                        && SplitterGuards.SafeStart(text, separatorEnd) == separatorEnd)
                    {
                        end = separatorEnd;
                    }
                }

                chunkText = text.Substring(position, end - position).Trim();

                if (chunkText.Length > 0)
                {
                    var chunk = new RagChunk
                    {
                        Id = $"{document.Id}_chunk_{index}",
                        DocumentId = document.Id,
                        Content = chunkText,
                        Index = index,
                        Metadata = new Dictionary<string, string>(document.Metadata)
                    };
                    chunk.Metadata["source"] = document.Source;
                    chunk.Metadata["chunk_index"] = index.ToString();

                    chunks.Add(chunk);
                    index++;
                }

                if (end == text.Length) break;
                previousEnd = end;

                // Advance position with overlap
                int nextPosition = Math.Max(position, end - overlap);
                if (overlap > 0 && !string.IsNullOrEmpty(Separator) && nextPosition > position)
                {
                    int searchStart = nextPosition - 1;
                    int searchLength = searchStart - position + 1;
                    if (searchLength > 0)
                    {
                        int lastSep = text.LastIndexOf(Separator, searchStart, searchLength, StringComparison.Ordinal);
                        if (lastSep >= position)
                        {
                            int aligned = lastSep + Separator!.Length;
                            if (aligned > position && aligned < end)
                                nextPosition = aligned;
                        }
                    }
                }

                if (nextPosition <= position)
                    nextPosition = end; // Prevent infinite loop

                position = SplitterGuards.SafeStart(text, nextPosition);
            }

            return chunks;
        }
    }
}
