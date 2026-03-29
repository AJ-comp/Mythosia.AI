# Mythosia.Documents.Pdf - Release Notes

## v1.0.0

### Initial Release

New package identity — renamed from `Mythosia.AI.Loaders.Pdf`.

- `PdfDocumentLoader` returning `DoclingDocument`.
- PdfPig-based parser (`PdfPigParser`) with structured document extraction.
- `PdfParserOptions` — `Password`, `IncludeMetadata`, `IncludePageNumbers`, `NormalizeWhitespace`.
- Custom parser injection via `IDocumentParser`.
