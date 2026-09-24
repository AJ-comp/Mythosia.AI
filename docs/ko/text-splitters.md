# 텍스트 분할기

검색 결과에는 질문에 답할 문맥이 필요하지만, 문서 전체를 한 덩어리로 임베딩하면 필요한 부분을 고르기 어렵습니다. 청킹은 이 크기와 문맥의 균형을 맞춥니다. 아래 분할기는 AI 모델 없이 로컬 규칙으로 동작합니다. 문서 구조에 맞게 선택하고 실제 질문으로 검색 결과를 평가하세요.

## 사용 가능한 분할기

### CharacterTextSplitter

일반 텍스트를 간단한 크기 기준으로 나눌 때 사용합니다. 가능한 경우 지정한 구분자를 우선하지만 문장 중간에서 나눌 수도 있습니다. `RagBuilder`의 기본값은 `CharacterTextSplitter(300, 30)`이며, `.md` 확장자만으로 Markdown 분할기가 자동 선택되지는 않습니다.

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (권장 기본값)

단락과 단어를 가능한 한 함께 유지하려는 일반 본문에 사용합니다. 기본 구분자 순서는 빈 줄 → 줄바꿈 → `. ` → 공백 → 개별 문자입니다. 모델이 의미를 판단하는 방식은 아니며, 마침표 뒤 공백도 문장 경계를 근사하는 규칙입니다.

`Separators`의 중복 항목은 처음 등장한 순서대로 한 번씩만 적용합니다. 같은 구분자를 여러 번 넣어도 분할 단계를 반복하지 않습니다. 긴 구분자 목록도 재귀 호출을 깊게 중첩하지 않고 처리합니다.

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

공백으로 나눈 단어 수를 대략적인 단위로 삼을 때 사용합니다. 이름과 달리 `MaxTokensPerChunk`와 `TokenOverlap`은 `TokenSeparators`(기본값: 공백·탭·줄바꿈)로 나눈 단위를 세며, 임베딩 모델의 토큰을 세지 않습니다. 결과에서는 구분자를 공백으로 정규화합니다. 공백 없는 긴 텍스트가 하나의 단위로 남을 수 있으므로 모델의 토큰 상한을 보장하지 않습니다.

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

제목 문맥, 표의 행, 코드 블록을 함께 유지해야 하는 Markdown 설명서나 Office/HWP 로더의 Markdown 출력에 사용합니다. ATX 제목(`#`–`######`), 코드 펜스, 표를 인식하는 규칙 기반 분할기이며 전체 Markdown 문법을 처리하는 구문 트리 파서는 아닙니다. 생성자는 `chunkSize`만 받으며 Markdown에는 overlap 인자나 옵션이 없습니다.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500))
```

#### 테이블 분할 품질

인식한 GFM 표는 행 사이에서 나누고 각 표 청크에 헤더와 구분 행을 반복합니다. 바깥쪽 파이프가 없는 `Name | Value` 형식도 지원합니다. 열 이름을 유지하는 기능이며, 검색 품질 자체는 문서·임베딩·질문에 따라 달라집니다.

표의 굵은 셀은 해당 행의 내용입니다. 예를 들어 A회사 행의 `**환불 불가**`가 B회사 행의 조건으로 붙으면 안 됩니다. 표 셀과 코드 블록의 내용은 반복할 본문 라벨로 취급하지 않습니다. `**라벨**` 한 줄은 빈 줄이나 구조 경계 뒤, 즉 문단 또는 텍스트 블록의 시작에서만 라벨로 인식합니다. 기존 문단 중간에서 줄바꿈된 굵은 문구는 새 문단이나 반복 라벨로 만들지 않습니다. 인식한 라벨은 그 텍스트 블록을 나눈 조각에 반복할 수 있으며, 표·코드 펜스·제목·다음에 인식한 라벨에서 적용 범위가 끝납니다.

```
원본 테이블:
| 이름   | 부서   | 연봉      |
|--------|--------|----------|
| 김철수 | 개발팀 | 5,000만원 |
| 이영희 | 기획팀 | 4,800만원 |
| 박민수 | 디자인 | 4,500만원 |

