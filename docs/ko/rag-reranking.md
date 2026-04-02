# 재순위 & 검색 튜닝

## 왜 재순위가 필요한가?

벡터 검색은 임베딩 유사도 순으로 후보를 반환하지만, 임베딩 유사도는 **근사치**입니다. 0.82점인 청크가 실제로는 0.85점인 것보다 더 관련 있을 수 있습니다 — 임베딩만으로는 이를 구분하지 못합니다.

**재순위기**는 초기 후보 리스트를 받아 각 청크를 원본 쿼리에 대해 더 강력한 모델로 점수를 매겨, 훨씬 더 정확한 관련성 순서를 만들어냅니다. 다음과 같은 경우에 특히 유용합니다:

- 코퍼스에 유사해 보이는 청크가 많을 때 (예: FAQ 항목)
- 벡터 검색의 상위 결과가 "비슷하지만 정확하지 않다"고 느낄 때
- 중요한 사용 사례에서 높은 정밀도의 답변이 필요할 때

## 재순위기 옵션

### LLM 재순위기

AI 서비스를 사용해 결과를 점수 매깁니다. 효과적이지만 지연 시간이 증가합니다:

```csharp
.WithRag(rag => rag
    .UseLlmReranker(aiService)
    .AddDocument("corpus.txt")
)
```

### Cohere 재순위기

Cohere Rerank API를 호출합니다 — 빠르고 정확합니다:

```csharp
.WithRag(rag => rag
    .UseCohereReranker(cohereApiKey)
    .AddDocument("corpus.txt")
)
```

### vLLM 재순위기

로컬에서 호스팅된 vLLM 재순위 엔드포인트를 사용합니다:

```csharp
.WithRag(rag => rag
    .UseVllmReranker("http://localhost:8000")
    .AddDocument("corpus.txt")
)
```

## 검색 파라미터

최종 선택 전에 검색되는 후보의 수와 필터 방법을 제어합니다:

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // 반환되는 최종 청크 수
    .WithRetrievalMultiplier(3)    // topK × 3 후보 검색 (재순위용)
    .WithMinScore(0.6)             // 최소 유사도 점수
    .AddDocument("corpus.txt")
)
```

- **`TopK`** — LLM 컨텍스트에 포함되는 최종 청크 수
- **`RetrievalMultiplier`** — 재순위기에게 더 넓은 후보군을 제공합니다. 3배수면 15개 후보를 가져온 뒤 재순위를 거쳐 상위 5개만 남깁니다.
- **`MinScore`** — `TopK`보다 적은 청크가 남더라도 이 유사도 임계값 이하의 결과는 버립니다

## 최종 선택 모드

재순위기를 사용할 때 최종 순위 점수 계산 방식을 선택합니다:

```csharp
using Mythosia.AI.Rag;

// 기본값: 재순위기 점수만 신뢰
.WithFinalSelectionMode(RagFinalSelectionMode.RerankerOnly)

// 검색 점수와 재순위기 점수를 혼합
.WithFinalSelectionMode(RagFinalSelectionMode.WeightedBlend)
.WithRetrievalWeightBlend(0.65)  // 65% 검색, 35% 재순위기
```

**`RerankerOnly`**는 안전한 기본값입니다 — 재순위기의 판단이 초기 검색 점수를 완전히 대체합니다.

**`WeightedBlend`**는 재순위기의 판단을 반영하면서 원래 검색 신호를 보존합니다. 벡터 임베딩이 이미 고품질이고 재순위기를 완전한 대체가 아닌 동점 결정자로 사용하고 싶을 때 유용합니다.
