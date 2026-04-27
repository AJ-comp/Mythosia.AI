# Mythosia.Documents.Office

Office document loaders for Word (.docx), Excel (.xlsx), and PowerPoint (.pptx). Parses documents into `DoclingDocument` structured models via OpenXml.

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
