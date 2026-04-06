using System;
using System.Collections.Generic;
using System.Text;

namespace Mythosia.AI.Rag.Splitters
{
    /// <summary>
    /// Structure-aware Markdown splitter that understands heading hierarchy (H1–H6),
    /// preserves atomic blocks (code fences, tables), and prepends heading breadcrumbs
    /// to each chunk so that vector search retrieves contextually rich fragments.
    /// </summary>
    public class MarkdownTextSplitter : ITextSplitter
    {
        /// <summary>Maximum characters per chunk (excluding the prepended breadcrumb).</summary>
        public int ChunkSize { get; set; } = 1000;

        /// <summary>
        /// When true, each chunk is prefixed with the heading path that leads to its
        /// content (e.g. "# Doc Title\n## Section\n### Sub-section\n\n"). 
        /// This dramatically improves retrieval relevance. Default is true.
        /// </summary>
        public bool IncludeHeadingBreadcrumb { get; set; } = true;

        /// <summary>
        /// Minimum heading level that triggers a new section split.
        /// 1 = split on all headings (#–######), 2 = ignore H1, etc.
        /// Default: 1.
        /// </summary>
        public int MinSplitHeadingLevel { get; set; } = 1;

        public MarkdownTextSplitter() { }

        public MarkdownTextSplitter(int chunkSize)
        {
            ChunkSize = chunkSize;
        }

        // =================================================================
        //  ITextSplitter
        // =================================================================

        public IReadOnlyList<RagChunk> Split(RagDocument document)
        {
            if (string.IsNullOrEmpty(document.Content))
                return Array.Empty<RagChunk>();

            // 1. Parse into flat list of structural blocks
            var blocks = ParseBlocks(document.Content);

            // 2. Walk blocks, building sections defined by headings
            var sections = BuildSections(blocks);

            // 3. Merge small / split large sections into chunks
            var textChunks = ChunkSections(sections);

            // 4. Emit RagChunks
            var result = new List<RagChunk>();
            for (int i = 0; i < textChunks.Count; i++)
            {
                var content = textChunks[i].Trim();
                if (content.Length == 0)
                    continue;

                var chunk = new RagChunk
                {
                    Id = $"{document.Id}_chunk_{i}",
                    DocumentId = document.Id,
                    Content = content,
                    Index = i,
                    Metadata = new Dictionary<string, string>(document.Metadata)
                };
                chunk.Metadata["source"] = document.Source;
                chunk.Metadata["chunk_index"] = i.ToString();

                result.Add(chunk);
            }

            return result;
        }

        // =================================================================
        //  Block-level parser
        // =================================================================

        private enum BlockKind { Heading, CodeFence, Table, Text }

        private sealed class Block
        {
            public BlockKind Kind;
            public string Content = string.Empty; // raw text of this block (no trailing newline)
            public int HeadingLevel;               // 1–6 for headings, 0 otherwise
        }

        /// <summary>
        /// Splits raw Markdown into atomic blocks that must never be split internally.
        /// </summary>
        private static List<Block> ParseBlocks(string markdown)
        {
            var blocks = new List<Block>();
            var lines = markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int i = 0;

            while (i < lines.Length)
            {
                var line = lines[i];

                // --- Code fence ---
                if (IsCodeFenceStart(line))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(line);
                    i++;
                    while (i < lines.Length)
                    {
                        sb.AppendLine(lines[i]);
                        if (IsCodeFenceEnd(lines[i], line))
                        {
                            i++;
                            break;
                        }
                        i++;
                    }
                    blocks.Add(new Block { Kind = BlockKind.CodeFence, Content = sb.ToString().TrimEnd() });
                    continue;
                }

                // --- Table ---
                if (IsTableRow(line))
                {
                    var sb = new StringBuilder();
                    while (i < lines.Length && IsTableRow(lines[i]))
                    {
                        sb.AppendLine(lines[i]);
                        i++;
                    }
                    blocks.Add(new Block { Kind = BlockKind.Table, Content = sb.ToString().TrimEnd() });
                    continue;
                }

                // --- Heading ---
                int level = GetHeadingLevel(line);
                if (level > 0)
                {
                    blocks.Add(new Block { Kind = BlockKind.Heading, Content = line, HeadingLevel = level });
                    i++;
                    continue;
                }

                // --- Text (paragraph / blank lines) ---
                {
                    var sb = new StringBuilder();
                    while (i < lines.Length
                           && GetHeadingLevel(lines[i]) == 0
                           && !IsCodeFenceStart(lines[i])
                           && !IsTableRow(lines[i]))
                    {
                        sb.AppendLine(lines[i]);
                        i++;
                    }
                    var text = sb.ToString().TrimEnd();
                    if (text.Length > 0)
                        blocks.Add(new Block { Kind = BlockKind.Text, Content = text });
                }
            }

