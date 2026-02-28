using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Loaders;

namespace Mythosia.AI.Loaders.Pdf
{
    /// <summary>
    /// Loads PDF documents using a configurable parser.
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

            var parser = _parser;
            if (!parser.CanParse(source))
                throw new NotSupportedException($"Parser '{parser.GetType().Name}' cannot parse '{source}'.");

            var parsed = await parser.ParseAsync(source, ct);
            var fileName = Path.GetFileName(source);

            var doc = new RagDocument
            {
                Id = fileName,
                Content = parsed.Content,
                Source = source,
                Metadata =
                {
                    ["type"] = "pdf",
                    ["filename"] = fileName,
                    ["extension"] = Path.GetExtension(source).ToLowerInvariant(),
                    ["parser"] = parser.GetType().Name
                }
            };

            foreach (var entry in parsed.Metadata)
            {
                if (!doc.Metadata.ContainsKey(entry.Key))
                    doc.Metadata[entry.Key] = entry.Value;
            }

            return new[] { doc };
        }
    }
}
