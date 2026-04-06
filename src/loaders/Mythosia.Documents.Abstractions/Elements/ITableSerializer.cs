using System.Text;

namespace Mythosia.Documents.Elements
{
    /// <summary>
    /// Strategy interface for rendering a <see cref="TableItem"/> to Markdown.
    /// Swap implementations on <see cref="MarkdownSerializer.TableSerializer"/>
    /// to change how tables are serialized without modifying any other code.
    /// </summary>
    public interface ITableSerializer
    {
        /// <summary>
        /// Renders the table into <paramref name="sb"/>.
        /// Implementations must append a trailing blank line when they emit output.
        /// </summary>
        void Render(TableItem table, StringBuilder sb);
    }
}
