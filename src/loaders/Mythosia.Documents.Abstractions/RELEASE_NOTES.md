# Mythosia.Documents.Abstractions - Release Notes

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
