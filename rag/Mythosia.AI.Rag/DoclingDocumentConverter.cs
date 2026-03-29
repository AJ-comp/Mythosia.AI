using Mythosia.Documents;
using System.Collections.Generic;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Converts <see cref="DoclingDocument"/> instances into <see cref="RagDocument"/> models
    /// used by the RAG pipeline (split → embed → store).
    /// </summary>
    public static class DoclingDocumentConverter
    {
        /// <summary>
        /// Converts a single <see cref="DoclingDocument"/> to a <see cref="RagDocument"/>.
        /// </summary>
        public static RagDocument ToRagDocument(DoclingDocument doc)
        {
            var ragDoc = new RagDocument
            {
                Id = !string.IsNullOrEmpty(doc.Source) ? doc.Source : doc.Name,
                Content = doc.ToMarkdown(),
                Source = doc.Source,
            };

            foreach (var kvp in doc.Metadata)
                ragDoc.Metadata[kvp.Key] = kvp.Value;

            return ragDoc;
        }

        /// <summary>
        /// Converts a list of <see cref="DoclingDocument"/> to <see cref="RagDocument"/> models.
        /// </summary>
        public static IReadOnlyList<RagDocument> ToRagDocuments(IReadOnlyList<DoclingDocument> docs)
        {
            var result = new RagDocument[docs.Count];
            for (int i = 0; i < docs.Count; i++)
                result[i] = ToRagDocument(docs[i]);
            return result;
        }
    }
}
