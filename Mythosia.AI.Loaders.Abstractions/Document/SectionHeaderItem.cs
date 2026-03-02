namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// A section heading item with a heading level.
    /// Follows the docling SectionHeaderItem convention.
    /// </summary>
    public class SectionHeaderItem : TextItem
    {
        /// <summary>
        /// Heading level (1 = top-level, 2 = sub-section, etc.).
        /// </summary>
        public int Level { get; set; } = 1;

        public SectionHeaderItem()
        {
            Label = DocItemLabel.SectionHeader;
        }
    }
}
