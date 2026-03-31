using Mythosia.Documents.Elements;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Mythosia.Documents.Pdf
{
    /// <summary>
    /// Parses PDF files using PdfPig into a structured DoclingDocument.
    /// Extracts headings (via font-size analysis), lists (via prefix detection),
    /// and paragraphs (via spatial line grouping).
    /// </summary>
    public class PdfPigParser : IDocumentParser
    {
        private readonly PdfParserOptions _options;

        // Font size ratio above body size → heading
        private const double HeadingFontSizeRatio = 1.15;
        // Words within this fraction of line height are on the same line
        private const double LineYToleranceFraction = 0.35;
        // Vertical gap exceeding this ratio of line height → new paragraph
        private const double ParagraphGapRatio = 1.4;
        // Headings must be shorter than this
        private const int MaxHeadingLength = 200;

        private static readonly Regex BulletPattern = new Regex(
            @"^[\u2022\u2023\u25E6\u2043\u2219\u25CF\u25CB\u25AA\u25A0\-\*]\s+",
            RegexOptions.Compiled);

        private static readonly Regex NumberedListPattern = new Regex(
            @"^(\d{1,3}[\.\)]\s+|[a-zA-Z][\.\)]\s+|[ivxIVX]{1,4}[\.\)]\s+)",
            RegexOptions.Compiled);

        private static readonly Regex MultiSpacePattern = new Regex(@"\s+", RegexOptions.Compiled);

        public PdfPigParser(PdfParserOptions? options = null)
        {
            _options = options ?? new PdfParserOptions();
        }

        public bool CanParse(string source)
            => string.Equals(Path.GetExtension(source), ".pdf", StringComparison.OrdinalIgnoreCase);

        // -----------------------------------------------------------------
        //  Main entry point
        // -----------------------------------------------------------------

        public Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var docName = Path.GetFileNameWithoutExtension(source);
            var result = new DoclingDocument { Name = docName };

            using var document = OpenDocument(source);

            // Single pass: collect structured page data and font sizes
            var allPages = new List<PageData>();
            var allFontSizes = new List<double>();
            var pageCount = 0;

            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                pageCount++;

                var words = page.GetWords().ToList();
                if (words.Count == 0)
                {
                    // Fallback: some PDFs expose page.Text but no extractable words
                    var pd = new PageData(pageCount);
                    var raw = page.Text;
                    if (!string.IsNullOrWhiteSpace(raw))
                        pd.FallbackText = NormalizeParagraph(raw);
                    allPages.Add(pd);
                    continue;
                }

                var lines = GroupWordsIntoLines(words);
                allPages.Add(new PageData(pageCount, lines));

                foreach (var line in lines)
                    allFontSizes.Add(line.FontSize);
            }

            // Determine body font size (mode of all extracted lines)
            var bodyFontSize = ComputeBodyFontSize(allFontSizes);

            // Emit structured content
            foreach (var pageData in allPages)
            {
                bool hasContent = pageData.Lines.Count > 0 || pageData.FallbackText != null;
                if (_options.IncludePageNumbers && hasContent)
                    result.AddHeading($"Page {pageData.PageNumber}", 2);

                if (pageData.FallbackText != null && pageData.Lines.Count == 0)
                    result.AddParagraph(pageData.FallbackText);
                else
                    EmitPageContent(result, pageData.Lines, bodyFontSize);
            }

            result.Metadata["page_count"] = pageCount.ToString();

            if (_options.IncludeMetadata)
                AddDocumentInfoMetadata(document, result.Metadata);

            return Task.FromResult(result);
        }

        // -----------------------------------------------------------------
        //  Line grouping from words
        // -----------------------------------------------------------------

        private List<LineData> GroupWordsIntoLines(List<Word> words)
        {
            // Sort top-down (descending baseline Y), then left-right
            var sorted = words
                .OrderByDescending(w => w.BoundingBox.Bottom)
                .ThenBy(w => w.BoundingBox.Left)
                .ToList();

            var lines = new List<LineData>();
            var currentWords = new List<Word> { sorted[0] };
            var currentBaseline = sorted[0].BoundingBox.Bottom;
            var currentHeight = sorted[0].BoundingBox.Height;

            for (int i = 1; i < sorted.Count; i++)
            {
                var word = sorted[i];
                var yDiff = Math.Abs(word.BoundingBox.Bottom - currentBaseline);
                var tolerance = Math.Max(currentHeight * LineYToleranceFraction, 1.5);

                if (yDiff <= tolerance)
                {
                    currentWords.Add(word);
                }
                else
                {
                    lines.Add(BuildLine(currentWords));
                    currentWords = new List<Word> { word };
                    currentBaseline = word.BoundingBox.Bottom;
                    currentHeight = word.BoundingBox.Height;
                }
            }

            if (currentWords.Count > 0)
                lines.Add(BuildLine(currentWords));

            return lines;
        }

        private static LineData BuildLine(List<Word> words)
        {
            words.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));

            var text = string.Join(" ", words.Select(w => w.Text));

            // Dominant font size: mode of rounded point sizes from letters
            var letters = words.SelectMany(w => w.Letters).ToList();
            double fontSize = 12; // fallback
            bool isBold = false;

            if (letters.Count > 0)
            {
                fontSize = letters
                    .GroupBy(l => Math.Round(l.PointSize, 1))
                    .OrderByDescending(g => g.Count())
                    .First().Key;

                isBold = letters.Count(l =>
                    (l.FontName ?? "").IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0)
                    > letters.Count / 2;
            }

            return new LineData
            {
                Text = text,
                FontSize = fontSize,
                IsBold = isBold,
                BaselineY = words.Average(w => w.BoundingBox.Bottom),
                LineHeight = words.Max(w => w.BoundingBox.Height),
            };
        }

        // -----------------------------------------------------------------
        //  Structured content emission
        // -----------------------------------------------------------------

        private void EmitPageContent(DoclingDocument doc, List<LineData> lines, double bodyFontSize)
        {
            if (lines.Count == 0) return;

            var headingThreshold = bodyFontSize * HeadingFontSizeRatio;
            var paragraphSb = new StringBuilder();

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var text = NormalizeLine(line.Text);
                if (string.IsNullOrWhiteSpace(text)) continue;

                bool isHeading = line.FontSize > headingThreshold
                              && text.Length <= MaxHeadingLength;

                var numberedMatch = NumberedListPattern.Match(text);
                var bulletMatch = BulletPattern.Match(text);
                bool isList = numberedMatch.Success || bulletMatch.Success;

                // Detect paragraph break by vertical gap between lines
                bool hasGap = false;
                if (i > 0)
                {
                    var prev = lines[i - 1];
                    var gap = prev.BaselineY - line.BaselineY;
                    hasGap = gap > prev.LineHeight * ParagraphGapRatio;
                }

                // Flush paragraph buffer on structure change or gap
                if (isHeading || isList || hasGap)
                    FlushParagraph(doc, paragraphSb);

                if (isHeading)
                {
                    int level = GetHeadingLevel(line.FontSize, bodyFontSize);
                    doc.AddHeading(text, level);
                }
                else if (numberedMatch.Success)
                {
                    var marker = numberedMatch.Groups[1].Value.TrimEnd();
                    var itemText = text.Substring(numberedMatch.Length).TrimStart();
                    doc.AddListItem(itemText, enumerated: true, marker: marker);
                }
                else if (bulletMatch.Success)
                {
                    var itemText = text.Substring(bulletMatch.Length).TrimStart();
                    doc.AddListItem(itemText, enumerated: false, marker: "-");
                }
                else
                {
                    if (paragraphSb.Length > 0)
                        paragraphSb.Append(' ');
                    paragraphSb.Append(text);
                }
            }

            FlushParagraph(doc, paragraphSb);
        }

        private static void FlushParagraph(DoclingDocument doc, StringBuilder sb)
        {
            if (sb.Length == 0) return;
            var text = sb.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                doc.AddParagraph(text);
            sb.Clear();
        }

        private static int GetHeadingLevel(double fontSize, double bodyFontSize)
        {
            if (bodyFontSize <= 0) return 2;
            var ratio = fontSize / bodyFontSize;
            if (ratio >= 2.0) return 1;
            if (ratio >= 1.5) return 2;
            return 3;
        }

        // -----------------------------------------------------------------
        //  Font size analysis
        // -----------------------------------------------------------------

        private static double ComputeBodyFontSize(List<double> fontSizes)
        {
            if (fontSizes.Count == 0) return 12;

            // Mode: most frequently occurring rounded font size
            return fontSizes
                .GroupBy(s => Math.Round(s, 1))
                .OrderByDescending(g => g.Count())
                .First().Key;
        }

        // -----------------------------------------------------------------
        //  Text normalization
        // -----------------------------------------------------------------

        private string NormalizeLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Trim();

            if (_options.NormalizeWhitespace)
                text = MultiSpacePattern.Replace(text, " ");

            return text;
        }

        /// <summary>
        /// Normalizes paragraph text preserving up to 2 consecutive newlines.
        /// Used for fallback raw page text.
        /// </summary>
        private string NormalizeParagraph(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (!_options.NormalizeWhitespace)
                return text.Trim();

            var sb = new StringBuilder(text.Length);
            var inWhitespace = false;
            var consecutiveNewlines = 0;

            foreach (var ch in text)
            {
                if (ch == '\r')
                    continue;

                if (ch == '\n')
                {
                    consecutiveNewlines++;
                    if (consecutiveNewlines <= 2)
                        sb.Append('\n');
                    inWhitespace = false;
                }
                else if (char.IsWhiteSpace(ch))
                {
                    consecutiveNewlines = 0;
                    if (!inWhitespace)
                    {
                        sb.Append(' ');
                        inWhitespace = true;
                    }
                }
                else
                {
                    consecutiveNewlines = 0;
                    sb.Append(ch);
                    inWhitespace = false;
                }
            }

            return sb.ToString().Trim();
        }

        // -----------------------------------------------------------------
        //  Document opening
        // -----------------------------------------------------------------

        private PdfDocument OpenDocument(string source)
        {
            if (string.IsNullOrWhiteSpace(_options.Password))
                return PdfDocument.Open(source);

            return PdfDocument.Open(source, new ParsingOptions { Password = _options.Password });
        }

        // -----------------------------------------------------------------
        //  Metadata
        // -----------------------------------------------------------------

        private static void AddDocumentInfoMetadata(PdfDocument document, IDictionary<string, string> metadata)
        {
            var info = document.Information;
            if (info == null) return;

            TryAdd(metadata, "title", info.Title);
            TryAdd(metadata, "author", info.Author);
            TryAdd(metadata, "creator", info.Creator);
            TryAdd(metadata, "subject", info.Subject);
            TryAdd(metadata, "producer", info.Producer);
        }

        private static void TryAdd(IDictionary<string, string> metadata, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !metadata.ContainsKey(key))
                metadata[key] = value!;
        }

        // -----------------------------------------------------------------
        //  Internal data structures
        // -----------------------------------------------------------------

        private class LineData
        {
            public string Text { get; set; } = string.Empty;
            public double FontSize { get; set; }
            public bool IsBold { get; set; }
            public double BaselineY { get; set; }
            public double LineHeight { get; set; }
        }

        private class PageData
        {
            public int PageNumber { get; }
            public List<LineData> Lines { get; }
            public string? FallbackText { get; set; }

            public PageData(int pageNumber, List<LineData>? lines = null)
            {
                PageNumber = pageNumber;
                Lines = lines ?? new List<LineData>();
            }
        }
    }
}