            return blocks;
        }

        // =================================================================
        //  Section builder
        // =================================================================

        private sealed class Section
        {
            /// <summary>
            /// Heading breadcrumb lines leading to this section (e.g. ["# Title", "## Sub"]).
            /// </summary>
            public List<string> HeadingPath = new List<string>();

            /// <summary>
            /// Content blocks belonging to this section (excluding the heading itself).
            /// </summary>
            public List<string> ContentBlocks = new List<string>();

            public int ContentLength
            {
                get
                {
                    int len = 0;
                    for (int i = 0; i < ContentBlocks.Count; i++)
                    {
                        if (i > 0) len += 2; // "\n\n" separator
                        len += ContentBlocks[i].Length;
                    }
                    return len;
                }
            }

            public string BuildBreadcrumb()
            {
                if (HeadingPath.Count == 0) return string.Empty;
                var sb = new StringBuilder();
                foreach (var h in HeadingPath)
                {
                    sb.AppendLine(h);
                }
                sb.AppendLine();
                return sb.ToString();
            }

            public string BuildContent()
            {
                return string.Join("\n\n", ContentBlocks);
            }
        }

        /// <summary>
        /// Groups blocks into sections where each heading starts a new section.
        /// The heading breadcrumb is maintained as a stack.
        /// </summary>
        private List<Section> BuildSections(List<Block> blocks)
        {
            var sections = new List<Section>();
            // headingStack[level-1] = heading line for that level
            var headingStack = new string[7]; // index 1–6
            Section? current = null;

            foreach (var block in blocks)
            {
                if (block.Kind == BlockKind.Heading && block.HeadingLevel >= MinSplitHeadingLevel)
                {
                    // Flush current section
                    if (current != null)
                        sections.Add(current);

                    // Update heading stack
                    headingStack[block.HeadingLevel] = block.Content;
                    // Clear deeper levels
                    for (int l = block.HeadingLevel + 1; l <= 6; l++)
                        headingStack[l] = null!;

                    // Start new section with breadcrumb
                    current = new Section();
                    for (int l = 1; l <= 6; l++)
                    {
                        if (headingStack[l] != null)
                            current.HeadingPath.Add(headingStack[l]);
                    }
                }
                else
                {
                    if (current == null)
                    {
                        current = new Section();
                        // Pick up any headings above MinSplitHeadingLevel
                        for (int l = 1; l <= 6; l++)
                        {
                            if (headingStack[l] != null)
                                current.HeadingPath.Add(headingStack[l]);
                        }
                    }

                    if (block.Kind == BlockKind.Heading)
                    {
                        // Heading below MinSplitHeadingLevel — include inline
                        headingStack[block.HeadingLevel] = block.Content;
                        for (int l = block.HeadingLevel + 1; l <= 6; l++)
                            headingStack[l] = null!;
                        current.ContentBlocks.Add(block.Content);
                    }
                    else
                    {
                        current.ContentBlocks.Add(block.Content);
                    }
                }
            }

            if (current != null)
                sections.Add(current);

            return sections;
        }

        // =================================================================
        //  Chunking
        // =================================================================

        private List<string> ChunkSections(List<Section> sections)
        {
            var chunks = new List<string>();

            foreach (var section in sections)
            {
                var breadcrumb = IncludeHeadingBreadcrumb ? section.BuildBreadcrumb() : string.Empty;
                const int bufferMargin = 50;
                var budgetForContent = ChunkSize - breadcrumb.Length - bufferMargin;

                if (budgetForContent < 100)
                    budgetForContent = 100; // safety minimum

                if (section.ContentLength <= budgetForContent)
                {
                    // Entire section fits in one chunk
                    var text = section.BuildContent().Trim();
                    if (text.Length > 0)
                        chunks.Add(breadcrumb + text);
                }
                else
                {
                    // Section is too large — split content blocks into sub-chunks
                    var subChunks = MergeBlocksIntoChunks(section.ContentBlocks, budgetForContent);

                    // Track active bold label across sub-chunks within this section.
                    // If a chunk doesn't start with its own **label**, prepend the active one.
                    string? activeLabel = null;
                    foreach (var sub in subChunks)
                    {
                        var text = sub.Trim();
                        if (text.Length == 0) continue;

                        var startsWithLabel = ExtractBoldLabel(text) != null;

                        if (!startsWithLabel && activeLabel != null)
                            text = activeLabel + "\n" + text;

                        var lastLabel = FindLastBoldLabel(text);
                        if (lastLabel != null)
                            activeLabel = lastLabel;

                        chunks.Add(breadcrumb + text);
                    }
                }
            }

            return chunks;
        }

