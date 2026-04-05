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

## Document Loaders & Text Splitters Integration

Document loaders for Word, Excel, PowerPoint, and HWP internally convert files through `DoclingDocument` into **Markdown format**. During this process, tables become Markdown tables (`| Header |` + `|---|` + `| Data |`), and headings and code blocks are also rendered as Markdown syntax.

This makes `MarkdownTextSplitter` the most effective choice for Office/HWP documents:

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter` splits tables at row boundaries and automatically includes headers in each chunk, so table data remains intact in search results. See [Text Splitters](text-splitters.md) for details.
