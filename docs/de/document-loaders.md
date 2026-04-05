# Dokument-Loader

Dokument-Loader parsen Dateien in strukturierte `DoclingDocument`-Objekte, die dann an die RAG-Pipeline übergeben werden können.

## Installation

Office- und PDF-Loader sind in `Mythosia.AI.Rag` enthalten. Für die eigenständige Verwendung:

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## Unterstützte Formate

| Loader | Format | Paket |
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
    Password = "geheim",            // Für verschlüsselte PDFs
    IncludeMetadata = true,         // Titel, Autor extrahieren
    IncludePageNumbers = true,      // Seitennummernmarkierungen hinzufügen
    NormalizeWhitespace = true      // Überschüssige Leerzeichen zusammenfassen
});

var docs = await loader.LoadAsync("bericht.pdf");
```

## Word (.docx)

```csharp
var loader = new WordDocumentLoader(new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("dokument.docx");
```

## Excel (.xlsx)

```csharp
var loader = new ExcelDocumentLoader(new OfficeParserOptions
{
    IncludeSheetNames = true,  // Tabellenblattname jedem Abschnitt voranstellen
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("tabelle.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // Foliennummer jedem Abschnitt voranstellen
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("praesentation.pptx");
```

## HWP (.hwp)

Parsing von koreanischen Hangul-Textverarbeitungsdateien (HWP). Verfügbar als separates Paket:

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

var docs = await loader.LoadAsync("bericht.hwp");
```

Der HWP-Loader wandelt Text, Tabellen und Überschriftenstruktur in ein `DoclingDocument` um, das anschließend als Markdown ausgegeben wird. Tabellen werden als Markdown-Tabellen (`| ... |`) dargestellt, sodass `MarkdownTextSplitter` die Tabellenstruktur beim Chunking vollständig erhält.

## In RAG verwenden

Loader werden automatisch integriert, wenn `.AddDocument()` im `RagBuilder` verwendet wird. Um manuell zu laden und das Ergebnis hinzuzufügen:

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("bericht.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("bericht.pdf")    // Format wird automatisch erkannt
        .AddDocument("notizen.docx")
    );
```

## DoclingDocument-Struktur

Jede geladene Datei wird zu einem `DoclingDocument` mit einem hierarchischen Element-Baum:

```csharp
var docs = await loader.LoadAsync("bericht.pdf");
var doc = docs[0];

Console.WriteLine(doc.Title);   // Dokumenttitel
Console.WriteLine(doc.Source);  // Dateipfad

foreach (var item in doc.Document)
{
    switch (item)
    {
        case SectionHeaderItem h: Console.WriteLine($"## {h.Text}"); break;
        case TextItem t:          Console.WriteLine(t.Text); break;
        case TableItem table:     /* Tabellenzellen verarbeiten */ break;
        case CodeItem code:       Console.WriteLine(code.Text); break;
    }
}
```

**Elementtypen:** `TextItem`, `SectionHeaderItem`, `TitleItem`, `ListItem`, `TableItem`, `CodeItem`, `FormulaItem`, `PictureItem`, `GroupItem`, `RefItem`

## Dokument-Loader & Text-Splitter Integration

Dokument-Loader für Word, Excel, PowerPoint und HWP wandeln Dateien intern über `DoclingDocument` in **Markdown-Format** um. Tabellen werden dabei zu Markdown-Tabellen (`| Header |` + `|---|` + `| Daten |`), und Überschriften sowie Codeblöcke werden ebenfalls als Markdown-Syntax ausgegeben.

Deshalb ist `MarkdownTextSplitter` die effektivste Wahl für Office/HWP-Dokumente:

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter` teilt Tabellen zeilenweise auf und fügt automatisch Header in jeden Chunk ein, sodass Tabellendaten in den Suchergebnissen vollständig erhalten bleiben. Weitere Details finden Sie unter [Text-Splitter](text-splitters.md).
