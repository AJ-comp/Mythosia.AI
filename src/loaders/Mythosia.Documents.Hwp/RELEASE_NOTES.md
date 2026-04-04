# Mythosia.Documents.Hwp - Release Notes

## v1.0.0

### Initial Release

- `HwpDocumentLoader` (.hwp) — returns `DoclingDocument` via `IDocumentLoader`.
- HwpLibSharp-based parser (`HwpParser`) with section/paragraph text extraction, heading/title detection, and table support.
- `HwpParserOptions` — `IncludeMetadata`, `NormalizeWhitespace`, `IncludeSectionHeaders`, `ExcludeControlChars`.
- Custom parser injection via `IDocumentParser`.
