namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// Represents a single cell in a table.
    /// Follows the docling TableCell convention with span and offset information.
    /// </summary>
    public class TableCell
    {
        /// <summary>
        /// Text content of the cell.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Number of rows this cell spans.
        /// </summary>
        public int RowSpan { get; set; } = 1;

        /// <summary>
        /// Number of columns this cell spans.
        /// </summary>
        public int ColSpan { get; set; } = 1;

        /// <summary>
        /// 0-based start row index (inclusive).
        /// </summary>
        public int StartRowOffsetIdx { get; set; }

        /// <summary>
        /// 0-based end row index (exclusive).
        /// </summary>
        public int EndRowOffsetIdx { get; set; }

        /// <summary>
        /// 0-based start column index (inclusive).
        /// </summary>
        public int StartColOffsetIdx { get; set; }

        /// <summary>
        /// 0-based end column index (exclusive).
        /// </summary>
        public int EndColOffsetIdx { get; set; }

        /// <summary>
        /// Whether this cell is a column header.
        /// </summary>
        public bool ColumnHeader { get; set; }

        /// <summary>
        /// Whether this cell is a row header.
        /// </summary>
        public bool RowHeader { get; set; }

        /// <summary>
        /// Whether this cell is a row section header.
        /// </summary>
        public bool RowSection { get; set; }
    }
}
