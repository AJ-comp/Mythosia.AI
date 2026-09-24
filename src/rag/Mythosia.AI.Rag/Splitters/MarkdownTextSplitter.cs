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
    /// <remarks>
    /// The combined output is limited to the greater of 65,536 UTF-16 code units or
    /// 32 times the input length. Repeated headings, table headers, and prose labels
    /// count toward this limit. Excessive expansion throws <see cref="InvalidOperationException"/>
    /// before the next chunk is materialized; no partial result is returned.
    /// Each call captures its input text and splitter settings before parsing. The
    /// budget is local to that call; subsequent calls observe updated settings.
    /// </remarks>
    public class MarkdownTextSplitter : ITextSplitter
    {
        private const int MinimumOutputBudget = 65_536;
        private const int MaximumExpansionFactor = 32;
        /// <summary>
        /// Maximum UTF-16 code units per chunk, excluding the prepended heading breadcrumb.
        /// Code fences and a table header with one complete row remain atomic and may exceed
        /// this budget. A surrogate pair remains intact even when the budget is one.
        /// </summary>
        public int ChunkSize { get; set; } = 1000;

        /// <summary>
        /// When true, each chunk is prefixed with the heading path that leads to its
        /// content (e.g. "# Doc Title\n## Section\n### Sub-section\n\n"). 
        /// When false, the original section heading is retained once in the content.
        /// Default is true.
        /// </summary>
        public bool IncludeHeadingBreadcrumb { get; set; } = true;

        /// <summary>
        /// Minimum heading level that triggers a new section split.
        /// 1 = split on all headings (#–######), 2 = ignore H1, etc.
        /// Ancestor headings also end an existing descendant section to keep its breadcrumb accurate.
        /// Default: 1.
        /// </summary>
        public int MinSplitHeadingLevel { get; set; } = 1;

        public MarkdownTextSplitter() { }

        public MarkdownTextSplitter(int chunkSize)
        {
            SplitterGuards.ValidateSize(chunkSize, nameof(chunkSize));
            ChunkSize = chunkSize;
        }

        // =================================================================
        //  ITextSplitter
        // =================================================================

        public IReadOnlyList<RagChunk> Split(RagDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var input = document.Content;
            var chunkSize = ChunkSize;
            var minimumHeadingLevel = MinSplitHeadingLevel;
            var includeHeadingBreadcrumb = IncludeHeadingBreadcrumb;
            SplitterGuards.ValidateSize(chunkSize, nameof(ChunkSize));
            if (minimumHeadingLevel < 1 || minimumHeadingLevel > 6)
                throw new ArgumentOutOfRangeException(nameof(MinSplitHeadingLevel), "Heading level must be between 1 and 6.");

            if (string.IsNullOrEmpty(input))
                return Array.Empty<RagChunk>();

            // 1. Parse into flat list of structural blocks
            var blocks = ParseBlocks(input);

            // 2. Walk blocks, building sections defined by headings
            var sections = BuildSections(blocks, minimumHeadingLevel, includeHeadingBreadcrumb);

            // 3. Merge small / split large sections into chunks
            var textChunks = ChunkSections(sections, input.Length, chunkSize, includeHeadingBreadcrumb);

            // 4. Emit RagChunks
            var result = new List<RagChunk>();
            for (int i = 0; i < textChunks.Count; i++)
            {
                // Code-fence indentation and unclosed code's trailing whitespace are
                // content. Only prose blocks are trimmed, before they are merged.
                var content = textChunks[i];
                if (string.IsNullOrWhiteSpace(content))
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
            public string? ProseLabel;              // standalone label scoped to this text block
        }

        /// <summary>
        /// Splits raw Markdown into atomic blocks that must never be split internally.
        /// </summary>
        private static List<Block> ParseBlocks(string markdown)
        {
            var blocks = new List<Block>();
            var lines = markdown.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            int i = 0;

            while (i < lines.Length)
            {
                var line = lines[i];

                // --- Code fence ---
                if (TryGetOpeningFence(line, out char fenceMarker, out int fenceLength))
                {
                    var sb = new StringBuilder();
                    sb.Append(line);
                    i++;
                    while (i < lines.Length)
                    {
                        sb.Append('\n').Append(lines[i]);
                        if (IsCodeFenceEnd(lines[i], fenceMarker, fenceLength))
                        {
                            i++;
                            break;
                        }
                        i++;
                    }
                    blocks.Add(new Block { Kind = BlockKind.CodeFence, Content = sb.ToString() });
                    continue;
                }

                // --- Table ---
                if (IsTableStart(lines, i))
                {
                    var sb = new StringBuilder();
                    sb.Append(lines[i++]).Append('\n');
                    sb.Append(lines[i++]).Append('\n');
                    while (i < lines.Length && IsTableBodyRow(lines[i]))
                    {
                        sb.Append(lines[i]).Append('\n');
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
                    string? proseLabel = null;
                    bool hasText = false;
                    while (i < lines.Length
                           && GetHeadingLevel(lines[i]) == 0
                           && !IsCodeFenceStart(lines[i])
                           && !IsTableStart(lines, i))
                    {
                        // A soft-wrapped emphasis line inside a paragraph is not a label.
                        // Labels may start a text block or follow a blank paragraph boundary.
                        var label = !hasText || (i > 0 && string.IsNullOrWhiteSpace(lines[i - 1]))
                            ? GetStandaloneProseLabel(lines[i]) : null;
                        // A label starts its own prose scope. A table, fence, heading,
                        // or another label ends that scope; table cells never enter it.
                        if (label != null && hasText) break;
                        if (label != null) proseLabel = label;
                        sb.Append(lines[i]).Append('\n');
                        hasText |= !string.IsNullOrWhiteSpace(lines[i]);
                        i++;
                    }
                    var text = sb.ToString().Trim();
                    if (text.Length > 0)
                        blocks.Add(new Block { Kind = BlockKind.Text, Content = text, ProseLabel = proseLabel });
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
            public List<Block> ContentBlocks = new List<Block>();

            public long BreadcrumbLength
            {
                get
                {
                    if (HeadingPath.Count == 0) return 0;
                    long len = 1; // final blank line
                    foreach (var heading in HeadingPath) len += (long)heading.Length + 1;
                    return len;
                }
            }

            public string BuildBreadcrumb()
            {
                if (HeadingPath.Count == 0) return string.Empty;
                var sb = new StringBuilder();
                foreach (var h in HeadingPath)
                {
                    sb.Append(h).Append('\n');
                }
                sb.Append('\n');
                return sb.ToString();
            }

        }

        /// <summary>
        /// Groups blocks into sections where each heading starts a new section.
        /// The heading breadcrumb is maintained as a stack.
        /// </summary>
        private static List<Section> BuildSections(List<Block> blocks, int minimumHeadingLevel, bool includeHeadingBreadcrumb)
        {
            var sections = new List<Section>();
            // headingStack[level-1] = heading line for that level
            var headingStack = new string[7]; // index 1–6
            Section? current = null;

            foreach (var block in blocks)
            {
                if (block.Kind == BlockKind.Heading && block.HeadingLevel >= minimumHeadingLevel)
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
                    if (!includeHeadingBreadcrumb)
                        current.ContentBlocks.Add(block);
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
                        // A new ancestor cannot belong to the previous descendant's
                        // breadcrumb, even when that ancestor is below the split threshold.
                        if (current.HeadingPath.Count > 0)
                        {
                            sections.Add(current);
                            current = new Section();
                            for (int l = 1; l <= 6; l++)
                                if (headingStack[l] != null) current.HeadingPath.Add(headingStack[l]);
                            if (includeHeadingBreadcrumb) continue;
                        }
                        current.ContentBlocks.Add(block);
                    }
                    else
                    {
                        current.ContentBlocks.Add(block);
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

        /// <summary>
        /// References original pieces until the shared output budget permits materialization.
        /// In particular, a table header is not copied once per row while planning.
        /// </summary>
        private sealed class ChunkPlan
        {
            private readonly IReadOnlyList<string> _pieces;
            private readonly string _separator;
            private readonly string? _proseLabel;
            public long Length { get; }

            public ChunkPlan(IReadOnlyList<string> pieces, string separator, string? proseLabel = null)
            {
                _pieces = pieces;
                _separator = separator;
                _proseLabel = proseLabel;
                long length = proseLabel == null ? 0 : (long)proseLabel.Length + 1;
                for (int i = 0; i < pieces.Count; i++)
                    length += (long)pieces[i].Length + (i == 0 ? 0 : separator.Length);
                Length = length;
            }

            public string Materialize(string breadcrumb)
            {
                if (breadcrumb.Length == 0 && _proseLabel == null && _pieces.Count == 1)
                    return _pieces[0];
                var output = new StringBuilder(checked((int)(Length + breadcrumb.Length)));
                output.Append(breadcrumb);
                if (_proseLabel != null) output.Append(_proseLabel).Append('\n');
                for (int i = 0; i < _pieces.Count; i++)
                {
                    if (i > 0) output.Append(_separator);
                    output.Append(_pieces[i]);
                }
                return output.ToString();
            }
        }

        private sealed class OutputBudget
        {
            private readonly long _limit;
            private long _used;

            public OutputBudget(int inputLength)
            {
                _limit = Math.Max(MinimumOutputBudget, (long)MaximumExpansionFactor * inputLength);
            }

            public void Reserve(long length)
            {
                if (length > int.MaxValue || length > _limit - _used)
                    throw new InvalidOperationException(
                        $"Markdown splitting exceeds the output budget of {_limit} UTF-16 code units "
                        + $"(the greater of {MinimumOutputBudget} or {MaximumExpansionFactor} times the input length). "
                        + "Repeated headings, table headers, and prose labels count toward this budget. "
                        + "No partial chunks were returned.");
                _used += length;
            }
        }

        private List<string> ChunkSections(List<Section> sections, int inputLength, int chunkSize, bool includeHeadingBreadcrumb)
        {
            var chunks = new List<string>();
            var outputBudget = new OutputBudget(inputLength);

            for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            {
                var section = sections[sectionIndex];
                // An empty ancestor section is already preserved by its child's breadcrumb.
                // Standalone headings and headings before a different section still produce content.
                if (includeHeadingBreadcrumb && section.ContentBlocks.Count == 0
                    && sectionIndex + 1 < sections.Count && IsAncestor(section, sections[sectionIndex + 1]))
                    continue;
                if (section.ContentBlocks.Count == 0)
                {
                    if (!includeHeadingBreadcrumb || section.HeadingPath.Count == 0) continue;
                    var headings = new List<string>(section.HeadingPath);
                    headings[headings.Count - 1] = headings[headings.Count - 1].TrimEnd();
                    var headingOnly = new ChunkPlan(headings, "\n");
                    outputBudget.Reserve(headingOnly.Length);
                    chunks.Add(headingOnly.Materialize(string.Empty));
                    continue;
                }

                var breadcrumbLength = includeHeadingBreadcrumb ? section.BreadcrumbLength : 0;
                string? breadcrumb = null;
                foreach (var plan in MergeBlocksIntoChunks(section.ContentBlocks, chunkSize))
                {
                    // Reserve before constructing the breadcrumb or copying repeated context.
                    outputBudget.Reserve(breadcrumbLength + plan.Length);
                    if (breadcrumb == null)
                        breadcrumb = includeHeadingBreadcrumb ? section.BuildBreadcrumb() : string.Empty;
                    chunks.Add(plan.Materialize(breadcrumb));
                }
            }

            return chunks;
        }

        /// <summary>
        /// Merges content blocks up to budget. Fences remain atomic; tables split only
        /// between rows and repeat their header. Markdown has no character overlap setting.
        /// </summary>
        private static bool IsAncestor(Section parent, Section child)
        {
            if (parent.HeadingPath.Count == 0 || child.HeadingPath.Count <= parent.HeadingPath.Count) return false;
            for (int i = 0; i < parent.HeadingPath.Count; i++)
                if (parent.HeadingPath[i] != child.HeadingPath[i]) return false;
            return true;
        }

        private IEnumerable<ChunkPlan> MergeBlocksIntoChunks(List<Block> blocks, int budget)
        {
            var current = new List<string>();
            long currentLength = 0;

            foreach (var block in blocks)
            {
                long addLen = current.Count == 0 ? block.Content.Length : (long)block.Content.Length + 2;

                if (current.Count > 0 && currentLength + addLen > budget)
                {
                    yield return new ChunkPlan(current, "\n\n");
                    current = new List<string>();
                    currentLength = 0;
                }

                if (block.Content.Length > budget)
                {
                    // Preserve the parsed block kind rather than interpreting trimmed text again.
                    if (block.Kind == BlockKind.CodeFence)
                        yield return new ChunkPlan(new[] { block.Content }, string.Empty);
                    else if (block.Kind == BlockKind.Table)
                    {
                        foreach (var plan in SplitLargeTable(block.Content, budget)) yield return plan;
                    }
                    else
                    {
                        var pieces = SplitOversizedBlock(block.Content, budget);
                        for (int i = 0; i < pieces.Count; i++)
                        {
                            var piece = pieces[i].Trim();
                            if (piece.Length == 0) continue;
                            // A repeated label belongs only to the original prose scope.
                            // Keep its original occurrence, and never exceed the budget.
                            var repeatedLabel = i > 0 && block.ProseLabel != null
                                && block.ProseLabel.Length < budget
                                && piece.Length <= budget - block.ProseLabel.Length - 1
                                ? block.ProseLabel : null;
                            yield return new ChunkPlan(new[] { piece }, string.Empty, repeatedLabel);
                        }
                    }
                    continue;
                }
                if (current.Count > 0) currentLength += 2;
                current.Add(block.Content);
                currentLength += block.Content.Length;
            }

            if (current.Count > 0) yield return new ChunkPlan(current, "\n\n");
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
                int end = SplitterGuards.SafeEnd(text, pos, budget);
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
        /// Recognizes a complete standalone prose label, not inline emphasis, a list
        /// item, or a table cell. Called only while parsing ordinary text blocks.
        /// </summary>
        private static string? GetStandaloneProseLabel(string line)
        {
            int offset = GetBlockStart(line);
            if (offset < 0) return null;
            var text = line.Substring(offset).TrimEnd(' ', '\t');
            if (text.Length <= 4 || !text.StartsWith("**", StringComparison.Ordinal))
                return null;

            int close = text.IndexOf("**", 2, StringComparison.Ordinal);
            if (close != text.Length - 2 || string.IsNullOrWhiteSpace(text.Substring(2, close - 2)))
                return null;

            return text;
        }

        // =================================================================
        //  Table splitting
        // =================================================================

        /// <summary>
        /// Splits a large Markdown table by rows, preserving the header row(s)
        /// at the start of each chunk so that each chunk remains a valid table.
        /// </summary>
        private IEnumerable<ChunkPlan> SplitLargeTable(string tableText, int budget)
        {
            var lines = tableText.Split(new[] { "\n" }, StringSplitOptions.None);
            if (lines.Length <= 2)
            {
                yield return new ChunkPlan(new[] { tableText }, string.Empty);
                yield break;
            }

            // Identify header: first row + optional separator row (e.g. |---|---|)
            var headerParts = new List<string> { lines[0] };
            int dataStart = 1;

            if (lines.Length > 1 && IsTableSeparatorRow(lines[1]))
            {
                headerParts.Add(lines[1]);
                dataStart = 2;
            }

            long headerLength = headerParts[0].Length;
            if (headerParts.Count > 1) headerLength += 1L + headerParts[1].Length;
            var current = new List<string>(headerParts);
            long currentLength = headerLength;

            for (int i = dataStart; i < lines.Length; i++)
            {
                var line = lines[i];
                if (current.Count > headerParts.Count && currentLength + 1 + line.Length > budget)
                {
                    yield return new ChunkPlan(current, "\n");
                    current = new List<string>(headerParts);
                    currentLength = headerLength;
                }
                current.Add(line);
                currentLength += 1L + line.Length;
            }

            yield return new ChunkPlan(current, "\n");
        }

        private static bool IsTableSeparatorRow(string line)
        {
            var cells = GetTableCells(line);
            if (cells.Count == 0) return false;
            foreach (var cell in cells)
            {
                var value = cell.Trim();
                int pos = value.StartsWith(":", StringComparison.Ordinal) ? 1 : 0;
                int start = pos;
                while (pos < value.Length && value[pos] == '-') pos++;
                if (pos == start) return false;
                if (pos < value.Length && value[pos] == ':') pos++;
                if (pos != value.Length) return false;
            }
            return true;
        }

        // =================================================================
        //  Line-level helpers
        // =================================================================

        private static int GetHeadingLevel(string line)
        {
            int offset = GetBlockStart(line);
            if (offset < 0 || offset >= line.Length || line[offset] != '#')
                return 0;

            int level = 0;
            while (offset + level < line.Length && level < 6 && line[offset + level] == '#')
                level++;

            // Must be followed by whitespace or end-of-line to be a real heading
            if (offset + level >= line.Length)
                return level; // "###" alone is valid
            if (line[offset + level] == ' ' || line[offset + level] == '\t')
                return level;

            return 0; // e.g. "#hashtag" is not a heading
        }

        private static bool IsCodeFenceStart(string line)
        {
            return TryGetOpeningFence(line, out _, out _);
        }

        private static bool IsCodeFenceEnd(string line, char marker, int openingLength)
        {
            // The opener was parsed once. Re-reading a long opener for every short
            // content line would turn a linear pass into quadratic work.
            int offset = GetBlockStart(line);
            if (offset < 0 || line.Length - offset < openingLength) return false;
            int end = offset;
            while (end < line.Length && line[end] == marker) end++;
            if (end - offset < openingLength) return false;
            while (end < line.Length && (line[end] == ' ' || line[end] == '\t')) end++;
            return end == line.Length;
        }

        private static bool TryGetOpeningFence(string line, out char marker, out int length)
        {
            marker = '\0';
            length = 0;
            int offset = GetBlockStart(line);
            if (offset < 0 || offset >= line.Length || (line[offset] != '`' && line[offset] != '~'))
                return false;
            marker = line[offset];
            int end = offset;
            while (end < line.Length && line[end] == marker) end++;
            length = end - offset;
            // A backtick fence's information string cannot contain backticks.
            return length >= 3 && (marker != '`' || line.IndexOf('`', end) < 0);
        }

        private static int GetBlockStart(string line)
        {
            int offset = 0;
            while (offset < line.Length && line[offset] == ' ') offset++;
            return offset <= 3 ? offset : -1;
        }

        private static bool IsTableStart(string[] lines, int index)
        {
            if (index + 1 >= lines.Length || GetHeadingLevel(lines[index]) > 0
                || !IsTableSeparatorRow(lines[index + 1]))
                return false;
            // Requiring a real pipe avoids interpreting a setext heading as a table.
            return HasUnescapedPipe(lines[index])
                && GetTableCells(lines[index]).Count == GetTableCells(lines[index + 1]).Count;
        }

        private static bool IsTableBodyRow(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || GetHeadingLevel(line) > 0 || IsCodeFenceStart(line))
                return false;
            // Block quotes and lists start a new block rather than extending a table.
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(">", StringComparison.Ordinal)
                || trimmed.StartsWith("- ", StringComparison.Ordinal)
                || trimmed.StartsWith("* ", StringComparison.Ordinal)
                || trimmed.StartsWith("+ ", StringComparison.Ordinal))
                return false;
            int digit = 0;
            while (digit < trimmed.Length && char.IsDigit(trimmed[digit])) digit++;
            if (digit > 0 && digit <= 9 && digit + 1 < trimmed.Length
                && (trimmed[digit] == '.' || trimmed[digit] == ')')
                && (trimmed[digit + 1] == ' ' || trimmed[digit + 1] == '\t'))
                return false;
            return true;
        }

        private static bool HasUnescapedPipe(string line)
        {
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '\\' && i + 1 < line.Length) i++;
                else if (line[i] == '|') return true;
            }
            return false;
        }

        private static List<string> GetTableCells(string line)
        {
            var cells = new List<string>();
            var trimmed = line.Trim();
            int start = 0;
            bool trailingPipe = false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (trimmed[i] == '\\' && i + 1 < trimmed.Length)
                {
                    i++;
                    continue;
                }
                if (trimmed[i] != '|') continue;
                if (i > 0) cells.Add(trimmed.Substring(start, i - start));
                start = i + 1;
                trailingPipe = start == trimmed.Length;
            }
            if (!trailingPipe) cells.Add(trimmed.Substring(start));
            return cells;
        }

    }
}
