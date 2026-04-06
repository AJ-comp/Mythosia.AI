# 文档加载器

文档加载器将文件解析为结构化的 `DoclingDocument` 对象，然后可以传递给 RAG 管道。

## 安装

Office 和 PDF 加载器包含在 `Mythosia.AI.Rag` 中。如需单独使用：

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## 支持的格式

| 加载器 | 格式 | 包名 |
|--------|------|------|
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
    Password = "secret",           // 加密 PDF 的密码
    IncludeMetadata = true,        // 提取标题、作者
    IncludePageNumbers = true,     // 添加页码标记
    NormalizeWhitespace = true     // 合并多余空白
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
    IncludeSheetNames = true,  // 在每个部分前加工作表名
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // 在每个部分前加幻灯片编号
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## HWP (.hwp)

解析韩国 Hangul 文字处理器（HWP）文件。作为单独的包提供：

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

HWP 加载器将文本、表格和标题结构转换为 `DoclingDocument`，最终以 Markdown 格式输出。表格以 Markdown 表格（`| ... |`）呈现，因此配合 `MarkdownTextSplitter` 使用时，表格结构在分块过程中会被完整保留。

## 在 RAG 中使用

在 `RagBuilder` 中使用 `.AddDocument()` 时，加载器会自动集成。手动加载并添加结果：

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("report.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("report.pdf")  // 自动检测格式
        .AddDocument("notes.docx")
    );
```

## DoclingDocument 结构

每个加载的文件会转换为一个 `DoclingDocument`，包含层级化的元素树：

```csharp
var docs = await loader.LoadAsync("report.pdf");
var doc = docs[0];

Console.WriteLine(doc.Title);   // 文档标题
Console.WriteLine(doc.Source);  // 文件路径

foreach (var item in doc.Document)
{
    switch (item)
    {
        case SectionHeaderItem h: Console.WriteLine($"## {h.Text}"); break;
        case TextItem t:          Console.WriteLine(t.Text); break;
        case TableItem table:     /* 处理表格单元格 */ break;
        case CodeItem code:       Console.WriteLine(code.Text); break;
    }
}
```

**元素类型：** `TextItem`、`SectionHeaderItem`、`TitleItem`、`ListItem`、`TableItem`、`CodeItem`、`FormulaItem`、`PictureItem`、`GroupItem`、`RefItem`

## 处理管线概览

文档在成为 RAG 可搜索的分块之前需要经过三个阶段。每个阶段由不同的包负责。

```text
┌─────────────────────────────────────────────────────────────┐
│  1. 解析 (Documents.Hwp / Documents.Office / Documents.Pdf)
│     .hwp, .pdf, .docx 等 → DoclingDocument（结构化模型）
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. 序列化 (Documents.Abstractions)
│     DoclingDocument → Markdown 字符串
│     MarkdownSerializer 将标题、表格、代码块
│     转换为 Markdown 语法。
│     表格渲染可通过 ITableSerializer 替换。
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. 分块 (AI.Rag)
│     Markdown 字符串 → 可搜索的分块列表
│     MarkdownTextSplitter 按标题分割为章节，
│     然后级联分割：段落 → 行 → 词边界。
└─────────────────────────────────────────────────────────────┘
```

**阶段 1（解析）** — 每个文档加载器（`HwpDocumentLoader`、`PdfDocumentLoader` 等）读取原始文件并将其转换为 `DoclingDocument`，即一个包含文本、标题、表格和代码块的树形结构化模型。

**阶段 2（序列化）** — 调用 `DoclingDocument.ToMarkdown()` 时，内部的 `MarkdownSerializer` 遍历树结构并生成 Markdown 字符串。表格渲染可通过 `ITableSerializer` 替换。HWP 文档默认使用 `SemanticTableSerializer`，以粗体组标签渲染表单样式的表格。

**阶段 3（分块）** — RAG 管线的 `MarkdownTextSplitter` 接收 Markdown 字符串并将其分割为适合搜索的分块。它按标题（`#`、`##` 等）组织章节，并自动在每个分块中包含面包屑导航（父标题路径）。

由于这三个阶段是解耦的，添加新的文档加载器或更改表格渲染策略不会影响其他阶段。

## 文档加载器与文本分割器的配合

`MarkdownTextSplitter` 是 Office/HWP 文档最有效的选择：

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter` 按行拆分表格，并自动在每个分块中包含表头，从而确保搜索结果中的表格数据保持完整。详细信息请参见[文本分割器](text-splitters.md)。
