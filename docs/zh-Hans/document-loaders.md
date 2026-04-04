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
