namespace Mythosia.Documents.Hwp
{
    /// <summary>
    /// Parser options for HWP document parsing.
    /// </summary>
    public class HwpParserOptions
    {
        /// <summary>
        /// Includes document metadata when available.
        /// </summary>
        public bool IncludeMetadata { get; set; } = true;

        /// <summary>
        /// Collapses excessive whitespace to single spaces.
        /// </summary>
        public bool NormalizeWhitespace { get; set; } = true;

        /// <summary>
        /// Includes section separators in extracted output.
        /// When true, each HWP section boundary is emitted as a heading.
        /// </summary>
        public bool IncludeSectionHeaders { get; set; } = false;

        /// <summary>
        /// Excludes control characters from extracted text.
        /// </summary>
        public bool ExcludeControlChars { get; set; } = true;
    }
}
