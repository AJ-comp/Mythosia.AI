using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.VectorDb.InMemory
{
    /// <summary>
    /// In-memory BM25 index for keyword-based document search.
    /// Uses <see cref="Bm25Tokenizer"/> for text tokenization.
    /// Thread-safe for concurrent reads and writes.
    /// </summary>
    public class Bm25Index
    {
        private const string IdField = "id";
        private const string ContentField = "content";
        private static readonly LuceneVersion Version = LuceneVersion.LUCENE_48;

        /// <summary>
        /// A single BM25 search result.
        /// </summary>
        public class Bm25Result
        {
            /// <summary>Document identifier.</summary>
            public string Id { get; }

            /// <summary>Original document content.</summary>
            public string Content { get; }

            /// <summary>BM25 relevance score.</summary>
            public double Score { get; }

            public Bm25Result(string id, string content, double score)
            {
                Id = id;
                Content = content;
                Score = score;
            }
        }

        private readonly object _indexLock = new object();
        private readonly RAMDirectory _directory = new RAMDirectory();
        private readonly StandardAnalyzer _analyzer = new StandardAnalyzer(Version);
        private readonly IndexWriter _writer;

        public Bm25Index()
        {
            var config = new IndexWriterConfig(Version, _analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND,
                Similarity = new BM25Similarity()
            };
            _writer = new IndexWriter(_directory, config);
        }

        /// <summary>
        /// Indexes a document. If a document with the same ID exists, it is replaced.
        /// </summary>
        /// <param name="id">Document identifier.</param>
        /// <param name="text">Document text content.</param>
        public void Index(string id, string text)
        {
            lock (_indexLock)
            {
                var doc = new Document
                {
                    new StringField(IdField, id, Field.Store.YES),
                    new TextField(ContentField, text ?? string.Empty, Field.Store.YES)
                };

                _writer.UpdateDocument(new Term(IdField, id), doc);
                _writer.Flush(triggerMerge: false, applyAllDeletes: true);
            }
        }

        /// <summary>
        /// Removes a document from the index.
        /// </summary>
        /// <param name="id">Document identifier to remove.</param>
        public void Remove(string id)
        {
            lock (_indexLock)
            {
                _writer.DeleteDocuments(new Term(IdField, id));
                _writer.Flush(triggerMerge: false, applyAllDeletes: true);
            }
        }

        /// <summary>
        /// Searches the index using BM25 scoring.
        /// </summary>
        /// <param name="query">The search query text.</param>
        /// <param name="topK">Maximum number of results to return.</param>
        /// <returns>BM25 results sorted by score descending.</returns>
        public IReadOnlyList<Bm25Result> Search(string query, int topK)
        {
            var queryTokens = Bm25Tokenizer.Tokenize(query);
            if (queryTokens.Count == 0)
                return Array.Empty<Bm25Result>();

            lock (_indexLock)
            {
                _writer.Commit();

                using var reader = _writer.GetReader(applyAllDeletes: true);
                if (reader.NumDocs == 0)
                    return Array.Empty<Bm25Result>();

                var searcher = new IndexSearcher(reader)
                {
                    Similarity = new BM25Similarity()
                };

                var booleanQuery = new BooleanQuery();
                foreach (var token in queryTokens.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    booleanQuery.Add(new TermQuery(new Term(ContentField, token)), Occur.SHOULD);
                }

                if (booleanQuery.Clauses.Count == 0)
                    return Array.Empty<Bm25Result>();

                var topDocs = searcher.Search(booleanQuery, topK);
                var results = new List<Bm25Result>(topDocs.ScoreDocs.Length);
                foreach (var scoreDoc in topDocs.ScoreDocs)
                {
                    var doc = searcher.Doc(scoreDoc.Doc);
                    var id = doc.Get(IdField);
                    var content = doc.Get(ContentField) ?? string.Empty;
                    results.Add(new Bm25Result(id, content, scoreDoc.Score));
                }

                return results;
            }
        }

        /// <summary>
        /// Returns the number of indexed documents.
        /// </summary>
        public int DocumentCount
        {
            get
            {
                lock (_indexLock)
                {
                    using var reader = _writer.GetReader(applyAllDeletes: true);
                    return reader.NumDocs;
                }
            }
        }
    }
}
