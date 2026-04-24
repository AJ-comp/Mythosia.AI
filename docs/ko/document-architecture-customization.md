# 원하는 대로 출력 바꾸기

[개념 페이지](document-architecture-concept.md)와 [데이터 모델 페이지](document-architecture-data-model.md)를 읽었다면,
이제 실제로 동작을 조정하는 방법을 살펴봅니다. 각 절은 독립적인 "레시피" 형태로 정리되어 있으니 필요한 것만 골라 보셔도 됩니다.

---

## 레시피 1: 표 렌더링 방식 바꾸기

표는 같은 데이터라도 두 가지 방식으로 렌더링할 수 있습니다.

```csharp
var doc = (await new ExcelDocumentLoader().LoadAsync("data.xlsx"))[0];

// (A) 기본: 표준 마크다운 파이프 표
string md1 = doc.ToMarkdown();
// | 헤더1 | 헤더2 | 헤더3 |
// |---|---|---|
// | a | b | c |

// (B) 의미 단위로 풀어쓴 형태
doc.TableSerializer = new SemanticTableSerializer();
string md2 = doc.ToMarkdown();
// 헤더1: a, 헤더2: b, 헤더3: c
```

**언제 어느 걸 쓰나?**

- **GridTableSerializer (기본)**: 사람이 읽기 좋고, GitHub/일반 마크다운 뷰어에서 잘 보입니다.
- **SemanticTableSerializer**: RAG에서 "헤더-값 쌍"을 검색하기 좋게 표를 풀어줍니다. LLM이 표를 이해할 때도 더 안정적인 경우가 많습니다.

---

## 레시피 2: 마크다운 이스케이프 끄기

기본적으로 본문 텍스트의 마크다운 특수 문자(`*`, `_`, `[`, `|` 등)는 자동으로 이스케이프됩니다.
원본 텍스트가 이미 마크다운 문법을 사용하고 있다면 이스케이프를 끄세요.

```csharp
var serializer = new MarkdownSerializer { EscapeText = false };
string md = serializer.Serialize(doc);
```

**언제 끄나?** 입력 파일이 코드 스니펫, 프로그래밍 문서, 또는 의도적으로 마크다운 문법을 포함한 텍스트일 때.

**언제 켜두나? (기본)** 사용자 작성 문서 — Word/HWP의 일반 본문에 우연히 들어간 `*` 같은 문자가 마크다운으로 잘못 해석되는 것을 방지합니다.

---

## 레시피 3: 구조화 파이프라인 우회하기 (RawContent)

이미 마크다운인 파일(.md)이나 텍스트 파일(.txt)을 구조화 없이 그대로 마크다운으로 통과시키고 싶을 때 사용합니다.

```csharp
var doc = new DoclingDocument
{
    RawContent = File.ReadAllText("README.md"),
};

string md = doc.ToMarkdown();  // RawContent를 그대로 반환
```

`RawContent`가 설정되면 `ToMarkdown()`은 트리 직렬화 없이 그 문자열을 즉시 반환합니다.

**언제 쓰나?** `PlainTextDocumentLoader`처럼 "내용을 있는 그대로 보존해야 하는" 경우. 일반 Office 로더는 절대 이 값을 설정하지 않습니다.

---

## 레시피 4: RAG를 위한 청킹 — 트리에서 직접 자르기

긴 문서를 LLM에 전달하려면 청크로 나눠야 합니다. **마크다운 문자열을 직접 자르지 마세요** — 표나 섹션 컨텍스트가 깨집니다. 대신 `DoclingDocument` 트리에서 자르세요.

### 슬라이드 단위 청킹 (PowerPoint)

```csharp
var doc = (await new PowerPointDocumentLoader().LoadAsync("deck.pptx"))[0];

var serializer = new MarkdownSerializer();
foreach (var slide in doc.Groups.Where(g => g.Label == GroupLabel.Slide))
{
    // 슬라이드 하나만 담은 임시 도큐먼트를 만들어 마크다운화
    var slideDoc = new DoclingDocument();
    slideDoc.Body.Children.Add(slide.GetRef());
    // ... (실제로는 헬퍼 메서드로 추출하는 게 깔끔함)

    var chunkMarkdown = serializer.Serialize(slideDoc);
    // 청크를 RAG 인덱스에 저장
}
```

