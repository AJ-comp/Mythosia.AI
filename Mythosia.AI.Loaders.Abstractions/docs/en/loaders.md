# Mythosia.AI Loaders Guide

## Overview

Loaders convert file sources into `RagDocument` items consumed by the RAG pipeline. In most cases you install
one of the loader packages and use `RagBuilder` to ingest documents. Implement `IDocumentLoader` only when
you need custom ingestion logic.

## Packages

- **Mythosia.AI.Loaders.Abstractions** — `IDocumentLoader`, `RagDocument`, `ParsedDocument`
- **Mythosia.AI.Loaders.Office** — Word/Excel/PowerPoint (.docx/.xlsx/.pptx) via OpenXml
- **Mythosia.AI.Loaders.Pdf** — PDF loader with PdfPig parser

## Installation

```bash
dotnet add package Mythosia.AI.Loaders.Office
dotnet add package Mythosia.AI.Loaders.Pdf
```

## Quick Start

```csharp
using Mythosia.AI.Rag;

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("docs/manual.pdf")
        .AddDocument("docs/plan.docx")
        .AddDocuments("./knowledge-base")
    );
```

`AddDocument(...)` and `AddDocuments(...)` automatically select a loader based on extension:

- `.docx` -> `WordDocumentLoader`
- `.xlsx` -> `ExcelDocumentLoader`
- `.pptx` -> `PowerPointDocumentLoader`
- `.pdf` -> `PdfDocumentLoader`
- otherwise -> `PlainTextDocumentLoader`

## Per-Extension Routing

Use per-extension routing when you want different loaders or splitters per file type.

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Pdf;
using Mythosia.AI.Loaders.Office.Word;
using Mythosia.AI.Rag.Splitters;

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocuments("./docs", src => src
            .WithExtension(".pdf")
            .WithLoader(new PdfDocumentLoader())
            .WithTextSplitter(new CharacterTextSplitter(800, 80))
        )
        .AddDocuments("./docs", src => src
            .WithExtension(".docx")
            .WithLoader(new WordDocumentLoader())
            .WithTextSplitter(new TokenTextSplitter(600, 60))
        )
        .AddDocuments("./docs", src => src
            .WithTextSplitter(new CharacterTextSplitter(400, 40))
        )
    );
```

## Office Loaders

Supported formats: **Word (.docx)**, **Excel (.xlsx)**, **PowerPoint (.pptx)**.

```csharp
using Mythosia.AI.Loaders.Office;
using Mythosia.AI.Loaders.Office.Word;

var options = new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
};

var loader = new WordDocumentLoader(options: options);

.WithRag(rag => rag.AddDocuments(loader, "docs/report.docx"));
```

`OfficeParserOptions` applies to all OpenXml parsers:

- `IncludeMetadata`
- `NormalizeWhitespace`
- `IncludeSheetNames` (Excel)
- `IncludeSlideNumbers` (PowerPoint)

## PDF Loader

```csharp
using Mythosia.AI.Loaders.Pdf;

var options = new PdfParserOptions
{
    IncludeMetadata = true,
    IncludePageNumbers = false,
    NormalizeWhitespace = true
};

var loader = new PdfDocumentLoader(options: options);

.WithRag(rag => rag.AddDocuments(loader, "docs/manual.pdf"));
```

### Custom Parser

To override the default parser, implement `IDocumentParser` (make sure `CanParse` handles PDFs) and pass it directly to the loader:

```csharp
using Mythosia.AI.Loaders.Pdf;

var loader = new PdfDocumentLoader(parser: new MyPdfParser());
```

## Custom Loader

```csharp
using Mythosia.AI.Loaders;

public class MarkdownDocumentLoader : IDocumentLoader
{
    public Task<IReadOnlyList<RagDocument>> LoadAsync(string source, CancellationToken ct = default)
    {
        var text = File.ReadAllText(source);
        return Task.FromResult<IReadOnlyList<RagDocument>>(new[]
        {
            new RagDocument
            {
                Id = Path.GetFileName(source),
                Content = text,
                Source = source,
                Metadata = { ["type"] = "markdown" }
            }
        });
    }
}
```

Register via the builder:

```csharp
.WithRag(rag => rag.AddDocuments(new MarkdownDocumentLoader(), "docs/intro.md"))
```

## Metadata Reference

Common metadata keys:

- Office: `type=office`, `office_type`, `filename`, `extension`, `parser`
- PDF: `type=pdf`, `filename`, `extension`, `parser`

Parser-specific metadata entries are preserved when available.
