using System;
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
        /// When true (default), escapes Markdown-significant characters in body text
        /// (paragraphs, list items, headings, titles) so source content like "*literal*"
        /// survives a round trip without being interpreted as emphasis.
        /// </summary>
        public bool EscapeText { get; set; } = true;

        /// <summary>
        /// Converts the entire document body to a Markdown string.
        /// </summary>
        public string Serialize(DoclingDocument doc)
        {
            var sb = new StringBuilder();
            var ctx = new SerializeContext();
            SerializeNode(doc, doc.Body, sb, ctx);
            return sb.ToString().TrimEnd('\n', '\r', ' ') + "\n";
        }

        private void SerializeNode(DoclingDocument doc, NodeItem node, StringBuilder sb, SerializeContext ctx)
        {
            RenderItem(doc, node, sb, ctx);

            foreach (var childRef in node.Children)
            {
                var child = childRef.Resolve(doc);
                if (child != null)
                    SerializeNode(doc, child, sb, ctx);
            }
        }

        private void RenderItem(DoclingDocument doc, NodeItem node, StringBuilder sb, SerializeContext ctx)
        {
            switch (node)
            {
                case TitleItem title:
                    EndListBlock(sb, ctx);
                    sb.AppendLine($"# {Escape(title.Text)}");
                    sb.AppendLine();
                    break;

                case SectionHeaderItem header:
                    EndListBlock(sb, ctx);
                    var level = Math.Min(Math.Max(header.Level, 0) + 1, 6);
                    var prefix = new string('#', level);
                    sb.AppendLine($"{prefix} {Escape(header.Text)}");
                    sb.AppendLine();
                    break;

                case DocListItem listItem:
                    if (listItem.Enumerated)
                        sb.AppendLine($"{listItem.Marker} {Escape(listItem.Text)}");
                    else
                        sb.AppendLine($"- {Escape(listItem.Text)}");
                    ctx.InsideList = true;
                    break;

                case CodeItem code:
                    EndListBlock(sb, ctx);
                    sb.AppendLine($"```{code.CodeLanguage}");
                    sb.AppendLine(code.Text);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    break;

                case FormulaItem formula:
                    EndListBlock(sb, ctx);
                    sb.AppendLine($"$${formula.Text}$$");
                    sb.AppendLine();
                    break;

                case TextItem text when text.Label == DocItemLabel.Paragraph
                                     || text.Label == DocItemLabel.Text:
                    if (!string.IsNullOrWhiteSpace(text.Text))
                    {
                        EndListBlock(sb, ctx);
                        sb.AppendLine(Escape(text.Text));
                        sb.AppendLine();
                    }
                    break;

                case TableItem table:
                    EndListBlock(sb, ctx);
                    TableSerializer.Render(table, sb);
                    break;

                case PictureItem _:
                    EndListBlock(sb, ctx);
                    sb.AppendLine(ImagePlaceholder);
                    sb.AppendLine();
                    break;

                // GroupItem: no direct rendering, children are walked by SerializeNode
                default:
                    break;
            }
        }

        /// <summary>
        /// When transitioning out of a list, emit a blank line so subsequent block
        /// elements (paragraphs, headings, tables) are not absorbed into the list.
        /// </summary>
        private static void EndListBlock(StringBuilder sb, SerializeContext ctx)
        {
            if (ctx.InsideList)
            {
                sb.AppendLine();
                ctx.InsideList = false;
            }
        }

        private string Escape(string text)
        {
            if (!EscapeText || string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '\\':
                    case '`':
                    case '*':
                    case '_':
                    case '[':
                    case ']':
                    case '<':
                    case '>':
                    case '|':
                    case '~':
                        sb.Append('\\');
                        sb.Append(ch);
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        private class SerializeContext
        {
            public bool InsideList;
        }
    }
}
