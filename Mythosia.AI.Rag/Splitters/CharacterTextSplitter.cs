using Mythosia.AI.Loaders;
using Mythosia.AI.Rag;
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
        /// Maximum number of characters per chunk.
        /// </summary>
        public int ChunkSize { get; set; } = 1000;

        /// <summary>
        /// Number of overlapping characters between consecutive chunks.
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
            if (string.IsNullOrEmpty(document.Content))
                return Array.Empty<RagChunk>();

            var chunks = new List<RagChunk>();
            var text = document.Content;
            int index = 0;
            int position = 0;

            while (position < text.Length)
            {
                int end = Math.Min(position + ChunkSize, text.Length);
                string chunkText;

                if (end < text.Length && Separator != null)
                {
                    // Try to find the last separator within the chunk range
                    int lastSep = text.LastIndexOf(Separator, end, end - position, StringComparison.Ordinal);
                    if (lastSep > position)
                    {
                        end = lastSep + Separator.Length;
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

                // Advance position with overlap
                int nextPosition = end - ChunkOverlap;
                if (Separator != null && nextPosition > position)
                {
                    int searchStart = Math.Min(nextPosition, text.Length - 1);
                    int searchLength = searchStart - position + 1;
                    if (searchLength > 0)
                    {
                        int lastSep = text.LastIndexOf(Separator, searchStart, searchLength, StringComparison.Ordinal);
                        if (lastSep >= position)
                        {
                            int aligned = lastSep + Separator.Length;
                            if (aligned > position && aligned < end)
                                nextPosition = aligned;
                        }
                    }
                }

                if (nextPosition <= position)
                    nextPosition = end; // Prevent infinite loop

                position = nextPosition;
            }

            return chunks;
        }
    }
}
