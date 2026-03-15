using System.Text;

namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// Serializes a <see cref="DoclingDocument"/> to Markdown format.
    /// Walks the body tree in reading order and emits Markdown for each content item.
    /// </summary>
    public class MarkdownSerializer
    {
        /// <summary>
        /// Placeholder text used for picture items.
        /// </summary>
        public string ImagePlaceholder { get; set; } = "<!-- image -->";

        /// <summary>
        /// Converts the entire document body to a Markdown string.
        /// </summary>
        public string Serialize(DoclingDocument doc)
        {
            var sb = new StringBuilder();
            SerializeNode(doc, doc.Body, sb);
            return sb.ToString().TrimEnd('\n', '\r', ' ') + "\n";
        }

        private void SerializeNode(DoclingDocument doc, NodeItem node, StringBuilder sb)
        {
            // If this node is itself a content item, render it
            RenderItem(doc, node, sb);

            // Walk children in order
            foreach (var childRef in node.Children)
            {
                var child = childRef.Resolve(doc);
                if (child != null)
                    SerializeNode(doc, child, sb);
            }
        }

        private void RenderItem(DoclingDocument doc, NodeItem node, StringBuilder sb)
        {
            switch (node)
            {
                case TitleItem title:
                    sb.AppendLine($"# {title.Text}");
                    sb.AppendLine();
                    break;

                case SectionHeaderItem header:
                    var prefix = new string('#', header.Level + 1); // level 1 → ##
                    sb.AppendLine($"{prefix} {header.Text}");
                    sb.AppendLine();
                    break;

                case DocListItem listItem:
                    if (listItem.Enumerated)
                        sb.AppendLine($"{listItem.Marker} {listItem.Text}");
                    else
                        sb.AppendLine($"- {listItem.Text}");
                    break;

                case CodeItem code:
                    sb.AppendLine($"```{code.CodeLanguage}");
                    sb.AppendLine(code.Text);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    break;

                case FormulaItem formula:
                    sb.AppendLine($"$${formula.Text}$$");
                    sb.AppendLine();
                    break;

                case TextItem text when text.Label == DocItemLabel.Paragraph
                                     || text.Label == DocItemLabel.Text:
                    if (!string.IsNullOrWhiteSpace(text.Text))
                    {
                        sb.AppendLine(text.Text);
                        sb.AppendLine();
                    }
                    break;

                case TableItem table:
                    RenderTable(doc, table, sb);
                    break;

                case PictureItem _:
                    sb.AppendLine(ImagePlaceholder);
                    sb.AppendLine();
                    break;

                // GroupItem: no direct rendering, children are walked by SerializeNode
                default:
                    break;
            }
        }

        private void RenderTable(DoclingDocument doc, TableItem table, StringBuilder sb)
        {
            var data = table.Data;
            if (data.NumRows == 0 || data.NumCols == 0)
                return;

            var grid = data.BuildGrid();

            // Determine how many rows are column headers
            int headerRows = 0;
            for (int r = 0; r < data.NumRows; r++)
            {
                bool anyHeader = false;
                for (int c = 0; c < data.NumCols; c++)
                {
                    if (grid[r, c]?.ColumnHeader == true)
                    {
                        anyHeader = true;
                        break;
                    }
                }

                if (anyHeader)
                    headerRows++;
                else
                    break;
            }

            // If no explicit header rows, treat first row as header
            if (headerRows == 0)
                headerRows = 1;

            // Render header rows
            for (int r = 0; r < headerRows; r++)
            {
                sb.Append("| ");
                for (int c = 0; c < data.NumCols; c++)
                {
                    var cellText = SanitizeForTable(grid[r, c]?.Text ?? "");
                    sb.Append(cellText);
                    sb.Append(" | ");
                }
                sb.AppendLine();
            }

            // Separator
            sb.Append("|");
            for (int c = 0; c < data.NumCols; c++)
                sb.Append("---|");
            sb.AppendLine();

            // Data rows
            for (int r = headerRows; r < data.NumRows; r++)
            {
                sb.Append("| ");
                for (int c = 0; c < data.NumCols; c++)
                {
                    var cellText = SanitizeForTable(grid[r, c]?.Text ?? "");
                    sb.Append(cellText);
                    sb.Append(" | ");
                }
                sb.AppendLine();
            }

            sb.AppendLine();
        }

        private static string SanitizeForTable(string text)
        {
            // Markdown table cells must not contain newlines or pipe characters
            return text.Replace("\r", "").Replace("\n", " ").Replace("|", "\\|");
        }
    }
}
