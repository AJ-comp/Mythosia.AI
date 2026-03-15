# Mythosia.AI.Loaders.Office - Release Notes

## v1.1.0

### Added

- Unified OpenXml parsers via `IDocumentParser` (`OpenXmlWordParser`, `OpenXmlExcelParser`, `OpenXmlPowerPointParser`).
- Extension-based auto-routing with `CanParse` validation.
- `OfficeParserOptions` — `IncludeMetadata`, `NormalizeWhitespace`, `IncludeSheetNames`, `IncludeSlideNumbers`.
- `OfficeParserUtilities` shared utilities.

### Loaders

- `WordDocumentLoader` (.docx)
- `ExcelDocumentLoader` (.xlsx)
- `PowerPointDocumentLoader` (.pptx)

---

## v1.0.0

### Initial Release

- OpenXml-based Word/Excel/PowerPoint document loaders.
- Depends on `Mythosia.AI.Loaders.Abstractions`.