        /// <summary>
        /// Merges content blocks up to budget, applying overlap when a chunk boundary is hit.
        /// Atomic blocks (code fences, tables) are never split even if they exceed the budget.
        /// </summary>
        private List<string> MergeBlocksIntoChunks(List<string> blocks, int budget)
        {
            var result = new List<string>();
            var current = new StringBuilder();

            foreach (var block in blocks)
            {
                int addLen = current.Length == 0 ? block.Length : block.Length + 2; // "\n\n"

                if (current.Length > 0 && current.Length + addLen > budget)
                {
                    // Flush current chunk
                    result.Add(current.ToString());
                    current.Clear();
                }

                if (current.Length > 0)
                    current.Append("\n\n");
                current.Append(block);
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            // Final pass: split any individual chunks that still exceed budget.
            // Code fences are preserved as-is (truly atomic).
            // Large tables are split by rows, preserving the header.
            var final = new List<string>();
            foreach (var chunk in result)
            {
                if (chunk.Length <= budget)
                {
                    final.Add(chunk);
                }
                else if (IsCodeFenceBlock(chunk))
                {
                    final.Add(chunk);
                }
                else if (IsTableBlock(chunk))
                {
                    final.AddRange(SplitLargeTable(chunk, budget));
                }
                else
                {
                    final.AddRange(SplitOversizedBlock(chunk, budget));
                }
            }

            return final;
        }

        /// <summary>
        /// Cascading split of a large text block: paragraph → line → word boundaries.
        /// Each stage only processes pieces that still exceed the budget.
        /// </summary>
        private List<string> SplitOversizedBlock(string text, int budget)
        {
            var pieces = SplitOversizedBySeparator(new List<string> { text }, budget, "\n\n");
            pieces = SplitOversizedBySeparator(pieces, budget, "\n");

            var result = new List<string>();
            foreach (var piece in pieces)
            {
                if (piece.Length <= budget)
                    result.Add(piece);
                else
                    result.AddRange(SplitByWordBoundary(piece, budget));
            }
            return result;
        }

        /// <summary>
        /// Splits only the pieces that exceed budget using the given separator.
        /// Pieces within budget pass through unchanged.
        /// </summary>
        private static List<string> SplitOversizedBySeparator(List<string> pieces, int budget, string separator)
        {
            var result = new List<string>();
            foreach (var piece in pieces)
            {
                if (piece.Length <= budget)
                {
                    result.Add(piece);
                    continue;
                }

                var parts = piece.Split(new[] { separator }, StringSplitOptions.None);
                if (parts.Length <= 1)
                {
                    result.Add(piece);
                    continue;
                }

                var sb = new StringBuilder();
                foreach (var part in parts)
                {
                    int addLen = sb.Length == 0 ? part.Length : part.Length + separator.Length;
                    if (sb.Length > 0 && sb.Length + addLen > budget)
                    {
                        result.Add(sb.ToString());
                        sb.Clear();
                    }
                    if (sb.Length > 0) sb.Append(separator);
                    sb.Append(part);
                }
                if (sb.Length > 0)
                    result.Add(sb.ToString());
            }
            return result;
        }

        /// <summary>
        /// Last-resort split at word (space) boundaries. Falls back to character-level
        /// if no spaces are found within the budget window.
        /// </summary>
        private static List<string> SplitByWordBoundary(string text, int budget)
        {
            var parts = new List<string>();
            int pos = 0;
            while (pos < text.Length)
            {
                int end = Math.Min(pos + budget, text.Length);
                if (end < text.Length)
                {
                    int lastSpace = text.LastIndexOf(' ', end - 1, end - pos);
                    if (lastSpace > pos)
                        end = lastSpace;
                }
                parts.Add(text.Substring(pos, end - pos));
                pos = end;
                if (pos < text.Length && text[pos] == ' ')
                    pos++;
            }
            return parts;
        }

        /// <summary>
        /// Extracts a bold label from the beginning of text, e.g. <c>**제품 내용**</c>.
        /// Returns null if no bold label pattern is found.
        /// </summary>
        private static string? ExtractBoldLabel(string text)
        {
            if (!text.StartsWith("**", StringComparison.Ordinal))
                return null;

            int close = text.IndexOf("**", 2, StringComparison.Ordinal);
            if (close < 0)
                return null;

            return text.Substring(0, close + 2);
        }

        /// <summary>
        /// Scans text for the last <c>**label**</c> pattern that appears at the
        /// start of a line. Returns the label, or null if none found.
        /// </summary>
        private static string? FindLastBoldLabel(string text)
        {
            string? last = null;
            int pos = 0;
            while (pos < text.Length)
            {
                if (pos == 0 || (pos > 0 && text[pos - 1] == '\n'))
                {
                    if (text.Length - pos >= 4 && text[pos] == '*' && text[pos + 1] == '*')
                    {
                        int close = text.IndexOf("**", pos + 2, StringComparison.Ordinal);
                        if (close > pos + 2)
                            last = text.Substring(pos, close + 2 - pos);
                    }
                }
                int nl = text.IndexOf('\n', pos);
                if (nl < 0) break;
                pos = nl + 1;
            }
            return last;
        }

        // =================================================================
        //  Table splitting
        // =================================================================

        /// <summary>
        /// Splits a large Markdown table by rows, preserving the header row(s)
        /// at the start of each chunk so that each chunk remains a valid table.
        /// </summary>
        private List<string> SplitLargeTable(string tableText, int budget)
        {
            var lines = tableText.Split(new[] { "\n" }, StringSplitOptions.None);
            if (lines.Length <= 2)
                return new List<string> { tableText };

            // Identify header: first row + optional separator row (e.g. |---|---|)
            var headerParts = new List<string> { lines[0] };
            int dataStart = 1;

            if (lines.Length > 1 && IsTableSeparatorRow(lines[1]))
            {
                headerParts.Add(lines[1]);
                dataStart = 2;
            }

            var header = string.Join("\n", headerParts);

            var parts = new List<string>();
            var sb = new StringBuilder(header);

            for (int i = dataStart; i < lines.Length; i++)
            {
                var line = lines[i];
                if (sb.Length > header.Length && sb.Length + 1 + line.Length > budget)
                {
                    parts.Add(sb.ToString());
                    sb.Clear();
                    sb.Append(header);
                }
                sb.Append('\n');
                sb.Append(line);
            }

            if (sb.Length > header.Length)
                parts.Add(sb.ToString());

            return parts.Count > 0 ? parts : new List<string> { tableText };
        }

        private static bool IsTableSeparatorRow(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 3 || trimmed[0] != '|')
                return false;
            foreach (var c in trimmed)
            {
                if (c != '|' && c != '-' && c != ':' && c != ' ')
                    return false;
            }
            return true;
        }

