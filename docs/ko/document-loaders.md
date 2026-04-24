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

## 처리 파이프라인 개요

문서가 RAG 검색 가능한 청크로 변환되기까지 세 단계를 거칩니다. 각 단계는 서로 다른 패키지가 담당합니다.

```text
┌─────────────────────────────────────────────────────────────┐
│  1. 파싱 (Documents.Hwp / Documents.Office / Documents.Pdf) │
│     .hwp, .pdf, .docx 등 → DoclingDocument (구조화 모델)     │
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. 직렬화 (Documents.Abstractions)                          │
│     DoclingDocument → Markdown 문자열                         │
│     MarkdownSerializer가 헤딩, 테이블, 코드 블록 등을          │
│     마크다운 문법으로 변환합니다.                                │
│     테이블 렌더링은 ITableSerializer로 교체할 수 있습니다.      │
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. 청킹 (AI.Rag)                                           │
│     Markdown 문자열 → 검색 가능한 청크 리스트                   │
│     MarkdownTextSplitter가 헤더 기반으로 섹션을 나누고,         │
│     큰 섹션은 문단 → 줄 → 단어 경계 순으로 분할합니다.          │
└─────────────────────────────────────────────────────────────┘
```

**1단계 (파싱)** — 각 문서 로더(`HwpDocumentLoader`, `PdfDocumentLoader` 등)가 원본 파일을 읽어 `DoclingDocument`라는 구조화된 모델로 변환합니다. 여기에는 텍스트, 헤딩, 테이블, 코드 블록 등의 요소가 트리 형태로 저장됩니다.

**2단계 (직렬화)** — `DoclingDocument.ToMarkdown()`이 호출되면 내부적으로 `MarkdownSerializer`가 트리를 순회하며 마크다운 문자열을 생성합니다. 테이블 렌더링 방식은 `ITableSerializer`를 통해 교체할 수 있으며, HWP 문서는 기본적으로 `SemanticTableSerializer`를 사용하여 양식(Form) 스타일 테이블을 볼드 레이블 형태로 렌더링합니다.

**3단계 (청킹)** — RAG 파이프라인의 `MarkdownTextSplitter`가 마크다운 문자열을 받아 검색에 적합한 크기의 청크로 분할합니다. 헤더(`#`, `##` 등) 기반으로 섹션을 구성하고, 각 청크에 상위 헤더 경로(breadcrumb)를 자동 포함합니다.

이 세 단계가 분리되어 있으므로, 문서 로더를 추가하거나 테이블 렌더링 방식을 변경해도 다른 단계에 영향을 주지 않습니다.

## 문서 로더와 텍스트 분할기 연계

Office/HWP 문서에는 `MarkdownTextSplitter`를 사용하는 것이 가장 효과적입니다:

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter`는 테이블을 행 단위로 분할하고 각 청크에 헤더를 자동 포함하므로, 검색 결과에서도 테이블 데이터가 온전한 형태로 반환됩니다. 자세한 내용은 [텍스트 분할기](text-splitters.md) 문서를 참고하세요.

---

## 더 깊이 알고 싶다면

아래 페이지들은 파싱 내부 동작을 설명합니다 — 표 렌더링을 커스터마이징하거나, 슬라이드/시트 단위로 청크를 나누거나, 새 파일 형식을 지원하고 싶을 때 유용합니다. `LoadAsync()` + `ToMarkdown()`만 쓸 거라면 읽지 않으셔도 됩니다.

- [문서 파싱 — 기본 개념](document-architecture-concept.md) — 왜 두 단계로 나뉘어 있는지
- [DoclingDocument 안에 무엇이 들어있을까?](document-architecture-data-model.md) — 각 로더가 만들어내는 트리 구조
- [출력 커스터마이징](document-architecture-customization.md) — 표 시리얼라이저 교체, 청킹 패턴, 커스텀 파서
