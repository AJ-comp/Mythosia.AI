using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// Parses a document into a structured <see cref="DoclingDocument"/> representation.
    /// </summary>
    public interface IDocumentParser
    {
        /// <summary>
        /// Returns true if the parser can handle the given source.
        /// </summary>
        bool CanParse(string source);

        /// <summary>
        /// Parses the document and returns a structured <see cref="DoclingDocument"/>.
        /// </summary>
        Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default);
    }
}
