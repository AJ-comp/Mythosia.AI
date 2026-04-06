using System.Text;

namespace Mythosia.Documents.Elements
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
        /// Strategy for rendering table items to Markdown.
        /// Defaults to <see cref="GridTableSerializer"/> (standard pipe table).
        /// Swap to <see cref="SemanticTableSerializer"/> for semantic group rendering.
        /// </summary>
        public ITableSerializer TableSerializer { get; set; } = new GridTableSerializer();

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
                    TableSerializer.Render(table, sb);
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

    }
}
