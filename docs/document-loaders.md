# Document Loaders

Document loaders parse files into structured `DoclingDocument` objects, which can then be passed to the RAG pipeline.

## Installation

Office and PDF loaders are included in `Mythosia.AI.Rag`. For standalone use:

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## Supported Formats

| Loader | Format | Package |
|--------|--------|---------|
| `PdfDocumentLoader` | `.pdf` | `Mythosia.Documents.Pdf` |
| `WordDocumentLoader` | `.docx` | `Mythosia.Documents.Office` |
| `ExcelDocumentLoader` | `.xlsx` | `Mythosia.Documents.Office` |
| `PowerPointDocumentLoader` | `.pptx` | `Mythosia.Documents.Office` |
| `HwpDocumentLoader` | `.hwp` | `Mythosia.Documents.Hwp` |
| `PlainTextDocumentLoader` | `.txt`, `.md`, etc. | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions
{
    Password = "secret",           // For encrypted PDFs
    IncludeMetadata = true,        // Extract title, author
    IncludePageNumbers = true,     // Add page number markers
    NormalizeWhitespace = true     // Collapse extra whitespace
});

var docs = await loader.LoadAsync("report.pdf");
```

## Word (.docx)

```csharp
var loader = new WordDocumentLoader(new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("document.docx");
```

## Excel (.xlsx)

```csharp
var loader = new ExcelDocumentLoader(new OfficeParserOptions
{
    IncludeSheetNames = true,  // Prepend sheet name to each section
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // Prepend slide number to each section
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## HWP (.hwp)

Parses Korean Hangul Word Processor (HWP) files. Available as a separate package:

```bash
dotnet add package Mythosia.Documents.Hwp
```

```csharp
var loader = new HwpDocumentLoader(options: new HwpParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true,
    IncludeSectionHeaders = false
});

var docs = await loader.LoadAsync("report.hwp");
```

The HWP loader converts text, tables, and heading structure into a `DoclingDocument`, which is then output as Markdown. Tables are rendered as Markdown tables (`| ... |`), so using `MarkdownTextSplitter` preserves table structure throughout chunking.

## Using in RAG

Loaders are integrated automatically when using `.AddDocument()` in `RagBuilder`. To load manually and add the result:

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("report.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("report.pdf")  // auto-detects format
        .AddDocument("notes.docx")
    );
```

## DoclingDocument Structure

Each loaded file becomes a `DoclingDocument` with a hierarchical element tree:

```csharp
var docs = await loader.LoadAsync("report.pdf");
var doc = docs[0];

Console.WriteLine(doc.Title);   // Document title
Console.WriteLine(doc.Source);  // File path

foreach (var item in doc.Document)
{
    switch (item)
    {
        case SectionHeaderItem h: Console.WriteLine($"## {h.Text}"); break;
        case TextItem t:          Console.WriteLine(t.Text); break;
        case TableItem table:     /* process table cells */ break;
        case CodeItem code:       Console.WriteLine(code.Text); break;
    }
}
```

**Element types:** `TextItem`, `SectionHeaderItem`, `TitleItem`, `ListItem`, `TableItem`, `CodeItem`, `FormulaItem`, `PictureItem`, `GroupItem`, `RefItem`

## Processing Pipeline Overview

Documents go through three stages before becoming RAG-searchable chunks. Each stage is handled by a different package.

```text
┌─────────────────────────────────────────────────────────────┐
│  1. Parsing (Documents.Hwp / Documents.Office / Documents.Pdf)
│     .hwp, .pdf, .docx, etc. → DoclingDocument (structured model)
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. Serialization (Documents.Abstractions)
│     DoclingDocument → Markdown string
│     MarkdownSerializer converts headings, tables, code blocks
│     into Markdown syntax.
│     Table rendering is swappable via ITableSerializer.
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. Chunking (AI.Rag)
│     Markdown string → searchable chunk list
│     MarkdownTextSplitter splits by headers into sections,
│     then cascades: paragraph → line → word boundary.
└─────────────────────────────────────────────────────────────┘
```

**Stage 1 (Parsing)** — Each document loader (`HwpDocumentLoader`, `PdfDocumentLoader`, etc.) reads the original file and converts it into a `DoclingDocument`, a structured model containing text, headings, tables, and code blocks in a tree structure.

**Stage 2 (Serialization)** — When `DoclingDocument.ToMarkdown()` is called, the internal `MarkdownSerializer` traverses the tree and produces a Markdown string. Table rendering can be swapped via `ITableSerializer`. HWP documents default to `SemanticTableSerializer`, which renders form-style tables with bold group labels.

**Stage 3 (Chunking)** — The RAG pipeline's `MarkdownTextSplitter` receives the Markdown string and splits it into search-friendly chunks. It organizes sections by headers (`#`, `##`, etc.) and automatically includes breadcrumbs (parent header paths) in each chunk.

Because these three stages are decoupled, adding a new document loader or changing the table rendering strategy does not affect the other stages.

## Document Loaders & Text Splitters Integration

`MarkdownTextSplitter` is the most effective choice for Office/HWP documents:

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter` splits tables at row boundaries and automatically includes headers in each chunk, so table data remains intact in search results. See [Text Splitters](text-splitters.md) for details.
