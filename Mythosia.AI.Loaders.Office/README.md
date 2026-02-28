# Mythosia.AI.Loaders.Office

## Package Summary

Office document loaders for Mythosia.AI.Rag. Includes OpenXml-based parsers for **.docx**, **.xlsx**, and **.pptx**.

## Installation

```bash
dotnet add package Mythosia.AI.Loaders.Office
```

## Quick Start

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Loaders.Office.Word;

var loader = new WordDocumentLoader();

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocuments(loader, "docs/report.docx")
    );
```

You can also rely on auto-selection by extension:

```csharp
.WithRag(rag => rag.AddDocument("docs/report.docx"))
```

## Parser Options

```csharp
using Mythosia.AI.Loaders.Office;
using Mythosia.AI.Loaders.Office.Excel;

var options = new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true,
    IncludeSheetNames = true,
    IncludeSlideNumbers = true
};

var loader = new ExcelDocumentLoader(options: options);
```

## Documentation

- [Loaders Guide (EN)](../Mythosia.AI.Loaders.Abstractions/docs/en/loaders.md)
- [Loaders Guide (KO)](../Mythosia.AI.Loaders.Abstractions/docs/ko/loaders.md)
- [Loaders Guide (JA)](../Mythosia.AI.Loaders.Abstractions/docs/ja/loaders.md)
- [Loaders Guide (ZH)](../Mythosia.AI.Loaders.Abstractions/docs/zh/loaders.md)
