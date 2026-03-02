namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// A text content item. Follows the docling TextItem convention.
    /// Carries both original and sanitized text representations.
    /// </summary>
    public class TextItem : DocItem
    {
        /// <summary>
        /// Original (untreated) text representation.
        /// </summary>
        public string Orig { get; set; } = string.Empty;

        /// <summary>
        /// Sanitized text representation.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        public TextItem()
        {
            Label = DocItemLabel.Paragraph;
        }
    }
}
