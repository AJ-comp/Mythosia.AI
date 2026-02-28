using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Loaders
{
    /// <summary>
    /// Parses a document into extracted text and metadata.
    /// </summary>
    public interface IDocumentParser
    {
        /// <summary>
        /// Returns true if the parser can handle the given source.
        /// </summary>
        bool CanParse(string source);

        /// <summary>
        /// Parses the document and returns extracted content.
        /// </summary>
        Task<ParsedDocument> ParseAsync(string source, CancellationToken ct = default);
    }
}
