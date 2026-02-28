using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NPOI.HWPF;
using NPOI.HWPF.Extractor;

namespace Mythosia.AI.Loaders.Office.Compat.Word.Parsers
{
    /// <summary>
    /// Parses legacy .doc files using NPOI.
    /// </summary>
    public class NpoiDocParser : IDocumentParser
    {
        private readonly OfficeCompatParserOptions _options;

        public NpoiDocParser(OfficeCompatParserOptions? options = null)
        {
            _options = options ?? new OfficeCompatParserOptions();
        }

        public bool CanParse(string source)
            => string.Equals(Path.GetExtension(source), ".doc", StringComparison.OrdinalIgnoreCase);

        public Task<ParsedDocument> ParseAsync(string source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            using var stream = File.OpenRead(source);
            var doc = new HWPFDocument(stream);
            var extractor = new WordExtractor(doc);
            var text = extractor.Text ?? string.Empty;

            if (_options.NormalizeWhitespace)
                text = OfficeCompatParserUtilities.NormalizeWhitespace(text);

            var parsed = new ParsedDocument(text);
            return Task.FromResult(parsed);
        }
    }
}
