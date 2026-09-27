using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Rag
{
    /// <summary>An immutable snapshot of one document's identity, title and ordered text chunks.</summary>
    public sealed class EmbeddingDocument
    {
        /// <summary>Local document identity. Providers need not transmit this value to a remote API.</summary>
        public string DocumentId { get; }

        /// <summary>Optional document title used for retrieval-aware input formatting.</summary>
        public string? Title { get; }

        /// <summary>Original chunk texts in splitter order, copied into a read-only collection.</summary>
        public IReadOnlyList<string> Chunks { get; }

        /// <summary>Captures a document without retaining a mutable input collection.</summary>
        public EmbeddingDocument(string documentId, IEnumerable<string> chunks, string? title = null)
        {
            if (string.IsNullOrWhiteSpace(documentId))
                throw new ArgumentException("A nonblank document ID is required.", nameof(documentId));
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            var snapshot = chunks.ToArray();
            if (snapshot.Any(chunk => chunk == null))
                throw new ArgumentException("Document chunks must not contain null text.", nameof(chunks));
            DocumentId = documentId;
            Title = title;
            Chunks = Array.AsReadOnly(snapshot);
        }
    }
}
