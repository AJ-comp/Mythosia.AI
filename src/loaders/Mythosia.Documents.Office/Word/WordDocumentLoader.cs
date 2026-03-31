using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.Documents;
using Mythosia.Documents.Elements;
using Mythosia.Documents.Office.Word.Parsers;

namespace Mythosia.Documents.Office.Word
{
    /// <summary>
    /// Loads Word documents via DoclingDocument → MarkdownSerializer.
    /// </summary>
    public class WordDocumentLoader : IDocumentLoader
    {
        private readonly IDocumentParser _parser;

        public WordDocumentLoader(IDocumentParser? parser = null, OfficeParserOptions? options = null)
        {
            if (parser != null && options != null)
                throw new ArgumentException("Pass options to the parser instance instead of the loader.");

            _parser = parser ?? new OpenXmlWordParser(options);
        }

        public async Task<IReadOnlyList<DoclingDocument>> LoadAsync(string source, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Source path is required.", nameof(source));
            if (!File.Exists(source))
                throw new FileNotFoundException($"Document file not found: {source}", source);

            if (!_parser.CanParse(source))
                throw new NotSupportedException($"Parser '{_parser.GetType().Name}' cannot parse '{source}'.");

            var doclingDoc = await _parser.ParseAsync(source, ct);
            doclingDoc.Source = source;
            doclingDoc.Metadata["type"] = "office";
            doclingDoc.Metadata["office_type"] = "word";
            doclingDoc.Metadata["filename"] = Path.GetFileName(source);
            doclingDoc.Metadata["extension"] = Path.GetExtension(source).ToLowerInvariant();
            doclingDoc.Metadata["parser"] = _parser.GetType().Name;

            return new[] { doclingDoc };
        }
    }
}
