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

## RAG에서 사용하기

로더는 `RagBuilder`에서 `.AddDocument()`를 사용할 때 자동으로 통합됩니다. 수동으로 로드하고 결과를 추가하려면:

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("report.pdf");

var service = new ClaudeService(apiKey, http)
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
