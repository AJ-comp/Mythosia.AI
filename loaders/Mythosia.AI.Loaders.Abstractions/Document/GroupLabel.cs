namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// Labels for group (container) items, following the docling GroupLabel convention.
    /// </summary>
    public enum GroupLabel
    {
        /// <summary>Unspecified group.</summary>
        Unspecified,

        /// <summary>List container (holds ListItems).</summary>
        List,

        /// <summary>Chapter grouping.</summary>
        Chapter,

        /// <summary>Section grouping.</summary>
        Section,

        /// <summary>Spreadsheet sheet.</summary>
        Sheet,

        /// <summary>Presentation slide.</summary>
        Slide,
    }
}
