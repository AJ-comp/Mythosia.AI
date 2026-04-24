# 텍스트 분할기

텍스트 분할기는 임베딩 전에 문서를 청크로 나눕니다. 청크 크기와 오버랩은 검색 품질에 크게 영향을 미칩니다.

## 사용 가능한 분할기

### CharacterTextSplitter

문자 수로 분할합니다. 단순하고 빠르지만 문장 중간에서 잘릴 수 있습니다:

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (권장 기본값)

다음 순서로 의미 있는 경계에서 분할을 시도합니다: 단락 → 문장 → 단어 → 문자. 더 일관된 청크를 생성합니다:

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

문자 수 대신 토큰 수로 분할합니다. LLM 컨텍스트 윈도우 예산에 더 정확합니다:

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

임베딩 모델에 엄격한 토큰 제한이 있을 때 사용하세요.

### MarkdownTextSplitter

마크다운의 구조를 이해하고 보존하는 분할기입니다. 헤딩 계층(H1–H6), 코드 펜스, 테이블 등의 구조를 인식하여 의미 있는 단위로 분할합니다:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

문서 파일, README, 그리고 Office/HWP 등 구조화된 문서 로더의 출력에 특히 적합합니다.

> [!TIP]
> Word, Excel, PowerPoint, HWP 등의 문서 로더는 내부적으로 문서를 마크다운으로 변환합니다. 이러한 문서에는 `MarkdownTextSplitter`를 사용하면 테이블과 코드 블록의 구조가 청킹 과정에서도 온전히 보존됩니다.

#### 테이블 분할 품질

`MarkdownTextSplitter`는 마크다운 테이블을 **행 단위**로 분할합니다. 행 중간에서 잘리는 일은 절대 없으며, 분할된 각 청크에는 **헤더 행과 구분선이 자동으로 포함**됩니다:

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

각 청크가 독립적으로 유효한 테이블이 되어, 임베딩과 검색 품질이 보장됩니다.

#### 코드 블록 보호

코드 펜스(`` ``` ``)로 감싸진 블록은 **원자적(atomic) 단위**로 취급됩니다. 코드 블록은 청크 크기를 초과하더라도 절대 중간에서 분할되지 않으므로 코드의 의미가 훼손되지 않습니다.

#### 헤딩 브레드크럼

각 청크에는 해당 콘텐츠가 속한 헤딩 경로가 자동으로 앞에 붙습니다. 이를 통해 벡터 검색 시 청크의 문맥이 훨씬 풍부해집니다:

```
# 제품 매뉴얼
## 설치 가이드
### Windows

(이 섹션의 실제 콘텐츠)
```

이 기능은 `IncludeHeadingBreadcrumb` 속성(기본값: `true`)으로 제어합니다.

## 파라미터 선택

| 파라미터 | 효과 |
|----------|------|
| `chunkSize` (크게) | 청크당 더 많은 컨텍스트, 더 적은 청크, 더 저렴한 임베딩 |
| `chunkSize` (작게) | 더 높은 정밀도 검색, 더 많은 청크, 더 많은 임베딩 |
| `chunkOverlap` | 청크 경계에서 정보 손실 방지 |

일반적인 시작점: `chunkSize: 500, chunkOverlap: 50`.

## 청크 크기와 토큰 수 (다국어 참고)

`chunkSize`는 **문자 수** 기준이지만, 임베딩 모델의 제한은 **토큰 수** 기준입니다. 언어에 따라 같은 문자 수라도 토큰 수가 크게 달라질 수 있습니다:

| 언어 | 1,000자 ≈ 토큰 수 | 권장 chunkSize |
|------|-------------------|----------------|
| 영어 | ~250 토큰 | 500–2,000 |
| 한국어 / 일본어 / 중국어 | ~800–1,500 토큰 | 300–1,000 |

> [!WARNING]
> 한국어, 일본어, 중국어 등 CJK 텍스트는 문자당 토큰 비율이 영어보다 훨씬 높습니다. 임베딩 모델의 토큰 제한(예: 2,048 토큰)을 초과하면 오류가 발생합니다. CJK 문서를 다룰 때는 `chunkSize`를 넉넉히 줄여 설정하세요.

예를 들어, 토큰 제한이 2,048인 임베딩 모델을 사용한다면:

```csharp
// 영어 문서: 2000자 ≈ 500 토큰 → 여유 있음
.WithTextSplitter(new MarkdownTextSplitter(2000, 200))

// 한국어 문서: 1000자 ≈ 1000 토큰 → 안전 범위
.WithTextSplitter(new MarkdownTextSplitter(1000, 200))
```

## 문서별 분할기

`RagBuilder`에서 문서마다 다른 분할기를 적용할 수 있습니다:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // 나머지 문서의 기본값
)
```

## 커스텀 분할기

커스텀하게 동작하는 분할 모듈을 작성해서 연동하고 싶다면 `ITextSplitter`를 구현하세요:

```csharp
public class SentenceSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Content = s,
            Index = i,
            DocumentId = document.Id
        }).ToList();
    }
}

// 등록:
.WithTextSplitter(new SentenceSplitter())
```

---

## 더 깊이 알고 싶다면

RAG에서 가장 안전한 청킹은 마크다운으로 변환하기 **전에**, `DoclingDocument` 트리에서 직접 자르는 방식입니다 — 표가 중간에서 잘리거나 헤딩이 본문과 분리되는 사고를 막을 수 있습니다.

- [원하는 대로 출력 바꾸기 — 청킹 레시피](document-architecture-customization.md#레시피-4-rag를-위한-청킹--트리에서-직접-자르기) — 슬라이드/시트 단위, 헤딩 컨텍스트 보존 청킹 패턴
- [DoclingDocument 데이터 모델](document-architecture-data-model.md) — 트리 기반 청킹을 직접 구현할 때 필요한 트리 구조 설명
