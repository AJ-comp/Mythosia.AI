using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Mythosia.AI.Loaders.Document;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Loaders.Office.Word.Parsers
{
    /// <summary>
    /// Parses .docx files using OpenXml SDK into a structured DoclingDocument.
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

        // -----------------------------------------------------------------
        //  Structured parsing → DoclingDocument
        // -----------------------------------------------------------------

        public Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var docName = Path.GetFileNameWithoutExtension(source);
            var result = new DoclingDocument { Name = docName };

            using var wpDoc = WordprocessingDocument.Open(source, false);
            var body = wpDoc.MainDocumentPart?.Document?.Body;
            if (body == null)
                return Task.FromResult(result);

            var numberingPart = wpDoc.MainDocumentPart?.NumberingDefinitionsPart;
            NodeItem currentParent = result.Body;

            foreach (var element in body.ChildElements)
            {
                ct.ThrowIfCancellationRequested();

                if (element is Table table)
                {
                    BuildTable(result, table, currentParent);
                }
                else if (element is Paragraph para)
                {
                    var text = para.InnerText;
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    var styleId = GetParagraphStyleId(para);
                    var headingLevel = GetHeadingLevel(styleId);

                    if (headingLevel > 0)
                    {
                        var heading = result.AddHeading(text, headingLevel, currentParent);
                        // Subsequent items become children of this heading
                        currentParent = heading;
                    }
                    else if (IsTitle(styleId))
                    {
                        var title = result.AddTitle(text, result.Body);
                        currentParent = title;
                    }
                    else if (IsListParagraph(para, numberingPart))
                    {
                        var (enumerated, marker) = GetListInfo(para, numberingPart);
                        result.AddListItem(text, enumerated, marker, currentParent);
                    }
                    else
                    {
                        result.AddParagraph(text, currentParent);
                    }
                }
            }

            return Task.FromResult(result);
        }

        // -----------------------------------------------------------------
        //  Heading / style detection
        // -----------------------------------------------------------------

        private static string? GetParagraphStyleId(Paragraph para)
        {
            return para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        }

        /// <summary>
        /// Maps Word heading style IDs to heading levels.
        /// Word uses "Heading1"–"Heading9" (en) and localized variants.
        /// </summary>
        private static int GetHeadingLevel(string? styleId)
        {
            if (string.IsNullOrEmpty(styleId))
                return 0;

            // Standard: "Heading1" .. "Heading9"
            if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                && styleId.Length <= 8
                && int.TryParse(styleId.Substring(7), out var level)
                && level >= 1 && level <= 9)
            {
                return level;
            }

            // Alternate format: "heading 1" etc.
            if (styleId.StartsWith("heading ", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(styleId.Substring(8).Trim(), out var level2)
                && level2 >= 1 && level2 <= 9)
            {
                return level2;
            }

            return 0;
        }

        private static bool IsTitle(string? styleId)
        {
            if (string.IsNullOrEmpty(styleId))
                return false;

            return string.Equals(styleId, "Title", StringComparison.OrdinalIgnoreCase);
        }

        // -----------------------------------------------------------------
        //  List detection
        // -----------------------------------------------------------------

        private static bool IsListParagraph(Paragraph para, NumberingDefinitionsPart? numberingPart)
        {
            var numPr = para.ParagraphProperties?.NumberingProperties;
            if (numPr?.NumberingId?.Val != null)
                return true;

            var styleId = GetParagraphStyleId(para);
            return string.Equals(styleId, "ListParagraph", StringComparison.OrdinalIgnoreCase);
        }

        private static (bool enumerated, string marker) GetListInfo(Paragraph para, NumberingDefinitionsPart? numberingPart)
        {
            var numPr = para.ParagraphProperties?.NumberingProperties;
            if (numPr?.NumberingId?.Val == null || numberingPart?.Numbering == null)
                return (false, "-");

            var numId = numPr.NumberingId.Val.Value;
            var ilvl = numPr.NumberingLevelReference?.Val?.Value ?? 0;

            var numInstance = numberingPart.Numbering
                .Elements<NumberingInstance>()
                .FirstOrDefault(n => n.NumberID?.Value == numId);

            if (numInstance?.AbstractNumId?.Val == null)
                return (false, "-");

            var abstractNumId = numInstance.AbstractNumId.Val.Value;
            var abstractNum = numberingPart.Numbering
                .Elements<AbstractNum>()
                .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);

            var levelDef = abstractNum?.Elements<Level>()
                .FirstOrDefault(l => l.LevelIndex?.Value == ilvl);

            if (levelDef?.NumberingFormat?.Val == null)
                return (false, "-");

            var fmt = levelDef.NumberingFormat.Val.Value;
            var enumerated = fmt == NumberFormatValues.Decimal
                          || fmt == NumberFormatValues.UpperLetter
                          || fmt == NumberFormatValues.LowerLetter
                          || fmt == NumberFormatValues.UpperRoman
                          || fmt == NumberFormatValues.LowerRoman;

            var marker = enumerated ? "1." : "-";
            return (enumerated, marker);
        }

        // -----------------------------------------------------------------
        //  Table building
        // -----------------------------------------------------------------

        private static void BuildTable(DoclingDocument doc, Table table, NodeItem parent)
        {
            var rows = table.Elements<TableRow>().ToList();
            if (rows.Count == 0)
                return;

            var tableData = new TableData();
            int rowIndex = 0;

            // Track merged cells via grid column tracking
            // vMerge continuation tracking: colIndex → remaining rows to skip
            var vMergeCells = new Dictionary<int, Document.TableCell>();

            foreach (var row in rows)
            {
                var cells = row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>().ToList();
                int colIndex = 0;

                foreach (var cell in cells)
                {
                    // Skip columns occupied by vertical merges
                    while (vMergeCells.ContainsKey(colIndex))
                    {
                        var existingCell = vMergeCells[colIndex];
                        // Extend the vertical merge
                        existingCell.EndRowOffsetIdx = rowIndex + 1;
                        existingCell.RowSpan = existingCell.EndRowOffsetIdx - existingCell.StartRowOffsetIdx;

                        // Check if this row continues the vMerge
                        var vMerge = cell.TableCellProperties?.VerticalMerge;
                        if (vMerge != null && vMerge.Val == null)
                        {
                            // continuation cell — skip
                            colIndex += existingCell.ColSpan;
                            break;
                        }
                        else
                        {
                            // vMerge ended
                            vMergeCells.Remove(colIndex);
                            break;
                        }
                    }

                    var cellText = GetCellText(cell);

                    // Determine column span
                    var gridSpan = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;

                    // Determine if this is the start of a vertical merge
                    var vMergeProp = cell.TableCellProperties?.VerticalMerge;
                    bool isVMergeStart = vMergeProp != null
                        && vMergeProp.Val != null
                        && vMergeProp.Val.Value == MergedCellValues.Restart;
                    bool isVMergeContinue = vMergeProp != null && vMergeProp.Val == null;

                    if (isVMergeContinue && vMergeCells.ContainsKey(colIndex))
                    {
                        // Continuation of existing vertical merge
                        var existingCell = vMergeCells[colIndex];
                        existingCell.EndRowOffsetIdx = rowIndex + 1;
                        existingCell.RowSpan = existingCell.EndRowOffsetIdx - existingCell.StartRowOffsetIdx;
                        colIndex += gridSpan;
                        continue;
                    }

                    // Detect if first row (treat as column header)
                    bool isHeader = rowIndex == 0;

                    var docCell = new Document.TableCell
                    {
                        Text = cellText,
                        RowSpan = 1,
                        ColSpan = gridSpan,
                        StartRowOffsetIdx = rowIndex,
                        EndRowOffsetIdx = rowIndex + 1,
                        StartColOffsetIdx = colIndex,
                        EndColOffsetIdx = colIndex + gridSpan,
                        ColumnHeader = isHeader,
                    };

                    tableData.TableCells.Add(docCell);

                    if (isVMergeStart)
                    {
                        vMergeCells[colIndex] = docCell;
                    }

                    colIndex += gridSpan;
                }

                // Update max columns
                if (colIndex > tableData.NumCols)
                    tableData.NumCols = colIndex;

                rowIndex++;
            }

            tableData.NumRows = rowIndex;

            // Clean up any remaining vMerge tracking
            vMergeCells.Clear();

            doc.AddTable(tableData, parent);
        }

        private static string GetCellText(DocumentFormat.OpenXml.Wordprocessing.TableCell cell)
        {
            var parts = cell.Descendants<Paragraph>()
                .Select(p => p.InnerText?.Trim() ?? string.Empty)
                .Where(t => t.Length > 0);
            return string.Join(" ", parts);
        }
    }
}
