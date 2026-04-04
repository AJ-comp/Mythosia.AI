# 文件載入器

文件載入器將檔案解析為結構化的 `DoclingDocument` 物件，然後可以傳遞給 RAG 管線。

## 安裝

Office 和 PDF 載入器包含在 `Mythosia.AI.Rag` 中。如需單獨使用：

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## 支援的格式

| 載入器 | 格式 | 套件名稱 |
|--------|------|----------|
| `PdfDocumentLoader` | `.pdf` | `Mythosia.Documents.Pdf` |
| `WordDocumentLoader` | `.docx` | `Mythosia.Documents.Office` |
| `ExcelDocumentLoader` | `.xlsx` | `Mythosia.Documents.Office` |
| `PowerPointDocumentLoader` | `.pptx` | `Mythosia.Documents.Office` |
| `PlainTextDocumentLoader` | `.txt`、`.md` 等 | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions
{
    Password = "secret",
    IncludeMetadata = true,
    IncludePageNumbers = true,
    NormalizeWhitespace = true
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
    IncludeSheetNames = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(new OfficeParserOptions
{
    IncludeSlideNumbers = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## 在 RAG 中使用

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("report.pdf")
        .AddDocument("notes.docx")
    );
```

## DoclingDocument 結構

```csharp
var docs = await loader.LoadAsync("report.pdf");
var doc = docs[0];

Console.WriteLine(doc.Title);
Console.WriteLine(doc.Source);

foreach (var item in doc.Document)
{
    switch (item)
    {
        case SectionHeaderItem h: Console.WriteLine($"## {h.Text}"); break;
        case TextItem t:          Console.WriteLine(t.Text); break;
        case TableItem table:     /* 處理表格儲存格 */ break;
        case CodeItem code:       Console.WriteLine(code.Text); break;
    }
}
```

**元素類型：** `TextItem`、`SectionHeaderItem`、`TitleItem`、`ListItem`、`TableItem`、`CodeItem`、`FormulaItem`、`PictureItem`、`GroupItem`、`RefItem`
