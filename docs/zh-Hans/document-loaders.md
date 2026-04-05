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

## 文档加载器与文本分割器的配合

Word、Excel、PowerPoint 和 HWP 的文档加载器在内部通过 `DoclingDocument` 将文件转换为 **Markdown 格式**。表格会变成 Markdown 表格（`| 表头 |` + `|---|` + `| 数据 |`），标题和代码块也以 Markdown 语法输出。

因此，`MarkdownTextSplitter` 是 Office/HWP 文档最有效的选择：

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter` 按行拆分表格，并自动在每个分块中包含表头，从而确保搜索结果中的表格数据保持完整。详细信息请参见[文本分割器](text-splitters.md)。
