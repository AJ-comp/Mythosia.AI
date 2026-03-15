using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Document;

namespace Mythosia.AI.Loaders.Pdf
{
    /// <summary>
    /// Loads PDF documents via DoclingDocument → MarkdownSerializer.
    /// </summary>
    public class PdfDocumentLoader : IDocumentLoader
    {
        private readonly IDocumentParser _parser;

        public PdfDocumentLoader(
            IDocumentParser? parser = null,
            PdfParserOptions? options = null)
        {
            if (parser != null && options != null)
                throw new ArgumentException("Pass options to the parser instance instead of the loader.");

            _parser = parser ?? new PdfPigParser(options);
        }

        public async Task<IReadOnlyList<RagDocument>> LoadAsync(string source, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Source path is required.", nameof(source));
            if (!File.Exists(source))
                throw new FileNotFoundException($"Document file not found: {source}", source);

            if (!_parser.CanParse(source))
                throw new NotSupportedException($"Parser '{_parser.GetType().Name}' cannot parse '{source}'.");

            var doclingDoc = await _parser.ParseAsync(source, ct);
            var content = doclingDoc.ToMarkdown();

            var fileName = Path.GetFileName(source);

            var doc = new RagDocument
            {
                Id = fileName,
                Content = content,
                Source = source,
                Metadata =
                {
                    ["type"] = "pdf",
                    ["filename"] = fileName,
                    ["extension"] = Path.GetExtension(source).ToLowerInvariant(),
                    ["parser"] = _parser.GetType().Name
                }
            };

            return new[] { doc };
        }
    }
}
