namespace Mythosia.AI.Loaders.Office.Compat
{
    /// <summary>
    /// Parser options shared by legacy Office parser implementations.
    /// </summary>
    public class OfficeCompatParserOptions
    {
        /// <summary>
        /// Includes document metadata when available.
        /// </summary>
        public bool IncludeMetadata { get; set; } = true;

        /// <summary>
        /// Collapses excessive whitespace to single spaces.
        /// </summary>
        public bool NormalizeWhitespace { get; set; } = true;
    }
}