`GroupLabel.Slide`로 표시된 컨테이너만 골라내면 슬라이드 단위 청크가 됩니다. Excel은 `GroupLabel.Sheet`로 같은 패턴이 적용됩니다.

### 헤더 컨텍스트 보존 청킹 (Word, HWP)

긴 본문을 자를 때, 청크가 어느 섹션에 속해있는지 컨텍스트를 잃지 않도록 가장 가까운 부모 헤더(들)을 청크 앞에 붙입니다.

```csharp
foreach (var heading in doc.Texts.OfType<SectionHeaderItem>())
{
    // heading의 자식들만 모아 청크 만들기
    var children = heading.Children.Select(r => r.Resolve(doc));
    var bodyText = string.Join("\n", children.OfType<TextItem>().Select(t => t.Text));

    var chunk = $"## {heading.Text}\n\n{bodyText}";
    // 인덱스에 저장
}
```

[Word 파서의 헤더 스택 처리](document-architecture-data-model.md#refitem이란-필요할-때만-읽기) 덕분에 H1/H2 계층이 정확히 트리에 반영되어 있어서, 위 코드가 정상 동작합니다.

### 표는 항상 통째로

표는 일반 텍스트와 다르게 **절대 청크 사이로 잘리면 안 됩니다** — 헤더와 데이터가 분리되면 LLM이 의미를 잃습니다.

```csharp
foreach (var table in doc.Tables)
{
    var sb = new StringBuilder();
    new GridTableSerializer().Render(table, sb);
    var tableMarkdown = sb.ToString();
    // 표 하나가 청크 하나
}
```

---

## 레시피 5: 커스텀 표 시리얼라이저 만들기

`ITableSerializer`만 구현하면 됩니다.

```csharp
public class CsvTableSerializer : ITableSerializer
{
    public void Render(TableItem table, StringBuilder sb)
    {
        var data = table.Data;
        var grid = data.BuildGrid();
        for (int r = 0; r < data.NumRows; r++)
        {
            for (int c = 0; c < data.NumCols; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append(grid[r, c]?.Text ?? "");
            }
            sb.AppendLine();
        }
        sb.AppendLine();
    }
}

// 사용
doc.TableSerializer = new CsvTableSerializer();
```

`TableData.BuildGrid()`는 병합 셀까지 펼친 2D 배열을 돌려주므로 위치 기반 접근이 쉽습니다.

---

## 레시피 6: 커스텀 파서로 새 파일 형식 지원

`IDocumentParser`를 구현하면 됩니다.

```csharp
public class OdtDocumentParser : IDocumentParser
{
    public bool CanParse(string source) =>
        Path.GetExtension(source).Equals(".odt", StringComparison.OrdinalIgnoreCase);

    public async Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default)
    {
        var doc = new DoclingDocument { Name = Path.GetFileNameWithoutExtension(source) };

        // (1) ODT 파일을 열고 (예: SharpZipLib + XML 파싱)
        // (2) 각 단락/표/헤딩을 doc.AddParagraph(), doc.AddHeading(), doc.AddTable() 등으로 추가
        // (3) doc 반환

        return doc;
    }
}

// 사용
var loader = new WordDocumentLoader(new OdtDocumentParser());
// (또는 새 ILoader 클래스를 만들어도 됨)
```

**파서 작성 시 주의 사항** (CLAUDE.md의 "문제 원인 분석 규칙"과도 연결됨):

- 파서는 **원본 포맷의 구조를 있는 그대로 기록**합니다. 마크다운 표현 방식을 미리 결정하지 마세요.
- 예: HWP에서 "어느 셀이 헤더인가"는 작성자가 명시한 `TitleCell` 플래그로 판단합니다 — "첫 행은 무조건 헤더" 같은 휴리스틱을 파서 단계에서 넣으면, 행 헤더 구조의 표가 망가집니다.
- 표현 결정은 시리얼라이저(혹은 그 fallback 로직)의 책임입니다.

---

## 다음에 읽을 것

- **[데이터 모델 페이지](document-architecture-data-model.md)** — `TableData.BuildGrid()`, `GroupLabel`, `RefItem` 등 위에서 사용한 타입들의 자세한 설명.
- **[개념 페이지](document-architecture-concept.md)** — 두 단계 파이프라인의 큰 그림.
- **[문서 로더](document-loaders.md)** — 각 로더의 옵션과 사용 예제.
