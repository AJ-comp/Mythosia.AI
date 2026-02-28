# Mythosia.AI.Loaders.Abstractions - Release Notes

## v1.1.0

### Added

- `IDocumentParser` interface for document parsing (`CanParse`, `ParseAsync`).

### Documentation

- Loaders guide split into language-specific docs under `docs/{lang}/loaders.md`.
- README now links to the language-specific loaders guides.

---

## v1.0.0

### Initial Release

- `IDocumentLoader` interface for custom document ingestion.
- Core models: `RagDocument`, `ParsedDocument`.
