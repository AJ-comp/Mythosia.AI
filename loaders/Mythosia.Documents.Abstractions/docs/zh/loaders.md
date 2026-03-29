# Mythosia.AI Loaders 指南

## 概述

Loaders 会将文件源转换为 `RagDocument`，供 RAG 管道使用。通常安装相应加载器包并使用
`RagBuilder` 导入文档即可。仅在需要自定义读取逻辑时实现 `IDocumentLoader`。

## 包

- **Mythosia.AI.Loaders.Abstractions** — `IDocumentLoader`, `RagDocument`, `ParsedDocument`
- **Mythosia.AI.Loaders.Office** — Word/Excel/PowerPoint (.docx/.xlsx/.pptx)，基于 OpenXml
- **Mythosia.AI.Loaders.Pdf** — PDF 加载器（PdfPig 解析器）

## 安装

```bash
dotnet add package Mythosia.AI.Loaders.Office
dotnet add package Mythosia.AI.Loaders.Pdf
```

## 快速开始

```csharp
using Mythosia.AI.Rag;

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("docs/manual.pdf")
        .AddDocument("docs/plan.docx")
        .AddDocuments("./knowledge-base")
    );
```

`AddDocument(...)` 和 `AddDocuments(...)` 会按扩展名自动选择加载器：

- `.docx` -> `WordDocumentLoader`
- `.xlsx` -> `ExcelDocumentLoader`
- `.pptx` -> `PowerPointDocumentLoader`
- `.pdf` -> `PdfDocumentLoader`
- 其他 -> `PlainTextDocumentLoader`

## 按扩展名路由

当你希望不同文件类型使用不同加载器或分割器时使用。

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Pdf;
using Mythosia.AI.Loaders.Office.Word;
using Mythosia.AI.Rag.Splitters;

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocuments("./docs", src => src
            .WithExtension(".pdf")
            .WithLoader(new PdfDocumentLoader())
            .WithTextSplitter(new CharacterTextSplitter(800, 80))
        )
        .AddDocuments("./docs", src => src
            .WithExtension(".docx")
            .WithLoader(new WordDocumentLoader())
            .WithTextSplitter(new TokenTextSplitter(600, 60))
        )
        .AddDocuments("./docs", src => src
            .WithTextSplitter(new CharacterTextSplitter(400, 40))
        )
    );
```

## Office 加载器

支持格式：**Word (.docx)**、**Excel (.xlsx)**、**PowerPoint (.pptx)**。

```csharp
using Mythosia.AI.Loaders.Office;
using Mythosia.AI.Loaders.Office.Word;

var options = new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
};

var loader = new WordDocumentLoader(options: options);

.WithRag(rag => rag.AddDocuments(loader, "docs/report.docx"));
```

`OfficeParserOptions` 适用于所有 OpenXml 解析器：

- `IncludeMetadata`
- `NormalizeWhitespace`
- `IncludeSheetNames` (Excel)
- `IncludeSlideNumbers` (PowerPoint)

## PDF 加载器

```csharp
using Mythosia.AI.Loaders.Pdf;

var options = new PdfParserOptions
{
    IncludeMetadata = true,
    IncludePageNumbers = false,
    NormalizeWhitespace = true
};

var loader = new PdfDocumentLoader(options: options);

.WithRag(rag => rag.AddDocuments(loader, "docs/manual.pdf"));
```

### 自定义解析器

如需替换默认解析器，请实现 `IDocumentParser`，确保 `CanParse` 能处理 PDF，然后传入:

```csharp
using Mythosia.AI.Loaders.Pdf;

var loader = new PdfDocumentLoader(parser: new MyPdfParser());
```

## 自定义加载器

```csharp
using Mythosia.AI.Loaders;

public class MarkdownDocumentLoader : IDocumentLoader
{
    public Task<IReadOnlyList<RagDocument>> LoadAsync(string source, CancellationToken ct = default)
    {
        var text = File.ReadAllText(source);
        return Task.FromResult<IReadOnlyList<RagDocument>>(new[]
        {
            new RagDocument
            {
                Id = Path.GetFileName(source),
                Content = text,
                Source = source,
                Metadata = { ["type"] = "markdown" }
            }
        });
    }
}
```

通过构建器注册：

```csharp
.WithRag(rag => rag.AddDocuments(new MarkdownDocumentLoader(), "docs/intro.md"))
```

## 元数据参考

常见元数据键：

- Office: `type=office`, `office_type`, `filename`, `extension`, `parser`
- PDF: `type=pdf`, `filename`, `extension`, `parser`

解析器提供的额外元数据会被保留。
