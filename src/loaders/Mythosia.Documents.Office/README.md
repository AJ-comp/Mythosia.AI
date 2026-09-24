# Mythosia.Documents.Office

Office document loaders for Word (.docx), Excel (.xlsx), and PowerPoint (.pptx). Parses documents into `DoclingDocument` structured models via OpenXml.

## Stable file identity

This fix is currently unreleased; see the [pending package notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/loaders/Mythosia.Documents.Office/RELEASE_NOTES.md#unreleased). Use a source build until the next package version is published.

Registering the same file through a relative path and an absolute path must update one document, while same-named files in different folders must stay separate. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` and `PdfDocumentLoader` now set `DoclingDocument.Source` to the normalized absolute file path, as the built-in TXT loaders do. RAG derives automatic document IDs from this value; explicit IDs remain caller-controlled. Default citations may therefore show absolute paths.

Previously stored relative-path IDs are not migrated or deleted automatically. Identify the old document ID, explicitly remove only that document from the relevant store and reindex it. Alternatively, index the complete source set into a new empty collection, validate it and switch the application to it. Reindexing only the new absolute-path ID in the existing collection leaves the old records behind. Do not delete unrelated documents. See [document identity and migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag.md#document-identity).

## Installation

```bash
dotnet add package Mythosia.Documents.Office
```

## Quick Start

```csharp
using Mythosia.Documents.Office.Word;

var loader = new WordDocumentLoader();
IReadOnlyList<DoclingDocument> docs = await loader.LoadAsync("docs/report.docx");

string markdown = docs[0].ToMarkdown();
```

### With RAG Pipeline

```csharp
var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "docs/report.docx")
    );

// Or auto-select loader by extension:
var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag.AddDocument("docs/report.docx"));
```

## Loaders

| Loader | Extensions | Namespace |
|--------|-----------|-----------|
| `WordDocumentLoader` | .docx | `Mythosia.Documents.Office.Word` |
| `ExcelDocumentLoader` | .xlsx | `Mythosia.Documents.Office.Excel` |
| `PowerPointDocumentLoader` | .pptx | `Mythosia.Documents.Office.PowerPoint` |

## Structured Extraction

The OpenXml parsers produce a structured `DoclingDocument` that can be serialized to Markdown or consumed directly by a RAG pipeline.

- **Word**: headings are tracked with a hierarchy stack, so equal-level headings stay siblings, lower-level headings nest under the nearest parent heading, and document titles reset the heading stack.
- **Word tables**: tables are attached to the current heading/title context and preserve basic row, column, and span information.
- **PowerPoint**: slide text shapes and table graphic frames are read in document order, preserving sequences such as text -> table -> text in Markdown output.
- **PowerPoint titles and lists**: title placeholders become section headings, and bullet/numbered paragraphs become list items.
- **Excel**: workbook cells are parsed into structured table data, with optional sheet-name context.

## Parser Options

```csharp
using Mythosia.Documents.Office;
using Mythosia.Documents.Office.Excel;

var options = new OfficeParserOptions
{
    IncludeMetadata = true,       // Extract title, author, etc.
    NormalizeWhitespace = true,   // Collapse excessive whitespace
    IncludeSheetNames = true,     // Sheet names in Excel output
    IncludeSlideNumbers = true,   // Slide numbers in PowerPoint output
};

var loader = new ExcelDocumentLoader(options: options);
```

## Custom Parser

Implement `IDocumentParser` and pass it to the loader:

```csharp
var loader = new WordDocumentLoader(parser: new MyCustomWordParser());
```

## Related Packages

| Package | Description |
|---------|-------------|
| [Mythosia.Documents.Abstractions](https://www.nuget.org/packages/Mythosia.Documents.Abstractions) | Core abstractions (DoclingDocument, IDocumentLoader) |
| [Mythosia.Documents.Pdf](https://www.nuget.org/packages/Mythosia.Documents.Pdf) | PDF loader |
| [Mythosia.AI.Rag](https://www.nuget.org/packages/Mythosia.AI.Rag) | RAG pipeline |
