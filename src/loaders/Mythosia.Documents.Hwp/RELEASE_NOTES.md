# Mythosia.Documents.Hwp - Release Notes

## v1.0.2

### Fixed

- Add an explicit `OpenMcdf` 3.1.4 dependency floor instead of relying on the vulnerable 3.1.0 transitive floor from `HwpLibSharp`.

### Internal

- Build against `Mythosia.Documents.Abstractions` 1.2.0 and include release notes, symbols and repository provenance in the package.

### Compatibility

- Public loader signatures and HWP parsing configuration remain unchanged.

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
