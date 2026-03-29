# Mythosia.Documents.Abstractions - Release Notes

## v1.0.0

### Initial Release

New package identity — renamed from `Mythosia.AI.Loaders.Abstractions`.

- `DoclingDocument` structured document model with body tree, `RawContent` bypass, `Metadata`, Builder API, and Markdown export.
- `IDocumentLoader` interface returning `IReadOnlyList<DoclingDocument>`.
- `IDocumentParser` interface (`CanParse`, `ParseAsync` → `DoclingDocument`).
- `ParsedDocument` model for legacy parser output.
- Element types in `Mythosia.Documents.Elements` namespace: `TextItem`, `TitleItem`, `SectionHeaderItem`, `CodeItem`, `DocListItem`, `TableItem`, `TableData`, `TableCell`, `PictureItem`, `GroupItem`, `MarkdownSerializer`.
