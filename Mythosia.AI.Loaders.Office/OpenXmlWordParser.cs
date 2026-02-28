using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Office;

namespace Mythosia.AI.Loaders.Office.Word.Parsers
{
    /// <summary>
    /// Parses .docx files using OpenXml SDK.
    /// </summary>
    public class OpenXmlWordParser : IDocumentParser
    {
        private readonly OfficeParserOptions _options;

        public OpenXmlWordParser(OfficeParserOptions? options = null)
        {
            _options = options ?? new OfficeParserOptions();
        }

        public bool CanParse(string source)
            => string.Equals(Path.GetExtension(source), ".docx", StringComparison.OrdinalIgnoreCase);

        public Task<ParsedDocument> ParseAsync(string source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            using var doc = WordprocessingDocument.Open(source, false);
            var text = doc.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;

            if (_options.NormalizeWhitespace)
                text = OfficeParserUtilities.NormalizeWhitespace(text);

            var parsed = new ParsedDocument(text);
            if (_options.IncludeMetadata)
                OfficeParserUtilities.AddPackageMetadata(doc, parsed.Metadata);

            return Task.FromResult(parsed);
        }
    }
}
