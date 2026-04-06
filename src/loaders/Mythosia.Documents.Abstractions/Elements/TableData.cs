using System.Collections.Generic;

namespace Mythosia.Documents.Elements
{
    /// <summary>
    /// Holds all table cell data and dimensions.
    /// Follows the docling TableData convention.
    /// </summary>
    public class TableData
    {
        /// <summary>
        /// Flat list of all table cells.
        /// </summary>
        public List<TableCell> TableCells { get; set; } = new List<TableCell>();

        /// <summary>
        /// Total number of rows.
        /// </summary>
        public int NumRows { get; set; }

        /// <summary>
        /// Total number of columns.
        /// </summary>
        public int NumCols { get; set; }

        /// <summary>
        /// Builds a 2D grid from the flat cell list.
        /// Each grid position references the cell that covers it.
        /// </summary>
        public TableCell?[,] BuildGrid()
        {
            var grid = new TableCell?[NumRows, NumCols];

            foreach (var cell in TableCells)
            {
                for (int r = cell.StartRowOffsetIdx; r < cell.EndRowOffsetIdx && r < NumRows; r++)
                {
                    for (int c = cell.StartColOffsetIdx; c < cell.EndColOffsetIdx && c < NumCols; c++)
                    {
                        grid[r, c] = cell;
                    }
                }
            }

            return grid;
        }

        /// <summary>
        /// Analyses the table structure and returns a semantic view that groups rows
        /// by their col-0 row label and detects form/key-value layout.
        /// The underlying flat cell data is not modified; this is a read-only analysis layer.
        /// </summary>
        public TableSemanticView BuildSemanticGroups()
        {
            var view = new TableSemanticView();

            if (NumRows == 0 || NumCols == 0)
                return view;

            var grid = BuildGrid();

            // ── 1. Determine explicit column header rows ────────────────────
            // Count consecutive rows from the top where at least one origin cell
            // carries ColumnHeader = true. No row-0 fallback here — unlike
            // MarkdownSerializer, we represent "no headers" as an empty HeaderRows list.
            int headerRows = 0;
            for (int r = 0; r < NumRows; r++)
            {
                bool anyHeader = false;
                for (int c = 0; c < NumCols; c++)
                {
                    var cell = grid[r, c];
                    if (cell?.ColumnHeader == true && cell.StartRowOffsetIdx == r)
                    {
                        anyHeader = true;
                        break;
                    }
                }
                if (anyHeader) headerRows++;
                else break;
            }

            // ── 2. Extract header row texts ─────────────────────────────────
            for (int r = 0; r < headerRows; r++)
            {
                var headerRow = new string[NumCols];
                for (int c = 0; c < NumCols; c++)
                    headerRow[c] = GetOriginText(grid, r, c);
                view.HeaderRows.Add(headerRow);
            }

            // ── 3. Group data rows by col-0 origin ──────────────────────────
            // A new group starts whenever col 0 has a new origin cell at row r.
            // Rows where col 0 is a span continuation belong to the current group.
            TableSemanticGroup? currentGroup = null;

            for (int r = headerRows; r < NumRows; r++)
            {
                var col0 = grid[r, 0];
                bool isNewOrigin = col0 != null
                    && col0.StartRowOffsetIdx == r
                    && col0.StartColOffsetIdx == 0;

                if (isNewOrigin || currentGroup == null)
                {
                    string? label = isNewOrigin && !string.IsNullOrWhiteSpace(col0!.Text)
                        ? col0.Text.Trim()
                        : null;
                    currentGroup = new TableSemanticGroup { RowLabel = label };
                    view.Groups.Add(currentGroup);
                }

                // Capture all columns for this row (col 0 text appears only at origin)
                var row = new string[NumCols];
                for (int c = 0; c < NumCols; c++)
                    row[c] = GetOriginText(grid, r, c);
                currentGroup.DataRows.Add(row);
            }

            // ── 4. Detect form/key-value layout ─────────────────────────────
            view.IsFormStyle = DetectFormStyle(grid, headerRows);

            return view;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the trimmed text of the cell only at its origin position (StartRow, StartCol).
        /// Span positions return an empty string to avoid duplicating merged cell content.
        /// </summary>
        private static string GetOriginText(TableCell?[,] grid, int r, int c)
        {
            var cell = grid[r, c];
            if (cell == null) return string.Empty;
            if (cell.StartRowOffsetIdx != r || cell.StartColOffsetIdx != c) return string.Empty;
            return cell.Text.Trim();
        }

        /// <summary>
        /// Heuristic: the table looks like a form/key-value structure when
        /// there are no explicit column headers and col-0 cells are short labels.
        /// A col-0 cell with RowSpan &gt; 1 is treated as a strong grouping signal.
        /// </summary>
        private bool DetectFormStyle(TableCell?[,] grid, int headerRows)
        {
            // Must have at least 2 columns (label + value)
            if (NumCols < 2) return false;

            // If there are explicit column headers, it is a data table, not a form
            if (headerRows > 0) return false;

            int col0Count = 0;
            int col0ShortCount = 0; // cells whose trimmed text is 1–30 chars
            bool hasRowSpanLabel = false;

            for (int r = 0; r < NumRows; r++)
            {
                var cell = grid[r, 0];
                if (cell == null || cell.StartRowOffsetIdx != r || cell.StartColOffsetIdx != 0)
                    continue;

                col0Count++;
                int len = cell.Text.Trim().Length;
                if (len > 0 && len <= 30) col0ShortCount++;
                if (cell.RowSpan > 1) hasRowSpanLabel = true;
            }

            if (col0Count == 0) return false;

            double shortRatio = (double)col0ShortCount / col0Count;

            // Strong signal: any col-0 cell spans multiple rows (explicit grouping)
            // Weak signal: most col-0 cells are short labels
            return hasRowSpanLabel
                ? shortRatio >= 0.5
                : shortRatio >= 0.7;
        }
    }
}
