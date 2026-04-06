# Mythosia.Documents.Abstractions

Core document abstractions for structured document loading and parsing. Framework-agnostic — usable with any RAG pipeline or document processing system.

## Installation

```bash
dotnet add package Mythosia.Documents.Abstractions
```

## Key Types

### DoclingDocument

Unified structured document representation following the [docling](https://github.com/DS4SD/docling) convention. Content items are stored in flat lists; the tree structure is maintained via body/furniture root nodes.

```csharp
using Mythosia.Documents;

var doc = new DoclingDocument
{
    Name = "report",
    Source = "docs/report.pdf",
};

// Builder API
doc.AddTitle("Annual Report");
doc.AddHeading("Revenue", level: 2);
doc.AddParagraph("Total revenue increased by 15%.");
doc.AddCode("var x = 42;", language: "csharp");

// Export to Markdown
string markdown = doc.ToMarkdown();

// Optional: override table rendering strategy
doc.TableSerializer = new SemanticTableSerializer();
string semanticMarkdown = doc.ToMarkdown();
```

For plain-text content that should be preserved as-is, use `RawContent`:

```csharp
var doc = new DoclingDocument
{
    Name = "notes",
    Source = "notes.txt",
    RawContent = rawText, // ToMarkdown() returns this directly
};
```

### Table Serialization

Table rendering is pluggable via `ITableSerializer`. The default is `GridTableSerializer` (standard Markdown pipe table). Switch to `SemanticTableSerializer` for form-style documents:

```csharp
using Mythosia.Documents.Elements;

// Default: pipe table
var doc = new DoclingDocument { Name = "report" };
string md = doc.ToMarkdown(); // uses GridTableSerializer

// Semantic: bold group labels for form-style tables
doc.TableSerializer = new SemanticTableSerializer();
string md2 = doc.ToMarkdown(); // uses SemanticTableSerializer
```

| Serializer | Output Style |
|------|-------------|
| `GridTableSerializer` | Standard Markdown pipe table (default) |
| `SemanticTableSerializer` | Form-style with `**bold labels**` and inline data |

### IDocumentLoader

```csharp
public interface IDocumentLoader
{
    Task<IReadOnlyList<DoclingDocument>> LoadAsync(
        string source, CancellationToken cancellationToken = default);
}
```

### IDocumentParser

```csharp
public interface IDocumentParser
{
    bool CanParse(string source);
    Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default);
}
```

### Element Types (Mythosia.Documents.Elements)

| Type | Description |
|------|-------------|
| `TextItem` | Paragraph, generic text |
| `TitleItem` | Document title |
| `SectionHeaderItem` | Section heading (H1–H6) |
| `CodeItem` | Code block with language |
| `DocListItem` | List item (ordered/unordered) |
| `TableItem` / `TableData` / `TableCell` | Table structure |
| `TableSemanticView` | Semantic group/column analysis for table layout |
| `PictureItem` | Image placeholder |
| `GroupItem` | Container (chapter, slide, sheet) |

## Related Packages

| Package | Description |
|---------|-------------|
| [Mythosia.Documents.Hwp](https://www.nuget.org/packages/Mythosia.Documents.Hwp) | HWP (Korean word processor) loader |
| [Mythosia.Documents.Office](https://www.nuget.org/packages/Mythosia.Documents.Office) | Word / Excel / PowerPoint loaders |
| [Mythosia.Documents.Pdf](https://www.nuget.org/packages/Mythosia.Documents.Pdf) | PDF loader (PdfPig) |
| [Mythosia.AI.Rag](https://www.nuget.org/packages/Mythosia.AI.Rag) | RAG pipeline that consumes DoclingDocument |
