using HwpLib.Object;
using HwpLib.Object.BodyText.Control;
using HwpLib.Object.BodyText.Control.Table;
using HwpLib.Object.BodyText.Paragraph;
using HwpLib.Object.DocInfo;
using HwpLib.Reader;
using Mythosia.Documents.Elements;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.Documents.Hwp
{
    /// <summary>
    /// Parses .hwp files using HwpLibSharp into a structured DoclingDocument.
    /// </summary>
    public class HwpParser : IDocumentParser
    {
        private readonly HwpParserOptions _options;

        // Korean heading style names used in HWP (개요 1~10, 제목)
        private static readonly Regex OutlinePattern = new Regex(
            @"^(?:개요|Outline)\s*(\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> TitleStyleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "제목", "Title"
        };

        // Heading-class style names (maps to heading level 1–9)
        private static readonly Regex HeadingPattern = new Regex(
            @"^Heading\s*(\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public HwpParser(HwpParserOptions? options = null)
        {
            _options = options ?? new HwpParserOptions();
        }

        public bool CanParse(string source)
            => string.Equals(Path.GetExtension(source), ".hwp", StringComparison.OrdinalIgnoreCase);

        public Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var docName = Path.GetFileNameWithoutExtension(source);
            var result = new DoclingDocument { Name = docName };

            var hwpFile = HWPReader.FromFile(source);

            if (_options.IncludeMetadata)
                AddMetadata(hwpFile, result.Metadata);

            // Build style lookup for heading detection
            var styleLookup = BuildStyleLookup(hwpFile);

            var sections = hwpFile.BodyText.SectionList;
            NodeItem currentParent = result.Body;

            for (int sIdx = 0; sIdx < sections.Count; sIdx++)
            {
                ct.ThrowIfCancellationRequested();

                var section = sections[sIdx];

                if (_options.IncludeSectionHeaders && sections.Count > 1)
                {
                    var sectionHeading = result.AddHeading($"Section {sIdx + 1}", 1, result.Body);
                    currentParent = sectionHeading;
                }

                for (int pIdx = 0; pIdx < section.ParagraphCount; pIdx++)
                {
                    ct.ThrowIfCancellationRequested();

                    var para = section.GetParagraph(pIdx);

                    // Process inline table controls first
                    if (para.ControlList != null)
                    {
                        foreach (var control in para.ControlList)
                        {
                            if (control.Type == ControlType.Table && control is ControlTable tableCtrl)
                            {
                                BuildTable(result, tableCtrl, currentParent);
                            }
                        }
                    }

                    var text = para.GetNormalString();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    if (_options.NormalizeWhitespace)
                        text = NormalizeWhitespace(text);

                    // Detect heading / title via style
                    var styleId = para.Header?.StyleId ?? -1;
                    var (headingLevel, isTitle) = ClassifyStyle(styleId, styleLookup);

                    if (isTitle)
                    {
                        var title = result.AddTitle(text, result.Body);
                        currentParent = title;
                    }
                    else if (headingLevel > 0)
                    {
                        var heading = result.AddHeading(text, headingLevel, currentParent);
                        currentParent = heading;
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
        //  Style classification
        // -----------------------------------------------------------------

        private static Dictionary<int, StyleInfo> BuildStyleLookup(HWPFile hwpFile)
        {
            var lookup = new Dictionary<int, StyleInfo>();
            var styles = hwpFile.DocInfo?.StyleList;
            if (styles == null) return lookup;

            for (int i = 0; i < styles.Count; i++)
            {
                lookup[i] = styles[i];
            }

            return lookup;
        }

        private static (int headingLevel, bool isTitle) ClassifyStyle(
            int styleId, Dictionary<int, StyleInfo> styleLookup)
        {
            if (styleId < 0 || !styleLookup.TryGetValue(styleId, out var style))
                return (0, false);

            // Check both Korean and English style names
            var names = new[] { style.HangulName, style.EnglishName };

            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name))
                    continue;

                if (TitleStyleNames.Contains(name))
                    return (0, true);

                var outlineMatch = OutlinePattern.Match(name);
                if (outlineMatch.Success && int.TryParse(outlineMatch.Groups[1].Value, out var level)
                    && level >= 1 && level <= 9)
                    return (level, false);

                var headingMatch = HeadingPattern.Match(name);
                if (headingMatch.Success && int.TryParse(headingMatch.Groups[1].Value, out var hLevel)
                    && hLevel >= 1 && hLevel <= 9)
                    return (hLevel, false);
            }

            return (0, false);
        }

        // -----------------------------------------------------------------
        //  Table building
        // -----------------------------------------------------------------

        private static void BuildTable(DoclingDocument doc, ControlTable tableCtrl, NodeItem parent)
        {
            var rows = tableCtrl.RowList;
            if (rows == null || rows.Count == 0)
                return;

            var tableData = new TableData();
            int totalCols = 0;

            for (int rIdx = 0; rIdx < rows.Count; rIdx++)
            {
                var row = rows[rIdx];
                int colCount = 0;

                foreach (var cell in row.CellList)
                {
                    var header = cell.ListHeader;
                    var cellText = cell.ParagraphList?.GetNormalString() ?? string.Empty;

                    var colSpan = header.ColSpan;
                    var rowSpan = header.RowSpan;
                    var colIndex = header.ColIndex;
                    var rowIndex = header.RowIndex;

                    var docCell = new Elements.TableCell
                    {
                        Text = cellText.Trim(),
                        ColSpan = colSpan,
                        RowSpan = rowSpan,
                        StartRowOffsetIdx = rowIndex,
                        EndRowOffsetIdx = rowIndex + rowSpan,
                        StartColOffsetIdx = colIndex,
                        EndColOffsetIdx = colIndex + colSpan,
                        ColumnHeader = header.Property.TitleCell,
                    };

                    tableData.TableCells.Add(docCell);

                    var endCol = colIndex + colSpan;
                    if (endCol > colCount)
                        colCount = endCol;
                }

                if (colCount > totalCols)
                    totalCols = colCount;
            }

            tableData.NumRows = rows.Count;
            tableData.NumCols = totalCols;

            doc.AddTable(tableData, parent);
        }

        // -----------------------------------------------------------------
        //  Metadata
        // -----------------------------------------------------------------

        private static void AddMetadata(HWPFile hwpFile, Dictionary<string, string> metadata)
        {
            var docProps = hwpFile.DocInfo?.DocumentProperties;
            if (docProps != null)
            {
                metadata["section_count"] = docProps.SectionCount.ToString();
            }
        }

        // -----------------------------------------------------------------
        //  Text normalization
        // -----------------------------------------------------------------

        private static string NormalizeWhitespace(string text)
        {
            // Collapse runs of spaces/tabs but preserve newlines
            var sb = new StringBuilder(text.Length);
            bool prevSpace = false;

            foreach (var ch in text)
            {
                if (ch == '\n' || ch == '\r')
                {
                    prevSpace = false;
                    sb.Append(ch);
                }
                else if (char.IsWhiteSpace(ch))
                {
                    if (!prevSpace)
                    {
                        sb.Append(' ');
                        prevSpace = true;
                    }
                }
                else
                {
                    prevSpace = false;
                    sb.Append(ch);
                }
            }

            return sb.ToString().Trim();
        }
    }
}
