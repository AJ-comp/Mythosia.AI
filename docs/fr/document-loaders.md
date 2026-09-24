# Chargeurs de documents

Les chargeurs de documents analysent les fichiers en objets `DoclingDocument` structurés, qui peuvent ensuite être transmis au pipeline RAG.

<a id="file-source-identity"></a>

## Conserver une identité stable pour chaque fichier

Un même fichier enregistré par un chemin relatif ou absolu doit mettre à jour un seul document, tandis que les fichiers homonymes de dossiers différents doivent rester distincts. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` et `PdfDocumentLoader` définissent désormais `DoclingDocument.Source` avec le chemin absolu normalisé, comme les chargeurs TXT intégrés. RAG en dérive les ID automatiques ; les ID explicites restent sous votre contrôle. Les citations par défaut peuvent donc afficher un chemin absolu.

Les anciens ID relatifs ne sont ni migrés ni supprimés automatiquement. Identifiez l’ancien ID, supprimez explicitement ce seul document dans le stockage concerné, puis réindexez-le. Vous pouvez aussi indexer toutes les sources dans une nouvelle collection vide, la valider, puis y basculer l’application. Réindexer uniquement le nouvel ID absolu dans la collection existante laisse les anciens enregistrements. Ne supprimez pas les documents sans rapport. Voir [identité et migration](rag.md#document-identity).

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
| `HwpDocumentLoader` | `.hwp` | `Mythosia.Documents.Hwp` |
| `PlainTextDocumentLoader` | `.txt`, `.md`, etc. | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(options: new PdfParserOptions
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
    IncludeSheetNames = true,  // Préfixer chaque section par le nom de la feuille
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("tableur.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(options: new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // Préfixer chaque section par le numéro de diapositive
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## HWP (.hwp)

Analyse les fichiers du traitement de texte coréen Hangul (HWP). Disponible en tant que package séparé :

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

Le chargeur HWP convertit le texte, les tableaux et la structure des titres en `DoclingDocument`, qui est ensuite restitué au format Markdown. Les tableaux sont rendus en tableaux Markdown (`| ... |`), de sorte que l'utilisation de `MarkdownTextSplitter` préserve la structure des tableaux tout au long du découpage.

## Utiliser dans le RAG

Les chargeurs sont intégrés automatiquement lors de l'utilisation de `.AddDocument()` dans `RagBuilder`. Pour charger manuellement et ajouter le résultat :

```csharp
var loader = new PdfDocumentLoader(options: new PdfParserOptions { IncludePageNumbers = true });
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

## Aperçu du pipeline de traitement

Les documents passent par trois étapes avant de devenir des fragments recherchables par RAG. Chaque étape est gérée par un package différent.

```text
┌─────────────────────────────────────────────────────────────┐
│  1. Analyse (Documents.Hwp / Documents.Office / Documents.Pdf)
│     .hwp, .pdf, .docx, etc. → DoclingDocument (modèle structuré)
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. Sérialisation (Documents.Abstractions)
│     DoclingDocument → chaîne Markdown
│     MarkdownSerializer convertit titres, tableaux, blocs de code
│     en syntaxe Markdown.
│     Le rendu des tableaux est interchangeable via ITableSerializer.
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. Découpage (AI.Rag)
│     Chaîne Markdown → liste de fragments recherchables
│     MarkdownTextSplitter découpe par titres en sections,
│     puis en cascade : paragraphe → ligne → limite de mot.
└─────────────────────────────────────────────────────────────┘
```

**Étape 1 (Analyse)** — Chaque chargeur de documents (`HwpDocumentLoader`, `PdfDocumentLoader`, etc.) lit le fichier original et le convertit en `DoclingDocument`, un modèle structuré contenant texte, titres, tableaux et blocs de code dans une arborescence.

**Étape 2 (Sérialisation)** — Lorsque `DoclingDocument.ToMarkdown()` est appelé, le `MarkdownSerializer` interne parcourt l'arborescence et produit une chaîne Markdown. Le rendu des tableaux peut être remplacé via `ITableSerializer`. Les documents HWP utilisent par défaut `SemanticTableSerializer`, qui rend les tableaux de formulaire avec des libellés de groupe en gras.

**Étape 3 (Découpage)** — Le `MarkdownTextSplitter` du pipeline RAG reçoit la chaîne Markdown et la découpe en fragments adaptés à la recherche. Il organise les sections par titres (`#`, `##`, etc.) et inclut automatiquement des fils d'Ariane (chemins des titres parents) dans chaque fragment.

Ces trois étapes étant découplées, l'ajout d'un nouveau chargeur de documents ou la modification de la stratégie de rendu des tableaux n'affecte pas les autres étapes.

## Intégration chargeurs de documents et découpeurs de texte

Utilisez `MarkdownTextSplitter` pour conserver titres, blocs de code et lignes de tableau. Il n’a pas d’argument de chevauchement. Les titres répétés sont hors budget ; un bloc entier ou l’en-tête avec une ligne peut le dépasser. Vérifiez les limites strictes avec le tokenizer du modèle sur les fragments finaux. Voir [Découpeurs de texte](text-splitters.md).

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000))
    );
```

Les tableaux GFM reconnus sont divisés entre les lignes ; l’en-tête et la ligne de séparation sont répétés dans chaque fragment de tableau. Les barres extérieures sont facultatives (`Name | Value` est accepté). Les noms des colonnes sont conservés ; la qualité de recherche dépend toujours des documents, embeddings et questions.
