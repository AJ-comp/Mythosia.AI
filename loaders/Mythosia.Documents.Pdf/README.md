# Mythosia.Documents.Pdf

PDF document loader. Parses PDF files into `DoclingDocument` structured models via [PdfPig](https://github.com/UglyToad/PdfPig). Supports encrypted PDFs, metadata extraction, and page number headers.

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
var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocuments(new PdfDocumentLoader(), "docs/manual.pdf")
    );

// Or auto-select loader by extension:
var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag.AddDocument("docs/manual.pdf"));
```

## Parser Options

```csharp
using Mythosia.Documents.Pdf;

var options = new PdfParserOptions
{
    Password = null,              // For encrypted PDFs
    IncludeMetadata = true,       // Extract title, author, page count
    IncludePageNumbers = false,   // Add page number headers
    NormalizeWhitespace = true,   // Collapse excessive whitespace
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
