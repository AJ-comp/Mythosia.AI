using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Office;

namespace Mythosia.AI.Loaders.Office.Excel.Parsers
{
    /// <summary>
    /// Parses .xlsx files using OpenXml SDK.
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

        public Task<ParsedDocument> ParseAsync(string source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            using var doc = SpreadsheetDocument.Open(source, false);
            var workbookPart = doc.WorkbookPart;
            if (workbookPart?.Workbook?.Sheets == null)
                return Task.FromResult(new ParsedDocument(string.Empty));

            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
            var sb = new StringBuilder();
            var sheetCount = 0;

            foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
            {
                ct.ThrowIfCancellationRequested();
                sheetCount++;

                var sheetName = sheet.Name?.Value ?? $"Sheet{sheetCount}";
                if (_options.IncludeSheetNames)
                    sb.AppendLine($"[sheet {sheetCount}: {sheetName}]");

                var worksheetPart = workbookPart.GetPartById(sheet.Id!) as WorksheetPart;
                if (worksheetPart?.Worksheet == null)
                {
                    sb.AppendLine();
                    continue;
                }

                foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
                {
                    var rowValues = new List<string>();
                    foreach (var cell in row.Elements<Cell>())
                    {
                        var cellValue = GetCellValue(cell, sharedStrings);
                        if (string.IsNullOrWhiteSpace(cellValue))
                            continue;

                        if (_options.NormalizeWhitespace)
                            cellValue = OfficeParserUtilities.NormalizeWhitespace(cellValue);

                        rowValues.Add(cellValue);
                    }

                    if (rowValues.Count > 0)
                        sb.AppendLine(string.Join("\t", rowValues));
                }

                sb.AppendLine();
            }

            var parsed = new ParsedDocument(sb.ToString().Trim());
            if (_options.IncludeMetadata)
            {
                parsed.Metadata["sheet_count"] = sheetCount.ToString(CultureInfo.InvariantCulture);
                OfficeParserUtilities.AddPackageMetadata(doc, parsed.Metadata);
            }

            return Task.FromResult(parsed);
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