        // =================================================================
        //  Overlap helper
        // =================================================================

        // =================================================================
        //  Line-level helpers
        // =================================================================

        private static int GetHeadingLevel(string line)
        {
            if (line.Length == 0 || line[0] != '#')
                return 0;

            int level = 0;
            while (level < line.Length && level < 6 && line[level] == '#')
                level++;

            // Must be followed by whitespace or end-of-line to be a real heading
            if (level >= line.Length)
                return level; // "###" alone is valid
            if (char.IsWhiteSpace(line[level]))
                return level;

            return 0; // e.g. "#hashtag" is not a heading
        }

        private static bool IsCodeFenceStart(string line)
        {
            var trimmed = line.TrimStart();
            return trimmed.StartsWith("```", StringComparison.Ordinal)
                || trimmed.StartsWith("~~~", StringComparison.Ordinal);
        }

        private static bool IsCodeFenceEnd(string line, string openLine)
        {
            var trimmed = line.TrimStart();
            var openTrimmed = openLine.TrimStart();
            if (openTrimmed.StartsWith("```", StringComparison.Ordinal))
                return trimmed.StartsWith("```", StringComparison.Ordinal) && trimmed.TrimEnd().Length <= 3;
            if (openTrimmed.StartsWith("~~~", StringComparison.Ordinal))
                return trimmed.StartsWith("~~~", StringComparison.Ordinal) && trimmed.TrimEnd().Length <= 3;
            return false;
        }

        private static bool IsTableRow(string line)
        {
            var trimmed = line.TrimStart();
            return trimmed.Length > 0 && trimmed[0] == '|';
        }

        private static bool IsCodeFenceBlock(string text)
        {
            var trimmed = text.TrimStart();
            return trimmed.StartsWith("```", StringComparison.Ordinal)
                || trimmed.StartsWith("~~~", StringComparison.Ordinal);
        }

        private static bool IsTableBlock(string text)
        {
            var trimmed = text.TrimStart();
            return trimmed.Length > 0 && trimmed[0] == '|';
        }
    }
}
