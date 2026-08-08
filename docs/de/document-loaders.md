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
var loader = new PdfDocumentLoader(options: new PdfParserOptions
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
var loader = new WordDocumentLoader(options: new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("dokument.docx");
```

## Excel (.xlsx)

```csharp
var loader = new ExcelDocumentLoader(options: new OfficeParserOptions
{
    IncludeSheetNames = true,  // Tabellenblattname jedem Abschnitt voranstellen
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("tabelle.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(options: new OfficeParserOptions
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
var loader = new PdfDocumentLoader(options: new PdfParserOptions { IncludePageNumbers = true });
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

## Verarbeitungs-Pipeline Übersicht

Dokumente durchlaufen drei Stufen, bevor sie zu RAG-durchsuchbaren Chunks werden. Jede Stufe wird von einem anderen Paket verarbeitet.

```text
┌─────────────────────────────────────────────────────────────┐
│  1. Parsing (Documents.Hwp / Documents.Office / Documents.Pdf)
│     .hwp, .pdf, .docx usw. → DoclingDocument (strukturiertes Modell)
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. Serialisierung (Documents.Abstractions)
│     DoclingDocument → Markdown-String
│     MarkdownSerializer wandelt Überschriften, Tabellen,
│     Codeblöcke in Markdown-Syntax um.
│     Tabellen-Rendering ist über ITableSerializer austauschbar.
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. Chunking (AI.Rag)
│     Markdown-String → durchsuchbare Chunk-Liste
│     MarkdownTextSplitter teilt nach Überschriften in Sektionen,
│     dann kaskadierend: Absatz → Zeile → Wortgrenze.
└─────────────────────────────────────────────────────────────┘
```

**Stufe 1 (Parsing)** — Jeder Dokument-Loader (`HwpDocumentLoader`, `PdfDocumentLoader` usw.) liest die Originaldatei und wandelt sie in ein `DoclingDocument` um — ein strukturiertes Modell mit Text, Überschriften, Tabellen und Codeblöcken in einer Baumstruktur.

**Stufe 2 (Serialisierung)** — Beim Aufruf von `DoclingDocument.ToMarkdown()` durchläuft der interne `MarkdownSerializer` den Baum und erzeugt einen Markdown-String. Das Tabellen-Rendering kann über `ITableSerializer` ausgetauscht werden. HWP-Dokumente verwenden standardmäßig `SemanticTableSerializer`, der Formulartabellen mit fetten Gruppenlabels rendert.

**Stufe 3 (Chunking)** — Der `MarkdownTextSplitter` der RAG-Pipeline empfängt den Markdown-String und teilt ihn in suchfreundliche Chunks auf. Er organisiert Sektionen nach Überschriften (`#`, `##` usw.) und fügt automatisch Breadcrumbs (übergeordnete Überschriftenpfade) in jeden Chunk ein.

Da diese drei Stufen entkoppelt sind, beeinflusst das Hinzufügen eines neuen Dokument-Loaders oder das Ändern der Tabellen-Rendering-Strategie die anderen Stufen nicht.

## Dokument-Loader & Text-Splitter Integration

`MarkdownTextSplitter` ist die effektivste Wahl für Office/HWP-Dokumente:

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter` teilt Tabellen zeilenweise auf und fügt automatisch Header in jeden Chunk ein, sodass Tabellendaten in den Suchergebnissen vollständig erhalten bleiben. Weitere Details finden Sie unter [Text-Splitter](text-splitters.md).
