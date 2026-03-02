# Mythosia.AI.Loaders.Pdf - Release Notes

## v1.1.0

### Added

- Unified PdfPig parser via `IDocumentParser` (`PdfPigParser`).
- Extension-based auto-routing with `CanParse` validation.
- `PdfParserOptions` — `Password`, `IncludeMetadata`, `IncludePageNumbers`, `NormalizeWhitespace`.
- Custom parser injection support (`PdfDocumentLoader(parser: ...)`).

---

## v1.0.0

### Initial Release

- PdfPig-based PDF document loader.
- Depends on `Mythosia.AI.Loaders.Abstractions`.
