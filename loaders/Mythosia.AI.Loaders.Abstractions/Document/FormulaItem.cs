namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// A mathematical formula item. Follows the docling FormulaItem convention.
    /// </summary>
    public class FormulaItem : TextItem
    {
        public FormulaItem()
        {
            Label = DocItemLabel.Formula;
        }
    }
}
