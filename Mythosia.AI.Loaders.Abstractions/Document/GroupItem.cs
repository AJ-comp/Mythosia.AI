namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// A container node that groups other nodes (e.g. list container, chapter, section, slide).
    /// Follows the docling GroupItem convention. Cannot be a leaf node.
    /// </summary>
    public class GroupItem : NodeItem
    {
        /// <summary>
        /// Name of the group (e.g. "Introduction Chapter", "Slide 5", "Sheet1").
        /// </summary>
        public string Name { get; set; } = "group";

        /// <summary>
        /// Semantic label for the group type.
        /// </summary>
        public GroupLabel Label { get; set; } = GroupLabel.Unspecified;
    }
}
