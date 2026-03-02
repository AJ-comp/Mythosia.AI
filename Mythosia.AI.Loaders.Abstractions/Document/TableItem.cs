using System.Collections.Generic;

namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// A table content item. Follows the docling TableItem convention.
    /// </summary>
    public class TableItem : DocItem
    {
        /// <summary>
        /// The table structure and cell data.
        /// </summary>
        public TableData Data { get; set; } = new TableData();

        /// <summary>
        /// References to caption items for this table.
        /// </summary>
        public List<RefItem> Captions { get; set; } = new List<RefItem>();

        public TableItem()
        {
            Label = DocItemLabel.Table;
        }
    }
}
