using System;
using System.Collections.Generic;
using System.IO;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;

namespace Mythosia.VectorDb
{
    /// <summary>
    /// BM25 tokenizer backed by Lucene.Net <see cref="StandardAnalyzer"/>.
    /// Used by in-memory BM25 indexing and Qdrant sparse vector building.
    /// </summary>
    public static class Bm25Tokenizer
    {
        public sealed class AnalysisResult
        {
            public IReadOnlyList<string> Tokens { get; }
            public Dictionary<string, int> TermFrequencies { get; }

            public AnalysisResult(IReadOnlyList<string> tokens, Dictionary<string, int> termFrequencies)
            {
                Tokens = tokens;
                TermFrequencies = termFrequencies;
            }
        }

        private static readonly LuceneVersion Version = LuceneVersion.LUCENE_48;

        /// <summary>
        /// Tokenizes the input text into a list of normalized terms.
        /// </summary>
        /// <param name="text">The input text to tokenize.</param>
        /// <returns>A list of normalized, non-stopword tokens.</returns>
        public static IReadOnlyList<string> Tokenize(string text)
            => Analyze(text).Tokens;

        /// <summary>
        /// Analyzes text in a single pass and returns both normalized tokens and term frequencies.
        /// </summary>
        /// <param name="text">The input text to analyze.</param>
        /// <returns>Analysis result containing tokens and term-frequency map.</returns>
        public static AnalysisResult Analyze(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new AnalysisResult(
                    Array.Empty<string>(),
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
            }

            var tokens = new List<string>();
            var tf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (var analyzer = new StandardAnalyzer(Version))
            using (var reader = new StringReader(text))
            using (var tokenStream = analyzer.GetTokenStream("content", reader))
            {
                var termAttr = tokenStream.AddAttribute<ICharTermAttribute>();
                tokenStream.Reset();
                while (tokenStream.IncrementToken())
                {
                    var token = termAttr.ToString();
                    if (token.Length < 2)
                        continue;

                    tokens.Add(token);
                    if (tf.TryGetValue(token, out var count))
                        tf[token] = count + 1;
                    else
                        tf[token] = 1;
                }
                tokenStream.End();
            }

            return new AnalysisResult(tokens, tf);
        }

        /// <summary>
        /// Computes term frequencies for a tokenized document.
        /// </summary>
        /// <param name="tokens">The tokens to compute frequencies for.</param>
        /// <returns>A dictionary mapping each token to its frequency.</returns>
        public static Dictionary<string, int> ComputeTermFrequencies(IReadOnlyList<string> tokens)
        {
            var tf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in tokens)
            {
                if (tf.TryGetValue(token, out var count))
                    tf[token] = count + 1;
                else
                    tf[token] = 1;
            }
            return tf;
        }
    }
}
