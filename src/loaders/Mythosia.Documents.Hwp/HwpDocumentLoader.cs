using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.Documents.Elements;

namespace Mythosia.Documents.Hwp
{
    /// <summary>
    /// Loads HWP (Korean Hangul Word Processor) documents via DoclingDocument.
    /// </summary>
    public class HwpDocumentLoader : IDocumentLoader
    {
        private readonly IDocumentParser _parser;

        public HwpDocumentLoader(IDocumentParser? parser = null, HwpParserOptions? options = null)
        {
            if (parser != null && options != null)
                throw new ArgumentException("Pass options to the parser instance instead of the loader.");

            _parser = parser ?? new HwpParser(options);
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
            doclingDoc.TableSerializer = new SemanticTableSerializer();
            doclingDoc.Metadata["type"] = "hwp";
            doclingDoc.Metadata["filename"] = Path.GetFileName(source);
            doclingDoc.Metadata["extension"] = Path.GetExtension(source).ToLowerInvariant();
            doclingDoc.Metadata["parser"] = _parser.GetType().Name;

            return new[] { doclingDoc };
        }
    }
}
