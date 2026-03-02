using System.Collections.Generic;

namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// A picture/image content item. Follows the docling PictureItem convention.
    /// </summary>
    public class PictureItem : DocItem
    {
        /// <summary>
        /// References to caption items for this picture.
        /// </summary>
        public List<RefItem> Captions { get; set; } = new List<RefItem>();

        public PictureItem()
        {
            Label = DocItemLabel.Picture;
        }
    }
}
