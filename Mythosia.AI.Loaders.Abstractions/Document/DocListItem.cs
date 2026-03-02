namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// A list item within a list group.
    /// Follows the docling ListItem convention.
    /// Named DocListItem to avoid conflict with System.Collections types.
    /// </summary>
    public class DocListItem : TextItem
    {
        /// <summary>
        /// Whether this list item is enumerated (ordered list).
        /// </summary>
        public bool Enumerated { get; set; }

        /// <summary>
        /// The bullet or number marker that prefixes this list item.
        /// </summary>
        public string Marker { get; set; } = "-";

        public DocListItem()
        {
            Label = DocItemLabel.ListItem;
        }
    }
}
