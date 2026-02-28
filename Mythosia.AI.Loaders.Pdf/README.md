# Mythosia.AI.Loaders.Pdf

## Package Summary

PDF document loader for Mythosia.AI.Rag. The default parser uses PdfPig and supports metadata extraction.

## Installation

```bash
dotnet add package Mythosia.AI.Loaders.Pdf
```

## Quick Start

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Pdf;

var loader = new PdfDocumentLoader();

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocuments(loader, "docs/manual.pdf")
    );
```

Auto-selection by extension is also supported:

```csharp
.WithRag(rag => rag.AddDocument("docs/manual.pdf"))
```

## Parser Options

Options apply to the default PdfPig parser:

```csharp
using Mythosia.AI.Loaders.Pdf;

var options = new PdfParserOptions
{
    IncludeMetadata = true,
    IncludePageNumbers = false,
    NormalizeWhitespace = true
};

var loader = new PdfDocumentLoader(options: options);
```

## Custom Parser

If you have a custom parser, implement `IDocumentParser` and pass it directly to the loader:

```csharp
using Mythosia.AI.Loaders.Pdf;

var loader = new PdfDocumentLoader(parser: new MyPdfParser());
```

## Documentation

- [Loaders Guide (EN)](../Mythosia.AI.Loaders.Abstractions/docs/en/loaders.md)
- [Loaders Guide (KO)](../Mythosia.AI.Loaders.Abstractions/docs/ko/loaders.md)
- [Loaders Guide (JA)](../Mythosia.AI.Loaders.Abstractions/docs/ja/loaders.md)
- [Loaders Guide (ZH)](../Mythosia.AI.Loaders.Abstractions/docs/zh/loaders.md)
