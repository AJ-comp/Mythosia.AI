using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Mythosia.AI.Loaders.Document;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Loaders.Office.Excel.Parsers
{
    /// <summary>
    /// Parses .xlsx files using OpenXml SDK into a structured DoclingDocument.
    /// </summary>
    public class OpenXmlExcelParser : IDocumentParser
    {
        private readonly OfficeParserOptions _options;

        public OpenXmlExcelParser(OfficeParserOptions? options = null)
        {
            _options = options ?? new OfficeParserOptions();
        }

        public bool CanParse(string source)
            => string.Equals(Path.GetExtension(source), ".xlsx", StringComparison.OrdinalIgnoreCase);

        // -----------------------------------------------------------------
        //  Structured parsing → DoclingDocument
        // -----------------------------------------------------------------

        public Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var docName = Path.GetFileNameWithoutExtension(source);
            var result = new DoclingDocument { Name = docName };

            using var spreadsheet = SpreadsheetDocument.Open(source, false);
            var workbookPart = spreadsheet.WorkbookPart;
            if (workbookPart?.Workbook?.Sheets == null)
                return Task.FromResult(result);

            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
            var sheetCount = 0;

            foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
            {
                ct.ThrowIfCancellationRequested();
                sheetCount++;

                var sheetName = sheet.Name?.Value ?? $"Sheet{sheetCount}";

                // Each sheet becomes a Group with label = Sheet
                var sheetGroup = result.AddGroup(sheetName, GroupLabel.Sheet);

                // Add sheet name as heading
                result.AddHeading(sheetName, 2, sheetGroup);

                var worksheetPart = workbookPart.GetPartById(sheet.Id!) as WorksheetPart;
                if (worksheetPart?.Worksheet == null)
                    continue;

                BuildSheetTable(result, worksheetPart, sharedStrings, sheetGroup);
            }

            return Task.FromResult(result);
        }

        private void BuildSheetTable(
            DoclingDocument doc,
            WorksheetPart worksheetPart,
            SharedStringTable? sharedStrings,
            NodeItem parent)
        {
            var rows = worksheetPart.Worksheet.Descendants<Row>().ToList();
            if (rows.Count == 0)
                return;

            // Determine max column count across all rows
            int maxCols = 0;
            var rowDataList = new List<List<string>>();

            foreach (var row in rows)
            {
                var cellValues = new List<string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    // Fill gaps for sparse columns
                    var colIndex = GetColumnIndex(cell.CellReference?.Value);
                    while (cellValues.Count < colIndex)
                        cellValues.Add(string.Empty);

                    var value = GetCellValue(cell, sharedStrings);
                    if (_options.NormalizeWhitespace && !string.IsNullOrWhiteSpace(value))
                        value = OfficeParserUtilities.NormalizeWhitespace(value);
                    cellValues.Add(value);
                }

                if (cellValues.Count > maxCols)
                    maxCols = cellValues.Count;
                rowDataList.Add(cellValues);
            }

            if (maxCols == 0)
                return;

            var tableData = new TableData
            {
                NumRows = rowDataList.Count,
                NumCols = maxCols,
            };

            for (int r = 0; r < rowDataList.Count; r++)
            {
                var rowData = rowDataList[r];
                for (int c = 0; c < maxCols; c++)
                {
                    var text = c < rowData.Count ? rowData[c] : string.Empty;
                    tableData.TableCells.Add(new Document.TableCell
                    {
                        Text = text,
                        RowSpan = 1,
                        ColSpan = 1,
                        StartRowOffsetIdx = r,
                        EndRowOffsetIdx = r + 1,
                        StartColOffsetIdx = c,
                        EndColOffsetIdx = c + 1,
                        ColumnHeader = r == 0, // first row = header
                    });
                }
            }

            doc.AddTable(tableData, parent);
        }

        private static readonly Regex ColumnLetterRegex = new Regex(@"^([A-Z]+)", RegexOptions.Compiled);

        private static int GetColumnIndex(string? cellReference)
        {
            if (string.IsNullOrEmpty(cellReference))
                return 0;

            var match = ColumnLetterRegex.Match(cellReference);
            if (!match.Success)
                return 0;

            var letters = match.Groups[1].Value;
            int index = 0;
            foreach (var ch in letters)
            {
                index = index * 26 + (ch - 'A' + 1);
            }
            return index - 1; // 0-based
        }

        private static string GetCellValue(Cell cell, SharedStringTable? sharedStrings)
        {
            if (cell == null)
                return string.Empty;

            if (cell.DataType != null)
            {
                if (cell.DataType.Value == CellValues.SharedString)
                {
                    if (int.TryParse(cell.CellValue?.InnerText, out var index)
                        && sharedStrings != null
                        && index >= 0
                        && index < sharedStrings.ChildElements.Count)
                    {
                        return sharedStrings.ChildElements[index].InnerText ?? string.Empty;
                    }

                    return string.Empty;
                }

                if (cell.DataType.Value == CellValues.InlineString)
                    return cell.InlineString?.Text?.Text ?? string.Empty;

                if (cell.DataType.Value == CellValues.Boolean)
                    return cell.CellValue?.InnerText == "1" ? "TRUE" : "FALSE";
            }

            return cell.CellValue?.InnerText ?? string.Empty;
        }
    }
}
