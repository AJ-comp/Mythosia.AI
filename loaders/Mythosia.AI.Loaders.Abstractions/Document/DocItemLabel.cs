namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// Labels for document content items, following the docling DocItemLabel convention.
    /// </summary>
    public enum DocItemLabel
    {
        /// <summary>Document title.</summary>
        Title,

        /// <summary>Section heading (with level).</summary>
        SectionHeader,

        /// <summary>Generic paragraph text.</summary>
        Paragraph,

        /// <summary>Generic text element.</summary>
        Text,

        /// <summary>List item.</summary>
        ListItem,

        /// <summary>Table.</summary>
        Table,

        /// <summary>Picture or image.</summary>
        Picture,

        /// <summary>Code block.</summary>
        Code,

        /// <summary>Mathematical formula.</summary>
        Formula,

        /// <summary>Caption for a table or picture.</summary>
        Caption,

        /// <summary>Footnote.</summary>
        Footnote,

        /// <summary>Page header (furniture).</summary>
        PageHeader,

        /// <summary>Page footer (furniture).</summary>
        PageFooter,

        /// <summary>Document index / table of contents.</summary>
        DocumentIndex,

        /// <summary>Selected checkbox.</summary>
        CheckboxSelected,

        /// <summary>Unselected checkbox.</summary>
        CheckboxUnselected,

        /// <summary>Reference / citation.</summary>
        Reference,

        /// <summary>Key-value region.</summary>
        KeyValueRegion,
    }
}