→ 청크 1:
| 이름   | 부서   | 연봉      |
|--------|--------|----------|
| 김철수 | 개발팀 | 5,000만원 |
| 이영희 | 기획팀 | 4,800만원 |

→ 청크 2:
| 이름   | 부서   | 연봉      |
|--------|--------|----------|
| 박민수 | 디자인 | 4,500만원 |
```

#### 코드 블록 보호

백틱과 물결표로 감싼 코드 블록은 통째로 유지합니다. 닫는 펜스는 여는 펜스와 같은 문자이고 길이가 같거나 길어야 하므로, 블록 안의 더 짧은 펜스는 종료로 처리하지 않습니다. 블록 전체를 유지하기 위해 `ChunkSize`를 넘길 수 있습니다.

여는 펜스의 들여쓰기도 코드와 함께 보존하므로 분할 후 코드의 렌더링된 들여쓰기 의미가 바뀌지 않습니다. 여는 펜스 정보는 블록당 한 번만 분석해, 긴 펜스를 본문의 매 줄마다 다시 읽는 비용을 피합니다.

#### 헤딩 브레드크럼

`IncludeHeadingBreadcrumb`의 기본값은 `true`입니다. 검색된 일부 본문에도 문맥이 남도록 각 청크 앞에 상위 제목 경로를 반복합니다. `false`로 바꾸면 반복만 끄며 원래 제목은 보존합니다. 본문 없이 제목만 있는 섹션도 남깁니다.

`MinSplitHeadingLevel`은 1–6을 받아 섹션을 시작할 제목 수준을 정하며 기본값은 1입니다. 상위 제목이 바뀌면 이전 제목 경로가 새 내용에 섞이지 않도록 기존 하위 섹션을 끝냅니다.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500)
{
    IncludeHeadingBreadcrumb = false
})
```

## 파라미터 선택

`CharacterTextSplitter`, `RecursiveTextSplitter`, `MarkdownTextSplitter`의 크기는 모델 토큰이나 화면에 보이는 글자 수가 아닌 UTF-16 코드 단위(`string.Length`)입니다. 이모지 등의 surrogate pair는 중간에서 자르지 않습니다. 크기가 1일 때 한 쌍에 필요한 2단위는 예외적으로 상한을 넘길 수 있습니다. 결합 문자나 전체 grapheme cluster의 보존까지 보장하지는 않습니다.

크기는 양수, overlap은 0 이상이어야 하며 잘못된 설정은 처리 전에 `ArgumentOutOfRangeException`으로 거부합니다. 변경 가능한 속성도 분할 시 다시 검사합니다. overlap이 크기 이상이면 호환 동작으로 overlap을 끕니다. Character/Recursive의 overlap은 구분자·Unicode 경계와 다음 청크의 여유에 맞춰 조정되는 목표값이며, `0`은 겹침 없음을 뜻합니다. 마지막 겹침 부분만 다시 내보내는 꼬리 청크는 만들지 않습니다.

Markdown의 `ChunkSize`는 **반복되는 상위 제목 경로를 제외한 본문 예산**입니다. 코드 블록 전체 또는 표 헤더와 완전한 한 행은 이 예산을 넘길 수 있습니다. 일반 본문은 위 surrogate pair 예외를 제외하고 설정 크기를 지킵니다.

