# Mythosia.Documents.Pdf - Release Notes

## v1.1.1

### Dependency Update

- Recompiled against `Mythosia.Documents.Abstractions` 1.1.0 (pluggable table serialization via `ITableSerializer`).

## v1.1.0

### Structured Extraction

- Font-size based heading detection — body font size (mode) computed across all pages, larger text classified as heading level 1–3 by size ratio.
- Bullet and numbered list recognition — detects `•`, `-`, `*` and `1.`, `a)`, `iv.` patterns.
- Spatial paragraph grouping — words grouped into lines by Y-coordinate proximity, consecutive body-text lines merged into paragraphs, vertical gaps trigger paragraph breaks.
- Fallback for PDFs with no extractable words — raw `page.Text` used when `GetWords()` returns empty.
- Direct metadata access via `document.Information` — reflection removed.
- `NormalizeWhitespace` preserves up to 2 consecutive newlines (aligned with Office loaders).
- Direct `ParsingOptions.Password` assignment — reflection removed.

## v1.0.0

### Initial Release

New package identity — renamed from `Mythosia.AI.Loaders.Pdf`.

- `PdfDocumentLoader` returning `DoclingDocument`.
- PdfPig-based parser (`PdfPigParser`) with structured document extraction.
- `PdfParserOptions` — `Password`, `IncludeMetadata`, `IncludePageNumbers`, `NormalizeWhitespace`.
- Custom parser injection via `IDocumentParser`.
