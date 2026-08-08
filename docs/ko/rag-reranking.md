# 재순위 & 검색 튜닝

> 📍 **질문 응답 파이프라인:** [쿼리 재작성](rag-query-rewriting.md) → 임베딩 → 필터링 → [검색](rag-hybrid-search.md) → **`재순위`** → 컨텍스트 구성

## 검색 결과가 완벽하지 않은 이유

벡터 검색은 임베딩 유사도를 기준으로 후보를 가져옵니다. 하지만 임베딩 유사도는 **근사치**입니다. 비유하자면 책의 표지만 보고 내용을 짐작하는 것과 비슷하죠. 대부분은 맞지만, 가끔 0.82점짜리 청크가 실제로는 0.85점짜리보다 더 관련 있는 경우가 생깁니다.

## 재순위란?

**재순위(Reranking)**는 이 문제를 보완하는 단계입니다. 벡터 검색이 가져온 후보 목록을 받아서, 더 정밀한 모델로 각 청크를 **원본 질문과 직접 비교**하여 관련성을 다시 평가합니다.

비유하자면 이런 흐름입니다:

```
① 벡터 검색: 서가에서 관련 있어 보이는 책 15권을 빠르게 골라옴 (빠르지만 대략적)
    ↓
② 재순위: 15권을 하나하나 읽어보고 진짜 관련 있는 5권만 최종 선정 (느리지만 정확)
```

다음과 같은 상황에서 특히 효과적입니다:

- 문서에 비슷해 보이는 내용이 많을 때 (예: FAQ 항목들)
- 벡터 검색 상위 결과가 "비슷하긴 한데 정확히는 아닌" 느낌일 때
- 중요한 질문에 대해 높은 정밀도의 답변이 필요할 때

## 재순위기 옵션

용도와 환경에 따라 세 가지 재순위기 중 선택할 수 있습니다:

### LLM 재순위기

현재 사용 중인 AI 서비스를 활용해 결과를 재평가합니다. 별도 서비스 없이 바로 쓸 수 있지만, AI 호출이 추가되므로 응답 시간이 다소 늘어납니다:

```csharp
.WithRag(rag => rag
    .WithReranker(new LlmReranker(aiService))
    .AddDocument("corpus.txt")
)
```

### Cohere 재순위기

Cohere의 전용 Rerank API를 호출합니다. 재순위에 특화된 모델이라 빠르고 정확합니다:

```csharp
.WithRag(rag => rag
    .WithReranker(new CohereReranker(cohereApiKey))
    .AddDocument("corpus.txt")
)
```

### vLLM 재순위기

로컬에 호스팅된 vLLM 재순위 엔드포인트를 사용합니다. 데이터를 외부로 보내지 않아야 하는 환경에 적합합니다:

```csharp
.WithRag(rag => rag
    .WithReranker(new VllmReranker(baseUrl: "http://localhost:8000"))
    .AddDocument("corpus.txt")
)
```

## 검색 파라미터

검색 결과의 양과 품질을 조절하는 핵심 파라미터 세 가지입니다:

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // 최종적으로 AI에게 전달할 청크 수
    .WithRetrievalMultiplier(3)    // 재순위 전에 가져올 후보 배수 (5 × 3 = 15개)
    .WithScoreThreshold(0.6)       // 이 점수 미만인 청크는 버림
    .AddDocument("corpus.txt")
)
```

각 파라미터의 역할을 풀어보면:

- **`TopK`** — 최종적으로 AI 프롬프트에 포함되는 청크 수입니다
- **`RetrievalMultiplier`** — 재순위기가 더 좋은 결과를 고를 수 있도록 넓은 후보군을 제공합니다. 예를 들어 TopK=5에 배수 3이면, 먼저 15개를 가져온 뒤 재순위를 거쳐 상위 5개만 남깁니다
- **`WithScoreThreshold`** — 유사도가 너무 낮은 결과는 아예 제외합니다. TopK보다 적은 수가 남더라도 품질을 우선합니다

## 최종 선택 모드

재순위기를 사용할 때, 최종 순위 점수를 어떻게 산출할지 두 가지 방식 중 선택할 수 있습니다:

```csharp
using Mythosia.AI.Rag;

// 기본값: 재순위기의 판단만 사용
.WithFinalSelectionPolicy(RagFinalSelectionMode.RerankerOnly)

// 벡터 검색 점수와 재순위기 점수를 혼합
.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)  // 65% 검색, 35% 재순위기
```

- **`RerankerOnly`** — 안전한 기본값입니다. 재순위기의 판단이 원래 검색 점수를 완전히 대체합니다
- **`WeightedBlend`** — 벡터 임베딩의 품질이 이미 충분히 좋고, 재순위기를 보조적인 판단 도구로 활용하고 싶을 때 적합합니다
