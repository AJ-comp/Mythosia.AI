namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// A code block item. Follows the docling CodeItem convention.
    /// </summary>
    public class CodeItem : TextItem
    {
        /// <summary>
        /// The programming language of the code block.
        /// </summary>
        public string CodeLanguage { get; set; } = string.Empty;

        public CodeItem()
        {
            Label = DocItemLabel.Code;
        }
    }
}
