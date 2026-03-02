using System.Collections.Generic;

namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// Base class for all tree nodes in the document structure.
    /// Follows the docling NodeItem convention with self_ref, parent, and children pointers.
    /// </summary>
    public class NodeItem
    {
        /// <summary>
        /// JSON pointer self-reference (e.g. "#/texts/0", "#/body").
        /// </summary>
        public string SelfRef { get; set; } = string.Empty;

        /// <summary>
        /// Reference to the parent node in the document tree.
        /// </summary>
        public RefItem? Parent { get; set; }

        /// <summary>
        /// References to child nodes in the document tree.
        /// </summary>
        public List<RefItem> Children { get; set; } = new List<RefItem>();

        /// <summary>
        /// Which content layer this node belongs to.
        /// </summary>
        public ContentLayer ContentLayer { get; set; } = ContentLayer.Body;

        /// <summary>
        /// Returns a RefItem pointing to this node.
        /// </summary>
        public RefItem GetRef() => new RefItem(SelfRef);
    }
}
