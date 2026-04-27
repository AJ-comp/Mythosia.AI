# Mythosia.Documents.Office - Release Notes

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
