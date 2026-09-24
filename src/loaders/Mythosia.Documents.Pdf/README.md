# Mythosia.Documents.Pdf

PDF document loader. Parses PDF files into `DoclingDocument` structured models via [PdfPig](https://github.com/UglyToad/PdfPig). Provides font-size based heading detection, bullet/numbered list recognition, and spatial paragraph grouping. Supports encrypted PDFs, metadata extraction, and page number headers.

## Stable file identity

This fix is currently unreleased; see the [pending package notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/loaders/Mythosia.Documents.Pdf/RELEASE_NOTES.md#unreleased). Use a source build until the next package version is published.

Registering the same file through a relative path and an absolute path must update one document, while same-named files in different folders must stay separate. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` and `PdfDocumentLoader` now set `DoclingDocument.Source` to the normalized absolute file path, as the built-in TXT loaders do. RAG derives automatic document IDs from this value; explicit IDs remain caller-controlled. Default citations may therefore show absolute paths.

Previously stored relative-path IDs are not migrated or deleted automatically. Identify the old document ID, explicitly remove only that document from the relevant store and reindex it. Alternatively, index the complete source set into a new empty collection, validate it and switch the application to it. Reindexing only the new absolute-path ID in the existing collection leaves the old records behind. Do not delete unrelated documents. See [document identity and migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag.md#document-identity).

## Installation

```bash
dotnet add package Mythosia.Documents.Pdf
```

## Quick Start

```csharp
using Mythosia.Documents.Pdf;

var loader = new PdfDocumentLoader();
IReadOnlyList<DoclingDocument> docs = await loader.LoadAsync("docs/manual.pdf");

string markdown = docs[0].ToMarkdown();
```

### With RAG Pipeline

```csharp
var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocuments(new PdfDocumentLoader(), "docs/manual.pdf")
    );

// Or auto-select loader by extension:
var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag.AddDocument("docs/manual.pdf"));
```

## Structured Extraction

The parser analyses font sizes and spatial layout to produce a structured `DoclingDocument`:

- **Headings** — text with font size exceeding the body font size (mode) by ≥15% is classified as heading level 1–3 based on size ratio.
- **Lists** — lines starting with bullet characters (`•`, `-`, `*`, etc.) or numbered patterns (`1.`, `a)`, `iv.`) are emitted as list items.
- **Paragraphs** — words are grouped into lines by Y-coordinate proximity. Consecutive body-text lines are merged into a single paragraph; vertical gaps larger than 1.4× line height trigger a paragraph break.
- **Fallback** — if `GetWords()` returns no results but raw page text exists, the text is preserved as a paragraph.

## Parser Options

```csharp
using Mythosia.Documents.Pdf;

var options = new PdfParserOptions
{
    Password = null,              // For encrypted PDFs
    IncludeMetadata = true,       // Extract title, author, page count
    IncludePageNumbers = false,   // Add page number headers
    NormalizeWhitespace = true,   // Collapse excessive whitespace (preserves newlines)
};

var loader = new PdfDocumentLoader(options: options);
```

## Custom Parser

Implement `IDocumentParser` and pass it to the loader:

```csharp
var loader = new PdfDocumentLoader(parser: new MyCustomPdfParser());
```

## Related Packages

| Package | Description |
|---------|-------------|
| [Mythosia.Documents.Abstractions](https://www.nuget.org/packages/Mythosia.Documents.Abstractions) | Core abstractions (DoclingDocument, IDocumentLoader) |
| [Mythosia.Documents.Office](https://www.nuget.org/packages/Mythosia.Documents.Office) | Word / Excel / PowerPoint loaders |
| [Mythosia.AI.Rag](https://www.nuget.org/packages/Mythosia.AI.Rag) | RAG pipeline |
