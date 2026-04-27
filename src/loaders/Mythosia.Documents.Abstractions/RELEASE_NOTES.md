# Mythosia.Documents.Abstractions - Release Notes

## v1.2.0

### Markdown Serialization

- `MarkdownSerializer` now escapes Markdown-significant characters in body text by default, including paragraphs, titles, headings, and list items.
- Added `MarkdownSerializer.EscapeText` for callers that want to preserve the previous raw Markdown behavior.
- Heading output is clamped to Markdown H1-H6 so deep source headings do not render as invalid H7+ headings.
- Lists now emit a blank line before following block elements, preventing paragraphs, headings, tables, code blocks, formulas, and image placeholders from being absorbed into the list.
- `RawContent` continues to bypass structured Markdown serialization unchanged.
- Updated `System.Text.Json` to 10.0.7.

## v1.1.0

### Pluggable Table Serialization

- `ITableSerializer` strategy interface for swapping table rendering without modifying other code.
- `GridTableSerializer` — standard Markdown pipe table (default).
- `SemanticTableSerializer` — form-style rendering with bold group labels (`**label**`) and inline data. Auto-detects form vs grid layout via `TableData.DetectFormStyle`.
- `DoclingDocument.TableSerializer` property — per-document override for table serialization strategy. When set, `ToMarkdown()` uses it instead of the default `GridTableSerializer`.
- `TableData` — structured table model with header detection and `BuildSemanticGroups()`.
- `TableSemanticView` — semantic group/column analysis for table layout classification.

## v1.0.0

### Initial Release

New package identity — renamed from `Mythosia.AI.Loaders.Abstractions`.

- `DoclingDocument` structured document model with body tree, `RawContent` bypass, `Metadata`, Builder API, and Markdown export.
- `IDocumentLoader` interface returning `IReadOnlyList<DoclingDocument>`.
- `IDocumentParser` interface (`CanParse`, `ParseAsync` → `DoclingDocument`).
- `ParsedDocument` model for legacy parser output.
- Element types in `Mythosia.Documents.Elements` namespace: `TextItem`, `TitleItem`, `SectionHeaderItem`, `CodeItem`, `DocListItem`, `TableItem`, `TableData`, `TableCell`, `PictureItem`, `GroupItem`, `MarkdownSerializer`.
