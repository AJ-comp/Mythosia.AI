# Document Loaders

Document loaders parse files into structured `DoclingDocument` objects, which can then be passed to the RAG pipeline.

<a id="file-source-identity"></a>

## Keep file identity stable across registrations

Registering the same file through a relative path and an absolute path must update one document, while same-named files in different folders must stay separate. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` and `PdfDocumentLoader` now set `DoclingDocument.Source` to the normalized absolute file path, as the built-in TXT loaders do. RAG derives automatic document IDs from this value; explicit IDs remain caller-controlled. Default citations may therefore show absolute paths.

Previously stored relative-path IDs are not migrated or deleted automatically. Identify the old document ID, explicitly remove only that document from the relevant store and reindex it. Alternatively, index the complete source set into a new empty collection, validate it and switch the application to it. Reindexing only the new absolute-path ID in the existing collection leaves the old records behind. Do not delete unrelated documents. See [document identity and migration](rag.md#document-identity).

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
var loader = new PdfDocumentLoader(options: new PdfParserOptions
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
var loader = new WordDocumentLoader(options: new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("document.docx");
```

## Excel (.xlsx)

```csharp
var loader = new ExcelDocumentLoader(options: new OfficeParserOptions
{
    IncludeSheetNames = true,  // Prepend sheet name to each section
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(options: new OfficeParserOptions
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
var loader = new PdfDocumentLoader(options: new PdfParserOptions { IncludePageNumbers = true });
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

Use `MarkdownTextSplitter` when preserving Markdown headings, fenced blocks and table rows matters. It has no overlap argument. Repeated headings are outside the content budget; a whole fenced block or table header plus one row may exceed it. Count final chunks with the embedding model's tokenizer for a strict token limit. See [Text Splitters](text-splitters.md).

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000))
    );
```

Recognized GFM tables are split between rows, with the header and delimiter row repeated in each resulting table chunk. Outer pipes are optional (`Name | Value` is supported). This preserves the column labels; retrieval quality still depends on the documents, embeddings and queries.

---

## Want to dig deeper?

The pages below explain the parsing internals — useful if you want to customize table rendering, chunk by slide/sheet, or add support for a new file format. Skip them if `LoadAsync()` + `ToMarkdown()` is all you need.

- [Document Parsing — Big Picture](document-architecture-concept.md) — why parsing happens in two stages
- [DoclingDocument Data Model](document-architecture-data-model.md) — the structured tree each loader produces
- [Customizing the Output](document-architecture-customization.md) — table strategies, chunking patterns, custom parsers
