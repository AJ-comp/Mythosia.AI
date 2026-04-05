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
| `HwpDocumentLoader` | `.hwp` | `Mythosia.Documents.Hwp` |
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

## HWP (.hwp)

解析韓國 Hangul 文書處理器（HWP）檔案。以獨立套件提供：

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

HWP 載入器會將文字、表格和標題結構轉換為 `DoclingDocument`，最終以 Markdown 格式輸出。表格會以 Markdown 表格（`| ... |`）呈現，因此搭配 `MarkdownTextSplitter` 使用時，表格結構在分塊過程中會被完整保留。

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

## 文件載入器與文字分割器的搭配

Word、Excel、PowerPoint 和 HWP 的文件載入器在內部透過 `DoclingDocument` 將檔案轉換為 **Markdown 格式**。表格會變成 Markdown 表格（`| 表頭 |` + `|---|` + `| 資料 |`），標題和程式碼區塊也以 Markdown 語法輸出。

因此，`MarkdownTextSplitter` 是 Office/HWP 文件最有效的選擇：

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter` 會按行拆分表格，並自動在每個分塊中包含表頭，確保搜尋結果中的表格資料保持完整。詳細資訊請參見[文字分割器](text-splitters.md)。
