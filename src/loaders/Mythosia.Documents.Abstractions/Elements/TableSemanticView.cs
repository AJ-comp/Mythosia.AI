using System.Collections.Generic;

namespace Mythosia.Documents.Elements
{
    /// <summary>
    /// Semantic view of a table, built from <see cref="TableData.BuildSemanticGroups"/>.
    /// Groups rows by their col-0 row label and detects whether the table
    /// is better represented as a form/key-value structure than as a grid.
    /// </summary>
    public class TableSemanticView
    {
        /// <summary>
        /// True when the table is detected as a form/key-value structure
        /// (col 0 = short row labels, col 1+ = values, no explicit column headers).
        /// When true, a Markdown serializer may render this as bold-key + value lines
        /// instead of a pipe table.
        /// </summary>
        public bool IsFormStyle { get; set; }

        /// <summary>
        /// Extracted column header rows (consecutive rows from the top whose origin
        /// cells carry <see cref="TableCell.ColumnHeader"/> = true).
        /// Each element is a string array of length NumCols covering that header row.
        /// Empty when no explicit column headers are present.
        /// </summary>
        public List<string[]> HeaderRows { get; set; } = new List<string[]>();

        /// <summary>
        /// Data rows grouped by their col-0 row label.
        /// A new group starts whenever a new origin cell appears in col 0.
        /// </summary>
        public List<TableSemanticGroup> Groups { get; set; } = new List<TableSemanticGroup>();
    }

    /// <summary>
    /// A set of consecutive data rows that share the same col-0 row label.
    /// </summary>
    public class TableSemanticGroup
    {
        /// <summary>
        /// The row label text taken from the col-0 origin cell of this group
        /// (e.g. "매출처", "헤더1").
        /// Null when col 0 is empty or spans from a previous row.
        /// </summary>
        public string? RowLabel { get; set; }

        /// <summary>
        /// All data rows belonging to this group.
        /// Each element is a string array of length NumCols covering all columns.
        /// Col-0 text appears only in the first row (the group origin);
        /// subsequent rows have an empty string at index 0 (span continuation).
        /// </summary>
        public List<string[]> DataRows { get; set; } = new List<string[]>();
    }
}
