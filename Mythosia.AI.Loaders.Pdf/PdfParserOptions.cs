namespace Mythosia.AI.Loaders.Pdf
{
    /// <summary>
    /// Parser options shared by PDF parser implementations.
    /// </summary>
    public class PdfParserOptions
    {
        /// <summary>
        /// Password for encrypted PDFs.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Includes document metadata when available.
        /// </summary>
        public bool IncludeMetadata { get; set; } = true;

        /// <summary>
        /// Includes page number headers in extracted text.
        /// </summary>
        public bool IncludePageNumbers { get; set; } = false;

        /// <summary>
        /// Collapses excessive whitespace to single spaces.
        /// </summary>
        public bool NormalizeWhitespace { get; set; } = true;
    }
}
