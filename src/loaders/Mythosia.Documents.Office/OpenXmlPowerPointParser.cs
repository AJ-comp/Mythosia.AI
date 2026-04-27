using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Mythosia.Documents.Elements;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Mythosia.Documents.Office.PowerPoint.Parsers
{
    /// <summary>
    /// Parses .pptx files using OpenXml SDK into a structured DoclingDocument.
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

        // -----------------------------------------------------------------
        //  Structured parsing ??DoclingDocument
        // -----------------------------------------------------------------

        public Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var docName = Path.GetFileNameWithoutExtension(source);
            var result = new DoclingDocument { Name = docName };

            using var pptDoc = PresentationDocument.Open(source, false);

            if (_options.IncludeMetadata)
                OfficeParserUtilities.AddPackageMetadata(pptDoc, result.Metadata);

            var presentationPart = pptDoc.PresentationPart;
            var slideCount = 0;

            var slideIds = presentationPart?.Presentation?.SlideIdList?.Elements<SlideId>()
                ?? Enumerable.Empty<SlideId>();

            foreach (var slideId in slideIds)
            {
                ct.ThrowIfCancellationRequested();
                slideCount++;

                var slidePart = presentationPart?.GetPartById(slideId.RelationshipId!) as SlidePart;
                if (slidePart?.Slide == null)
                    continue;

                // Each slide becomes a Group with label = Slide
                var slideGroup = result.AddGroup($"Slide {slideCount}", GroupLabel.Slide);

                BuildSlide(result, slidePart, slideCount, slideGroup);
            }

            return Task.FromResult(result);
        }

        private void BuildSlide(DoclingDocument doc, SlidePart slidePart, int slideNumber, NodeItem parent)
        {
            // Walk shapes and graphic frames in document order so tables appear
            // at their actual position relative to text bodies.
            foreach (var element in slidePart.Slide.Descendants())
            {
                if (element is P.Shape shape)
                {
                    BuildShape(doc, shape, parent);
                }
                else if (element is P.GraphicFrame gf)
                {
                    var tbl = gf.Descendants<A.Table>().FirstOrDefault();
                    if (tbl != null)
                        BuildSlideTable(doc, tbl, parent);
                }
            }
        }

        private void BuildShape(DoclingDocument doc, P.Shape shape, NodeItem parent)
        {
            var nvSpPr = shape.NonVisualShapeProperties;
            var phType = nvSpPr?.ApplicationNonVisualDrawingProperties?
                .GetFirstChild<P.PlaceholderShape>()?.Type?.Value;

            var textBody = shape.TextBody;
            if (textBody == null)
                return;

            var paragraphs = textBody.Elements<A.Paragraph>().ToList();
            bool isTitle = phType == PlaceholderValues.Title
                        || phType == PlaceholderValues.CenteredTitle;

            foreach (var para in paragraphs)
            {
                var runs = para.Elements<A.Run>().ToList();
                var text = string.Join("", runs.Select(r => r.Text?.Text ?? ""));

                if (_options.NormalizeWhitespace && !string.IsNullOrWhiteSpace(text))
                    text = OfficeParserUtilities.NormalizeWhitespace(text);

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (isTitle)
                {
                    doc.AddHeading(text, 2, parent);
                    isTitle = false; // only first paragraph as title
                }
                else
                {
                    var pPr = para.ParagraphProperties;
                    var buChar = pPr?.GetFirstChild<A.CharacterBullet>();
                    var buAutoNum = pPr?.GetFirstChild<A.AutoNumberedBullet>();

                    if (buAutoNum != null)
                    {
                        doc.AddListItem(text, enumerated: true, marker: "1.", parent: parent);
                    }
                    else if (buChar != null)
                    {
                        var marker = buChar.Char?.Value ?? "-";
                        doc.AddListItem(text, enumerated: false, marker: marker, parent: parent);
                    }
                    else
                    {
                        doc.AddParagraph(text, parent);
                    }
                }
            }
        }

        private static void BuildSlideTable(DoclingDocument doc, A.Table tbl, NodeItem parent)
        {
            var rows = tbl.Elements<A.TableRow>().ToList();
            if (rows.Count == 0) return;

            int numCols = rows.Max(r => r.Elements<A.TableCell>().Count());
            var tableData = new TableData
            {
                NumRows = rows.Count,
                NumCols = numCols,
            };

            for (int r = 0; r < rows.Count; r++)
            {
                var cells = rows[r].Elements<A.TableCell>().ToList();
                for (int c = 0; c < cells.Count; c++)
                {
                    var cellText = string.Join(" ",
                        cells[c].Descendants<A.Text>().Select(t => t.Text ?? "")).Trim();

                    tableData.TableCells.Add(new Elements.TableCell
                    {
                        Text = cellText,
                        RowSpan = 1,
                        ColSpan = 1,
                        StartRowOffsetIdx = r,
                        EndRowOffsetIdx = r + 1,
                        StartColOffsetIdx = c,
                        EndColOffsetIdx = c + 1,
                        ColumnHeader = r == 0,
                    });
                }
            }

            doc.AddTable(tableData, parent);
        }

    }
}
