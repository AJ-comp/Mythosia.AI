using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mythosia.Documents.Elements
{
    /// <summary>
    /// Renders tables using <see cref="TableData.BuildSemanticGroups"/>.
    /// Form-style tables (col-0 row labels, no explicit column headers) are rendered
    /// as bold-label + value lines. Grid-style tables are rendered as pipe tables.
    /// </summary>
    public class SemanticTableSerializer : ITableSerializer
    {
        public void Render(TableItem table, StringBuilder sb)
        {
            var data = table.Data;
            if (data.NumRows == 0 || data.NumCols == 0) return;

            var sv = data.BuildSemanticGroups();

            bool hasContent = sv.HeaderRows.Any(r => r.Any(c => !string.IsNullOrWhiteSpace(c)))
                           || sv.Groups.Any(g => g.DataRows.Any(r => r.Any(c => !string.IsNullOrWhiteSpace(c))));
            if (!hasContent) return;

            if (sv.IsFormStyle)
                RenderForm(sv, sb);
            else
                RenderGrid(sv, data.NumCols, sb);
        }

        private static void RenderForm(TableSemanticView sv, StringBuilder sb)
        {
            foreach (var group in sv.Groups)
            {
                var values = group.DataRows
                    .SelectMany(row => row.Skip(1))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => Inline(c))
                    .ToList();

                if (values.Count == 0) continue;

                if (!string.IsNullOrWhiteSpace(group.RowLabel))
                {
                    if (values.Count == 1)
                        sb.AppendLine($"**{group.RowLabel}**: {values[0]}");
                    else
                    {
                        sb.AppendLine($"**{group.RowLabel}**");
                        foreach (var v in values)
                            sb.AppendLine(v);
                    }
                }
                else
                {
                    foreach (var v in values)
                        sb.AppendLine(v);
                }

                sb.AppendLine();
            }
        }

        private static void RenderGrid(TableSemanticView sv, int numCols, StringBuilder sb)
        {
            IReadOnlyList<string[]> headerRows = sv.HeaderRows;
            int skipFirstGroupRows = 0;

            // Fallback: no explicit column headers → use first data row as header
            if (headerRows.Count == 0 && sv.Groups.Count > 0 && sv.Groups[0].DataRows.Count > 0)
            {
                headerRows = new[] { sv.Groups[0].DataRows[0] };
                skipFirstGroupRows = 1;
            }

            // Header rows
            foreach (var row in headerRows)
            {
                sb.Append("| ");
                for (int c = 0; c < numCols; c++)
                {
                    sb.Append(ForTable(c < row.Length ? row[c] : ""));
                    sb.Append(" | ");
                }
                sb.AppendLine();
            }

            // Separator
            sb.Append("|");
            for (int c = 0; c < numCols; c++) sb.Append("---|");
            sb.AppendLine();

            // Data rows
            for (int gi = 0; gi < sv.Groups.Count; gi++)
            {
                var group = sv.Groups[gi];
                int startRow = gi == 0 ? skipFirstGroupRows : 0;

                for (int ri = startRow; ri < group.DataRows.Count; ri++)
                {
                    var row = group.DataRows[ri];
                    sb.Append("| ");
                    for (int c = 0; c < numCols; c++)
                    {
                        sb.Append(ForTable(c < row.Length ? row[c] : ""));
                        sb.Append(" | ");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine();
        }

        private static string ForTable(string text)
            => text.Replace("\r", "").Replace("\n", " ").Replace("|", "\\|");

        private static string Inline(string text)
            => text.Replace("\r", "").Replace("\n", " ");
    }
}
