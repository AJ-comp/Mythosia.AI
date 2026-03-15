namespace Mythosia.AI.Loaders.Office
{
    /// <summary>
    /// Parser options shared by Office parser implementations.
    /// </summary>
    public class OfficeParserOptions
    {
        /// <summary>
        /// Includes document metadata such as title and author when available.
        /// </summary>
        public bool IncludeMetadata { get; set; } = true;

        /// <summary>
        /// Collapses excessive whitespace to single spaces.
        /// </summary>
        public bool NormalizeWhitespace { get; set; } = true;

        /// <summary>
        /// Includes sheet names when parsing Excel workbooks.
        /// </summary>
        public bool IncludeSheetNames { get; set; } = true;

        /// <summary>
        /// Includes slide numbers when parsing PowerPoint decks.
        /// </summary>
        public bool IncludeSlideNumbers { get; set; } = true;
    }
}
