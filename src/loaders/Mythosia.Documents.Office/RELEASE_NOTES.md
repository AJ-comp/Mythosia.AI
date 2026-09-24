# Mythosia.Documents.Office - Release Notes

## Unreleased

> This section describes unreleased source changes. The next release version has not been assigned; versioned entries below retain their original release history.

### Fixed

- File loading uses the normalized absolute path as `DoclingDocument.Source`, stabilizing RAG automatic document IDs across relative/absolute registrations and separating same-named files in different folders. Explicit IDs remain unchanged; default citations may show absolute paths.

### Compatibility

- Existing relative-path IDs are not automatically migrated or removed. Delete only the identified old document ID in the relevant store before reindexing, or index all source documents into a new empty collection and switch after validation. Indexing only the new ID leaves old records behind; preserve unrelated documents. Public signatures and package versions are unchanged.

## v1.1.0

### Structured Parsing and Markdown Serialization

- Recompiled against `Mythosia.Documents.Abstractions` 1.2.0, exposing `MarkdownSerializer.EscapeText` and default Markdown text escaping to Office consumers.
- Word parser now maintains a heading hierarchy stack so same-level headings remain siblings, lower-level headings nest correctly, and later higher-level headings pop back to the correct parent.
- Word titles now reset the heading stack and act as the current document container for following body content.
- Word tables are attached to the current heading/title context instead of a stale parent.
- PowerPoint parser now walks text shapes and table graphic frames in document order, preserving text/table/text slide sequences in Markdown output.
- PowerPoint title placeholders, bullets, numbered lists, and tables remain covered by parser tests.
- Added Office parser tests that generate `.docx` and `.pptx` fixtures with OpenXml.

## v1.0.1

### Dependency Update

- Recompiled against `Mythosia.Documents.Abstractions` 1.1.0 (pluggable table serialization via `ITableSerializer`).

## v1.0.0

### Initial Release

New package identity — renamed from `Mythosia.AI.Loaders.Office`.

- `WordDocumentLoader` (.docx), `ExcelDocumentLoader` (.xlsx), `PowerPointDocumentLoader` (.pptx) — all returning `DoclingDocument`.
- OpenXml-based parsers: `OpenXmlWordParser`, `OpenXmlExcelParser`, `OpenXmlPowerPointParser`.
- `OfficeParserOptions` — `IncludeMetadata`, `NormalizeWhitespace`, `IncludeSheetNames`, `IncludeSlideNumbers`.
- Custom parser injection via `IDocumentParser`.
