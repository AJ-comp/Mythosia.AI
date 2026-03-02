namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// Base type for any element that carries content (can be a leaf node).
    /// Follows the docling DocItem convention.
    /// </summary>
    public class DocItem : NodeItem
    {
        /// <summary>
        /// The semantic label of this content item.
        /// </summary>
        public DocItemLabel Label { get; set; }
    }
}
