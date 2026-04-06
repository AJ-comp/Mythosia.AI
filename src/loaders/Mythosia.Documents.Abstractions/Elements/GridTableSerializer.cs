using System.Text;

namespace Mythosia.Documents.Elements
{
    /// <summary>
    /// Renders tables as standard Markdown pipe tables.
    /// Rows whose origin cells carry <see cref="TableCell.ColumnHeader"/> = true
    /// are treated as header rows; if none exist, the first row is used as the header.
    /// </summary>
    public class GridTableSerializer : ITableSerializer
    {
        public void Render(TableItem table, StringBuilder sb)
        {
            var data = table.Data;
            if (data.NumRows == 0 || data.NumCols == 0) return;

            var grid = data.BuildGrid();

            // Skip if all origin cells are empty
            bool hasContent = false;
            for (int r = 0; r < data.NumRows && !hasContent; r++)
                for (int c = 0; c < data.NumCols && !hasContent; c++)
                {
                    var cell = grid[r, c];
                    if (cell != null && cell.StartRowOffsetIdx == r && cell.StartColOffsetIdx == c
                        && !string.IsNullOrWhiteSpace(cell.Text))
                        hasContent = true;
                }
            if (!hasContent) return;

            // Count consecutive header rows from the top
            int headerRows = 0;
            for (int r = 0; r < data.NumRows; r++)
            {
                bool anyHeader = false;
                for (int c = 0; c < data.NumCols; c++)
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

            // Fallback: first row as header
            if (headerRows == 0) headerRows = 1;

            // Header rows
            for (int r = 0; r < headerRows; r++)
            {
                sb.Append("| ");
                for (int c = 0; c < data.NumCols; c++)
                {
                    sb.Append(GetCellText(grid, r, c));
                    sb.Append(" | ");
                }
                sb.AppendLine();
            }

            // Separator
            sb.Append("|");
            for (int c = 0; c < data.NumCols; c++) sb.Append("---|");
            sb.AppendLine();

            // Data rows
            for (int r = headerRows; r < data.NumRows; r++)
            {
                sb.Append("| ");
                for (int c = 0; c < data.NumCols; c++)
                {
                    sb.Append(GetCellText(grid, r, c));
                    sb.Append(" | ");
                }
                sb.AppendLine();
            }

            sb.AppendLine();
        }

        private static string GetCellText(TableCell?[,] grid, int r, int c)
        {
            var cell = grid[r, c];
            if (cell == null) return "";
            if (cell.StartRowOffsetIdx != r || cell.StartColOffsetIdx != c) return "";
            return cell.Text.Replace("\r", "").Replace("\n", " ").Replace("|", "\\|");
        }
    }
}
