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

## 處理管線概覽

文件在成為 RAG 可搜尋的分塊之前需要經過三個階段。每個階段由不同的套件負責。

```text
┌─────────────────────────────────────────────────────────────┐
│  1. 解析 (Documents.Hwp / Documents.Office / Documents.Pdf)
│     .hwp, .pdf, .docx 等 → DoclingDocument（結構化模型）
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. 序列化 (Documents.Abstractions)
│     DoclingDocument → Markdown 字串
│     MarkdownSerializer 將標題、表格、程式碼區塊
│     轉換為 Markdown 語法。
│     表格渲染可透過 ITableSerializer 替換。
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. 分塊 (AI.Rag)
│     Markdown 字串 → 可搜尋的分塊列表
│     MarkdownTextSplitter 按標題分割為章節，
│     然後級聯分割：段落 → 行 → 詞邊界。
└─────────────────────────────────────────────────────────────┘
```

**階段 1（解析）** — 每個文件載入器（`HwpDocumentLoader`、`PdfDocumentLoader` 等）讀取原始檔案並將其轉換為 `DoclingDocument`，即一個包含文字、標題、表格和程式碼區塊的樹形結構化模型。

**階段 2（序列化）** — 呼叫 `DoclingDocument.ToMarkdown()` 時，內部的 `MarkdownSerializer` 遍歷樹結構並產生 Markdown 字串。表格渲染可透過 `ITableSerializer` 替換。HWP 文件預設使用 `SemanticTableSerializer`，以粗體群組標籤渲染表單樣式的表格。

**階段 3（分塊）** — RAG 管線的 `MarkdownTextSplitter` 接收 Markdown 字串並將其分割為適合搜尋的分塊。它按標題（`#`、`##` 等）組織章節，並自動在每個分塊中包含麵包屑導航（父標題路徑）。

由於這三個階段是解耦的，新增文件載入器或更改表格渲染策略不會影響其他階段。

## 文件載入器與文字分割器的搭配

`MarkdownTextSplitter` 是 Office/HWP 文件最有效的選擇：

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter` 會按行拆分表格，並自動在每個分塊中包含表頭，確保搜尋結果中的表格資料保持完整。詳細資訊請參見[文字分割器](text-splitters.md)。
