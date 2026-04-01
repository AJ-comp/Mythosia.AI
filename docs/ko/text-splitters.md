# 텍스트 분할기

텍스트 분할기는 임베딩 전에 문서를 청크로 나눕니다. 청크 크기와 오버랩은 검색 품질에 크게 영향을 미칩니다.

## 사용 가능한 분할기

### CharacterTextSplitter

문자 수로 분할합니다. 단순하고 빠르지만 문장 중간에서 잘릴 수 있습니다:

```csharp
.UseCharacterSplitter(chunkSize: 500, chunkOverlap: 50)
```

### RecursiveTextSplitter (권장 기본값)

다음 순서로 의미 있는 경계에서 분할을 시도합니다: 단락 → 문장 → 단어 → 문자. 더 일관된 청크를 생성합니다:

```csharp
.UseRecursiveSplitter(chunkSize: 500, chunkOverlap: 50)
```

### TokenTextSplitter

문자 수 대신 토큰 수로 분할합니다. LLM 컨텍스트 윈도우 예산에 더 정확합니다:

```csharp
.UseTokenSplitter(chunkSize: 256, chunkOverlap: 32)
```

임베딩 모델에 엄격한 토큰 제한이 있을 때 사용하세요.

### MarkdownTextSplitter

마크다운 구조를 보존합니다 — 문자 분할로 폴백하기 전에 헤더, 목록, 코드 블록에서 분할합니다:

```csharp
.UseMarkdownSplitter(chunkSize: 500, chunkOverlap: 50)
```

문서 파일, README 파일, 구조화된 마크다운 콘텐츠에 적합합니다.

## 파라미터 선택

| 파라미터 | 효과 |
|----------|------|
| `chunkSize` (크게) | 청크당 더 많은 컨텍스트, 더 적은 청크, 더 저렴한 임베딩 |
| `chunkSize` (작게) | 더 높은 정밀도 검색, 더 많은 청크, 더 많은 임베딩 |
| `chunkOverlap` | 청크 경계에서 정보 손실 방지 |

일반적인 시작점: `chunkSize: 500, chunkOverlap: 50`.

## 문서별 분할기

`RagBuilder`에서 문서마다 다른 분할기를 적용할 수 있습니다:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.md", new MarkdownTextSplitter(chunkSize: 600, chunkOverlap: 60))
    .AddDocument("data.txt",  new RecursiveTextSplitter(chunkSize: 300, chunkOverlap: 30))
    .UseRecursiveSplitter(chunkSize: 500, chunkOverlap: 50)  // 나머지 문서의 기본값
)
```

## 커스텀 분할기

완전히 커스텀한 분할 로직을 위해 `ITextSplitter`를 구현합니다:

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
.UseCustomSplitter(new SentenceSplitter())
```
