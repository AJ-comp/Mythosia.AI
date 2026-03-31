using Mythosia.Documents.Elements;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.Documents
{
    /// <summary>
    /// Loads documents from a given source (file path, URL, etc.) and returns structured DoclingDocument objects.
    /// Implement this interface for each document type (PDF, DOCX, HTML, plain text, etc.).
    /// </summary>
    public interface IDocumentLoader
    {
        /// <summary>
        /// Loads one or more documents from the specified source.
        /// A single source may yield multiple documents (e.g., a ZIP archive or a directory).
        /// </summary>
        /// <param name="source">File path, URL, or other source identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A list of loaded documents.</returns>
        Task<IReadOnlyList<DoclingDocument>> LoadAsync(string source, CancellationToken cancellationToken = default);
    }
}
