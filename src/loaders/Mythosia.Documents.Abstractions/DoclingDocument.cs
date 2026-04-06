using Mythosia.Documents.Elements;
using System.Collections.Generic;

namespace Mythosia.Documents
{
    /// <summary>
    /// Unified document representation following the docling DoclingDocument convention.
    /// Content items are stored in flat lists; the tree structure is maintained via
    /// body/furniture root nodes using RefItem pointers.
    /// </summary>
    public class DoclingDocument
    {
        /// <summary>
        /// The working name of this document (without extension).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The source path, URL, or identifier from which this document was loaded.
        /// Set by the loader to carry provenance through the pipeline.
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Root node of the main document body tree.
        /// </summary>
        public GroupItem Body { get; set; } = new GroupItem
        {
            Name = "_root_",
            SelfRef = "#/body",
        };

        /// <summary>
        /// Root node for furniture elements (headers, footers, etc.).
        /// </summary>
        public GroupItem Furniture { get; set; } = new GroupItem
        {
            Name = "_root_",
            SelfRef = "#/furniture",
            ContentLayer = ContentLayer.Furniture,
        };

        /// <summary>
        /// All text-based content items (paragraphs, headings, list items, code, formulas).
        /// </summary>
        public List<TextItem> Texts { get; set; } = new List<TextItem>();

        /// <summary>
        /// All table items.
        /// </summary>
        public List<TableItem> Tables { get; set; } = new List<TableItem>();

        /// <summary>
        /// All picture items.
        /// </summary>
        public List<PictureItem> Pictures { get; set; } = new List<PictureItem>();

        /// <summary>
        /// Group containers (list groups, chapters, sections, slides, sheets).
        /// </summary>
        public List<GroupItem> Groups { get; set; } = new List<GroupItem>();

        /// <summary>
        /// Document-level metadata extracted by the parser or loader
        /// (e.g., type, filename, extension, title, author, page_count).
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Optional raw content that bypasses the body tree serialization.
        /// When set, <see cref="ToMarkdown"/> returns this value directly
        /// instead of serializing the body tree via <see cref="Document.MarkdownSerializer"/>.
        /// Useful for plain-text loaders that should preserve content as-is.
        /// </summary>
        public string? RawContent { get; set; }

        /// <summary>
        /// Optional table serialization strategy used by <see cref="ToMarkdown"/>.
        /// When set, overrides the default <see cref="GridTableSerializer"/> in <see cref="MarkdownSerializer"/>.
        /// </summary>
        public ITableSerializer? TableSerializer { get; set; }

        // -----------------------------------------------------------------
        //  Builder API — mirrors docling's document construction methods
        // -----------------------------------------------------------------

        /// <summary>
        /// Adds a title item to the document body.
        /// </summary>
        public TitleItem AddTitle(string text, NodeItem? parent = null)
        {
            var item = new TitleItem { Orig = text, Text = text };
            AppendTextItem(item, parent);
            return item;
        }

        /// <summary>
        /// Adds a section heading to the document body.
        /// </summary>
        public SectionHeaderItem AddHeading(string text, int level = 1, NodeItem? parent = null)
        {
            var item = new SectionHeaderItem { Orig = text, Text = text, Level = level };
            AppendTextItem(item, parent);
            return item;
        }

        /// <summary>
        /// Adds a paragraph to the document body.
        /// </summary>
        public TextItem AddParagraph(string text, NodeItem? parent = null)
        {
            var item = new TextItem { Label = DocItemLabel.Paragraph, Orig = text, Text = text };
            AppendTextItem(item, parent);
            return item;
        }

        /// <summary>
        /// Adds a generic text item to the document body.
        /// </summary>
        public TextItem AddText(string text, DocItemLabel label = DocItemLabel.Text, NodeItem? parent = null)
        {
            var item = new TextItem { Label = label, Orig = text, Text = text };
            AppendTextItem(item, parent);
            return item;
        }

        /// <summary>
        /// Adds a list item to the document body.
        /// </summary>
        public DocListItem AddListItem(string text, bool enumerated = false, string marker = "-", NodeItem? parent = null)
        {
            var item = new DocListItem { Orig = text, Text = text, Enumerated = enumerated, Marker = marker };
            AppendTextItem(item, parent);
            return item;
        }

        /// <summary>
        /// Adds a code block to the document body.
        /// </summary>
        public CodeItem AddCode(string text, string language = "", NodeItem? parent = null)
        {
            var item = new CodeItem { Orig = text, Text = text, CodeLanguage = language };
            AppendTextItem(item, parent);
            return item;
        }

        /// <summary>
        /// Adds a table to the document body.
        /// </summary>
        public TableItem AddTable(TableData data, NodeItem? parent = null)
        {
            var index = Tables.Count;
            var item = new TableItem
            {
                Data = data,
                SelfRef = $"#/tables/{index}",
                ContentLayer = ContentLayer.Body,
            };

            Tables.Add(item);

            var parentNode = parent ?? Body;
            item.Parent = parentNode.GetRef();
            parentNode.Children.Add(item.GetRef());

            return item;
        }

        /// <summary>
        /// Adds a picture to the document body.
        /// </summary>
        public PictureItem AddPicture(NodeItem? parent = null)
        {
            var index = Pictures.Count;
            var item = new PictureItem
            {
                SelfRef = $"#/pictures/{index}",
                ContentLayer = ContentLayer.Body,
            };

            Pictures.Add(item);

            var parentNode = parent ?? Body;
            item.Parent = parentNode.GetRef();
            parentNode.Children.Add(item.GetRef());

            return item;
        }

        /// <summary>
        /// Adds a group container to the document body.
        /// </summary>
        public GroupItem AddGroup(string name = "group", GroupLabel label = GroupLabel.Unspecified, NodeItem? parent = null)
        {
            var index = Groups.Count;
            var item = new GroupItem
            {
                Name = name,
                Label = label,
                SelfRef = $"#/groups/{index}",
                ContentLayer = ContentLayer.Body,
            };

            Groups.Add(item);

            var parentNode = parent ?? Body;
            item.Parent = parentNode.GetRef();
            parentNode.Children.Add(item.GetRef());

            return item;
        }

        // -----------------------------------------------------------------
        //  Export
        // -----------------------------------------------------------------

        /// <summary>
        /// Serializes this document to Markdown format.
        /// </summary>
        public string ToMarkdown()
        {
            if (RawContent != null) return RawContent;
            var serializer = new MarkdownSerializer();
            if (TableSerializer != null)
                serializer.TableSerializer = TableSerializer;
            return serializer.Serialize(this);
        }

        // -----------------------------------------------------------------
        //  Internal helpers
        // -----------------------------------------------------------------

        private void AppendTextItem(TextItem item, NodeItem? parent)
        {
            var index = Texts.Count;
            item.SelfRef = $"#/texts/{index}";
            item.ContentLayer = ContentLayer.Body;

            Texts.Add(item);

            var parentNode = parent ?? Body;
            item.Parent = parentNode.GetRef();
            parentNode.Children.Add(item.GetRef());
        }
    }
}
