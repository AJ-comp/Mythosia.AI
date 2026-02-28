using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Office;
using A = DocumentFormat.OpenXml.Drawing;

namespace Mythosia.AI.Loaders.Office.PowerPoint.Parsers
{
    /// <summary>
    /// Parses .pptx files using OpenXml SDK.
    /// </summary>
    public class OpenXmlPowerPointParser : IDocumentParser
    {
        private readonly OfficeParserOptions _options;

        public OpenXmlPowerPointParser(OfficeParserOptions? options = null)
        {
            _options = options ?? new OfficeParserOptions();
        }

        public bool CanParse(string source)
            => string.Equals(Path.GetExtension(source), ".pptx", StringComparison.OrdinalIgnoreCase);

        public Task<ParsedDocument> ParseAsync(string source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            using var doc = PresentationDocument.Open(source, false);
            var presentationPart = doc.PresentationPart;
            var sb = new StringBuilder();
            var slideCount = 0;

            var slideIds = presentationPart?.Presentation?.SlideIdList?.Elements<SlideId>()
                ?? Enumerable.Empty<SlideId>();

            foreach (var slideId in slideIds)
            {
                ct.ThrowIfCancellationRequested();
                slideCount++;

                var slidePart = presentationPart?.GetPartById(slideId.RelationshipId) as SlidePart;
                if (slidePart?.Slide == null)
                    continue;

                if (_options.IncludeSlideNumbers)
                    sb.AppendLine($"[slide {slideCount}]");

                var texts = slidePart.Slide.Descendants<A.Text>().Select(t => t.Text);
                var slideText = string.Join(" ", texts);

                if (_options.NormalizeWhitespace)
                    slideText = OfficeParserUtilities.NormalizeWhitespace(slideText);

                if (!string.IsNullOrWhiteSpace(slideText))
                    sb.AppendLine(slideText);

                sb.AppendLine();
            }

            var parsed = new ParsedDocument(sb.ToString().Trim());
            if (_options.IncludeMetadata)
            {
                parsed.Metadata["slide_count"] = slideCount.ToString(CultureInfo.InvariantCulture);
                OfficeParserUtilities.AddPackageMetadata(doc, parsed.Metadata);
            }

            return Task.FromResult(parsed);
        }
    }
}
