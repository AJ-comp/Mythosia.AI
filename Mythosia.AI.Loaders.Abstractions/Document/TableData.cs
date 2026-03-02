using System.Collections.Generic;

namespace Mythosia.AI.Loaders.Document
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
    }
}