제목과 표 헤더 반복 때문에 작은 원문이 과도한 임베딩 입력으로 커지지 않도록, Markdown에는 문서 전체 출력 예산이 별도로 있습니다. 상한은 `max(65536, 32 × document.Content.Length)` UTF-16 단위이며, 모든 최종 청크의 길이를 합산하고 반복 제목·표 헤더·라벨도 포함합니다. 과도한 반복 출력을 만들기 전에 예산을 검사해 초과하면 `InvalidOperationException`으로 중단하며, 내용을 잘라내거나 일부 청크만 반환하지 않습니다. `ChunkSize`와 원자적 블록의 크기 예외도 이 전체 상한 안에서 적용됩니다. 기본 RAG 색인 흐름에서는 임베딩이나 저장 레코드 교체 전에 분할이 실패하므로 해당 문서의 기존 색인은 유지됩니다. 이 값은 출력 문자열의 한도이며 모델 토큰이나 프로세스 메모리 한도가 아닙니다. 예산은 `Split` 호출마다 적용하며 원문 길이에 비례해 커지므로, 문서 입력 크기를 일률적으로 제한하는 값은 아닙니다.

예를 들어 일반 본문은 `RecursiveTextSplitter(500, 50)`, Markdown은 `MarkdownTextSplitter(500)`에서 시작해 실제 질문으로 평가하세요. 큰 청크는 주변 내용을 더 많이 담고 overlap은 내용과 임베딩 작업을 늘립니다. 어느 쪽도 검색 품질 향상을 자동으로 보장하지 않습니다.

임베딩·LLM의 토큰 상한을 엄격히 지키려면 반복 제목과 표 헤더까지 포함한 최종 청크를 대상 모델의 토크나이저로 세어야 합니다. 문자·단어 수나 언어별 환산 비율은 안전한 토큰 예산이 아닙니다. 토큰 상한이 필수라면 해당 토크나이저로 `ITextSplitter`를 구현하세요.

이번 정확성 수정은 영향을 받는 문서의 청크 경계를 바꿉니다. 같은 문서 ID로 재색인해 이전 청크를 교체하고, 관련 임베딩 캐시와 평가 기준 결과도 갱신하세요. 저장된 청크가 자동으로 다시 작성되지는 않습니다.

## 문서별 분할기

`RagBuilder`에서 문서마다 다른 분할기를 적용할 수 있습니다:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // 나머지 문서의 기본값
)
```

## 커스텀 분할기

커스텀하게 동작하는 분할 모듈을 작성해서 연동하고 싶다면 `ITextSplitter`를 구현하세요:

색인이 성공했다고 나오는데 앞선 청크가 덮어써지는 일은 없어야 합니다. 각 청크에 컬렉션 안에서 고유한 비어 있지 않은 ID를 지정하고, 회사·접근 권한 필터를 유지하도록 문서 메타데이터를 복사하세요. 아래 예제는 문서 ID와 청크 순번을 결합합니다. 파이프라인은 누락된 ID와 한 문서 안의 중복 ID를 거부하며, 대신 사용할 ID를 자동 생성하지 않습니다. [색인 검증](rag-pipeline.md#indexing-validation)을 참고하세요.

```csharp
public class SentenceSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Id = $"{document.Id}_chunk_{i}",
            Content = s,
            Index = i,
            DocumentId = document.Id,
            Metadata = new Dictionary<string, string>(document.Metadata)
        }).ToList();
    }
}

// 등록:
.WithTextSplitter(new SentenceSplitter())
```

---

## 더 깊이 알고 싶다면

슬라이드·시트·중첩 문서의 경계가 중요하다면 Markdown 변환 **전에** `DoclingDocument` 트리를 순회하는 청킹도 직접 구현할 수 있습니다. 텍스트 표현만으로는 남지 않는 구조를 커스텀 분할기에서 활용할 수 있습니다.

- [출력 커스터마이징 — 청킹 레시피](document-architecture-customization.md#레시피-4-rag-청킹--트리에서-직접-분할하기) — 슬라이드/시트 단위, 헤딩 컨텍스트 보존 청킹 패턴
- [DoclingDocument 안에 무엇이 들어있을까?](document-architecture-data-model.md) — 트리 기반 청킹을 직접 구현할 때 필요한 트리 구조 설명
