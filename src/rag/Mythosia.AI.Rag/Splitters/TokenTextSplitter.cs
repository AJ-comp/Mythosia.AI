using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Rag.Splitters
{
    /// <summary>
    /// Splits documents into chunks based on approximate token count using whitespace tokenization.
    /// For precise token counting with a specific model's tokenizer, consider extending this class.
    /// </summary>
    public class TokenTextSplitter : ITextSplitter
    {
        /// <summary>
        /// Maximum number of whitespace-delimited units per chunk, not model tokens.
        /// </summary>
        public int MaxTokensPerChunk { get; set; } = 512;

        /// <summary>
        /// Number of overlapping units. Values at least as large as MaxTokensPerChunk disable overlap.
        /// </summary>
        public int TokenOverlap { get; set; } = 50;

        /// <summary>
        /// Characters used to split text into tokens. Default is whitespace.
        /// </summary>
        public char[] TokenSeparators { get; set; } = new[] { ' ', '\t', '\n', '\r' };

        public TokenTextSplitter() { }

        public TokenTextSplitter(int maxTokensPerChunk, int tokenOverlap = 50)
        {
            MaxTokensPerChunk = maxTokensPerChunk;
            TokenOverlap = tokenOverlap;
        }

        public IReadOnlyList<RagChunk> Split(RagDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            SplitterGuards.ValidateSize(MaxTokensPerChunk, nameof(MaxTokensPerChunk));
            SplitterGuards.ValidateOverlap(TokenOverlap, nameof(TokenOverlap));
            if (TokenSeparators == null || TokenSeparators.Any(char.IsSurrogate))
                throw new ArgumentException("Token separators must be non-null and cannot contain surrogate code units.", nameof(TokenSeparators));
            if (string.IsNullOrEmpty(document.Content))
                return Array.Empty<RagChunk>();

            var words = document.Content.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return Array.Empty<RagChunk>();

            var chunks = new List<RagChunk>();
            int index = 0;
            int wordPos = 0;

            while (wordPos < words.Length)
            {
                int end = wordPos + Math.Min(MaxTokensPerChunk, words.Length - wordPos);
                var chunkWords = words.Skip(wordPos).Take(end - wordPos);
                string chunkText = string.Join(" ", chunkWords).Trim();

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

                if (end == words.Length) break;

                int advance = MaxTokensPerChunk - TokenOverlap;
                if (advance <= 0) advance = MaxTokensPerChunk;
                wordPos += advance;
            }

            return chunks;
        }
    }
}
