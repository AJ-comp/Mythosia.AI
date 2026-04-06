# Mythosia.Documents.Hwp - Release Notes

## v1.0.1

### Semantic Table Rendering

- HWP documents now default to `SemanticTableSerializer` for table rendering.
- Form-style tables (e.g., application forms, key-value layouts) are automatically detected and rendered with bold group labels (`**label**`) for improved RAG chunking context.
- Requires `Mythosia.Documents.Abstractions` ≥ 1.1.0.

## v1.0.0

### Initial Release

- `HwpDocumentLoader` (.hwp) — returns `DoclingDocument` via `IDocumentLoader`.
- HwpLibSharp-based parser (`HwpParser`) with section/paragraph text extraction, heading/title detection, and table support.
- `HwpParserOptions` — `IncludeMetadata`, `NormalizeWhitespace`, `IncludeSectionHeaders`, `ExcludeControlChars`.
- Custom parser injection via `IDocumentParser`.
