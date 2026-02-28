using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Office.PowerPoint.Parsers;

namespace Mythosia.AI.Loaders.Office.PowerPoint
{
    /// <summary>
    /// Loads PowerPoint documents using a configurable parser strategy.
    /// </summary>
    public class PowerPointDocumentLoader : IDocumentLoader
    {
        private readonly IDocumentParser _parser;

        public PowerPointDocumentLoader(IDocumentParser? parser = null, OfficeParserOptions? options = null)
        {
            if (parser != null && options != null)
                throw new ArgumentException("Pass options to the parser instance instead of the loader.");

            _parser = parser ?? new OpenXmlPowerPointParser(options);
        }

        public async Task<IReadOnlyList<RagDocument>> LoadAsync(string source, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Source path is required.", nameof(source));
            if (!File.Exists(source))
                throw new FileNotFoundException($"Document file not found: {source}", source);

            if (!_parser.CanParse(source))
                throw new NotSupportedException($"Parser '{_parser.GetType().Name}' cannot parse '{source}'.");

            var parsed = await _parser.ParseAsync(source, ct);
            var fileName = Path.GetFileName(source);
            var extension = Path.GetExtension(source).ToLowerInvariant();

            var doc = new RagDocument
            {
                Id = fileName,
                Content = parsed.Content,
                Source = source,
                Metadata =
                {
                    ["type"] = "office",
                    ["office_type"] = "powerpoint",
                    ["filename"] = fileName,
                    ["extension"] = extension,
                    ["parser"] = _parser.GetType().Name
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
