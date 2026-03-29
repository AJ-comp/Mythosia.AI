# Mythosia.Documents.Office - Release Notes

## v1.0.0

### Initial Release

New package identity — renamed from `Mythosia.AI.Loaders.Office`.

- `WordDocumentLoader` (.docx), `ExcelDocumentLoader` (.xlsx), `PowerPointDocumentLoader` (.pptx) — all returning `DoclingDocument`.
- OpenXml-based parsers: `OpenXmlWordParser`, `OpenXmlExcelParser`, `OpenXmlPowerPointParser`.
- `OfficeParserOptions` — `IncludeMetadata`, `NormalizeWhitespace`, `IncludeSheetNames`, `IncludeSlideNumbers`.
- Custom parser injection via `IDocumentParser`.
