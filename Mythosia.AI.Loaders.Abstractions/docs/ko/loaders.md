# Mythosia.AI Loaders 가이드

## 개요

Loaders는 파일 소스를 `RagDocument`로 변환해 RAG 파이프라인에서 사용합니다. 대부분의 경우
로더 패키지를 설치하고 `RagBuilder`로 문서를 추가하면 됩니다. 커스텀 로딩이 필요한 경우에만
`IDocumentLoader`를 구현하세요.

## 패키지

- **Mythosia.AI.Loaders.Abstractions** — `IDocumentLoader`, `RagDocument`, `ParsedDocument`
- **Mythosia.AI.Loaders.Office** — Word/Excel/PowerPoint (.docx/.xlsx/.pptx) OpenXml 기반
- **Mythosia.AI.Loaders.Pdf** — PDF 로더 (PdfPig 파서)

## 설치

```bash
dotnet add package Mythosia.AI.Loaders.Office
dotnet add package Mythosia.AI.Loaders.Pdf
```

## 빠른 시작

```csharp
using Mythosia.AI.Rag;

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("docs/manual.pdf")
        .AddDocument("docs/plan.docx")
        .AddDocuments("./knowledge-base")
    );
```

`AddDocument(...)`와 `AddDocuments(...)`는 확장자 기준으로 자동 로더를 선택합니다:

- `.docx` -> `WordDocumentLoader`
- `.xlsx` -> `ExcelDocumentLoader`
- `.pptx` -> `PowerPointDocumentLoader`
- `.pdf` -> `PdfDocumentLoader`
- 그 외 -> `PlainTextDocumentLoader`

## 확장자별 라우팅

파일 유형마다 로더/스플리터를 다르게 쓰고 싶을 때 사용합니다.

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

## Office 로더

지원 포맷: **Word (.docx)**, **Excel (.xlsx)**, **PowerPoint (.pptx)**.

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

`OfficeParserOptions`는 모든 OpenXml 파서에 적용됩니다:

- `IncludeMetadata`
- `NormalizeWhitespace`
- `IncludeSheetNames` (Excel)
- `IncludeSlideNumbers` (PowerPoint)

## PDF 로더

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

### 커스텀 파서

기본 파서를 바꾸려면 `IDocumentParser`를 구현하고 `CanParse`가 PDF를 처리하도록 맞춘 뒤 직접 전달합니다:

```csharp
using Mythosia.AI.Loaders.Pdf;

var loader = new PdfDocumentLoader(parser: new MyPdfParser());
```

## 커스텀 로더

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

빌더 등록:

```csharp
.WithRag(rag => rag.AddDocuments(new MarkdownDocumentLoader(), "docs/intro.md"))
```

## 메타데이터 참고

자주 사용하는 메타데이터 키:

- Office: `type=office`, `office_type`, `filename`, `extension`, `parser`
- PDF: `type=pdf`, `filename`, `extension`, `parser`

파서가 제공하는 추가 메타데이터는 그대로 유지됩니다.
