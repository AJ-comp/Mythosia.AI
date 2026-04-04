# Chargeurs de documents

Les chargeurs de documents analysent les fichiers en objets `DoclingDocument` structurés, qui peuvent ensuite être transmis au pipeline RAG.

## Installation

Les chargeurs Office et PDF sont inclus dans `Mythosia.AI.Rag`. Pour une utilisation autonome :

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## Formats pris en charge

| Chargeur | Format | Package |
|--------|--------|---------|
| `PdfDocumentLoader` | `.pdf` | `Mythosia.Documents.Pdf` |
| `WordDocumentLoader` | `.docx` | `Mythosia.Documents.Office` |
| `ExcelDocumentLoader` | `.xlsx` | `Mythosia.Documents.Office` |
| `PowerPointDocumentLoader` | `.pptx` | `Mythosia.Documents.Office` |
| `PlainTextDocumentLoader` | `.txt`, `.md`, etc. | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions
{
    Password = "secret",            // Pour les PDF chiffrés
    IncludeMetadata = true,         // Extraire le titre, l'auteur
    IncludePageNumbers = true,      // Ajouter des marqueurs de numéro de page
    NormalizeWhitespace = true      // Condenser les espaces superflus
});

var docs = await loader.LoadAsync("rapport.pdf");
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
    IncludeSheetNames = true,  // Préfixer chaque section par le nom de la feuille
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("tableur.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // Préfixer chaque section par le numéro de diapositive
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## Utiliser dans le RAG

Les chargeurs sont intégrés automatiquement lors de l'utilisation de `.AddDocument()` dans `RagBuilder`. Pour charger manuellement et ajouter le résultat :

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("rapport.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("rapport.pdf")   // format détecté automatiquement
        .AddDocument("notes.docx")
    );
```

## Structure de DoclingDocument

Chaque fichier chargé devient un `DoclingDocument` avec un arbre d'éléments hiérarchique :

```csharp
var docs = await loader.LoadAsync("rapport.pdf");
var doc = docs[0];

Console.WriteLine(doc.Title);   // Titre du document
Console.WriteLine(doc.Source);  // Chemin du fichier

foreach (var item in doc.Document)
{
    switch (item)
    {
        case SectionHeaderItem h: Console.WriteLine($"## {h.Text}"); break;
        case TextItem t:          Console.WriteLine(t.Text); break;
        case TableItem table:     /* traiter les cellules du tableau */ break;
        case CodeItem code:       Console.WriteLine(code.Text); break;
    }
}
```

**Types d'éléments :** `TextItem`, `SectionHeaderItem`, `TitleItem`, `ListItem`, `TableItem`, `CodeItem`, `FormulaItem`, `PictureItem`, `GroupItem`, `RefItem`
