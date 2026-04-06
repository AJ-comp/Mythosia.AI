# Mythosia.Documents.Hwp

HWP (Hangul Word Processor) document loader. Parses `.hwp` files into `DoclingDocument` structured models via [HwpLibSharp](https://github.com/rkttu/libhwpsharp). Provides section/paragraph text extraction with table support.

## Installation

```bash
dotnet add package Mythosia.Documents.Hwp
```

## Quick Start

```csharp
using Mythosia.Documents.Hwp;

var loader = new HwpDocumentLoader();
IReadOnlyList<DoclingDocument> docs = await loader.LoadAsync("docs/report.hwp");

string markdown = docs[0].ToMarkdown();
```

### With RAG Pipeline

```csharp
var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocuments(new HwpDocumentLoader(), "docs/report.hwp")
    );

// Or auto-select loader by extension:
var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag.AddDocument("docs/report.hwp"));
```

## Table Rendering

HWP documents default to `SemanticTableSerializer`. Form-style tables (e.g., application forms, key-value layouts) are automatically detected and rendered with bold group labels (`**label**`) for improved RAG chunking context.

To override:

```csharp
using Mythosia.Documents.Elements;

var docs = await loader.LoadAsync("docs/report.hwp");
docs[0].TableSerializer = new GridTableSerializer(); // switch to pipe table
```

## Structured Extraction

The parser iterates HWP sections and paragraphs to produce a structured `DoclingDocument`:

- **Headings** — paragraphs with outline styles (Korean "개요" 1–9, Heading 1–9) are classified as headings.
- **Titles** — paragraphs with Korean "제목" or "Title" style are emitted as document titles.
- **Tables** — inline table controls are fully extracted with cell spans (ColSpan/RowSpan) preserved. Semantic form detection is applied by default.
- **Paragraphs** — all remaining text paragraphs are emitted as body paragraphs.

## Parser Options

```csharp
using Mythosia.Documents.Hwp;

var options = new HwpParserOptions
{
    IncludeMetadata = true,           // Extract section count metadata
    NormalizeWhitespace = true,       // Collapse excessive whitespace
    IncludeSectionHeaders = false,    // Emit section boundary headings
    ExcludeControlChars = true,       // Remove HWP control characters
};

var loader = new HwpDocumentLoader(options: options);
```

## Custom Parser

Implement `IDocumentParser` and pass it to the loader:

```csharp
var loader = new HwpDocumentLoader(parser: new MyCustomHwpParser());
```

## Related Packages

| Package | Description |
|---------|-------------|
| [Mythosia.Documents.Abstractions](https://www.nuget.org/packages/Mythosia.Documents.Abstractions) | Core abstractions (DoclingDocument, IDocumentLoader) |
| [Mythosia.Documents.Office](https://www.nuget.org/packages/Mythosia.Documents.Office) | Word / Excel / PowerPoint loaders |
| [Mythosia.Documents.Pdf](https://www.nuget.org/packages/Mythosia.Documents.Pdf) | PDF loader |
| [Mythosia.AI.Rag](https://www.nuget.org/packages/Mythosia.AI.Rag) | RAG pipeline |
