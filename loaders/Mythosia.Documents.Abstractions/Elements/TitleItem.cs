namespace Mythosia.Documents.Elements
{
    /// <summary>
    /// The document title item. Follows the docling TitleItem convention.
    /// </summary>
    public class TitleItem : TextItem
    {
        public TitleItem()
        {
            Label = DocItemLabel.Title;
        }
    }
}
