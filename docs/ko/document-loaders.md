# 문서 로더

문서 로더는 파일을 구조화된 `DoclingDocument` 객체로 파싱하며, 이를 RAG 파이프라인에 전달할 수 있습니다.

## 설치

Office 및 PDF 로더는 `Mythosia.AI.Rag`에 포함됩니다. 독립적으로 사용하려면:

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## 지원 형식

| 로더 | 형식 | 패키지 |
|------|------|--------|
| `PdfDocumentLoader` | `.pdf` | `Mythosia.Documents.Pdf` |
| `WordDocumentLoader` | `.docx` | `Mythosia.Documents.Office` |
| `ExcelDocumentLoader` | `.xlsx` | `Mythosia.Documents.Office` |
| `PowerPointDocumentLoader` | `.pptx` | `Mythosia.Documents.Office` |
| `HwpDocumentLoader` | `.hwp` | `Mythosia.Documents.Hwp` |
| `PlainTextDocumentLoader` | `.txt`, `.md` 등 | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions
{
    Password = "secret",           // 암호화된 PDF용
    IncludeMetadata = true,        // 제목, 작성자 추출
    IncludePageNumbers = true,     // 페이지 번호 마커 추가
    NormalizeWhitespace = true     // 여분의 공백 제거
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
    IncludeSheetNames = true,  // 각 섹션에 시트 이름 앞에 추가
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // 각 섹션에 슬라이드 번호 앞에 추가
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## HWP (.hwp)

한글(HWP) 파일을 파싱합니다. 별도 패키지로 제공됩니다:

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

HWP 로더는 문서의 텍스트, 테이블, 헤딩 구조를 `DoclingDocument`로 변환하며, 최종적으로 마크다운 형식으로 출력됩니다. 테이블은 마크다운 테이블(`| ... |`) 형식으로 변환되므로, `MarkdownTextSplitter`와 함께 사용하면 테이블 구조가 청킹 과정에서도 온전히 보존됩니다.

## RAG에서 사용하기

로더는 `RagBuilder`에서 `.AddDocument()`를 사용할 때 자동으로 통합됩니다. 수동으로 로드하고 결과를 추가하려면:

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("report.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("report.pdf")  // 형식 자동 감지
        .AddDocument("notes.docx")
    );
```

## DoclingDocument 구조

각 로드된 파일은 계층적 요소 트리를 가진 `DoclingDocument`가 됩니다:

```csharp
var docs = await loader.LoadAsync("report.pdf");
var doc = docs[0];

Console.WriteLine(doc.Title);   // 문서 제목
Console.WriteLine(doc.Source);  // 파일 경로

foreach (var item in doc.Document)
{
    switch (item)
    {
        case SectionHeaderItem h: Console.WriteLine($"## {h.Text}"); break;
        case TextItem t:          Console.WriteLine(t.Text); break;
        case TableItem table:     /* 테이블 셀 처리 */ break;
        case CodeItem code:       Console.WriteLine(code.Text); break;
    }
}
```

**요소 타입:** `TextItem`, `SectionHeaderItem`, `TitleItem`, `ListItem`, `TableItem`, `CodeItem`, `FormulaItem`, `PictureItem`, `GroupItem`, `RefItem`

## 문서 로더와 텍스트 분할기 연계

Word, Excel, PowerPoint, HWP 등의 문서 로더는 내부적으로 `DoclingDocument`를 거쳐 **마크다운 형식**으로 변환됩니다. 이 과정에서 테이블은 마크다운 테이블 문법(`| 헤더 |` + `|---|` + `| 데이터 |`)으로 변환되고, 헤딩과 코드 블록도 마크다운 구문으로 출력됩니다.

이 때문에 Office/HWP 문서에는 `MarkdownTextSplitter`를 사용하는 것이 가장 효과적입니다:

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter`는 테이블을 행 단위로 분할하고 각 청크에 헤더를 자동 포함하므로, 검색 결과에서도 테이블 데이터가 온전한 형태로 반환됩니다. 자세한 내용은 [텍스트 분할기](text-splitters.md) 문서를 참고하세요.
