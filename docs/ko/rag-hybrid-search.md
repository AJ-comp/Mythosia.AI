# 하이브리드 검색

## 왜 하이브리드 검색인가?

순수 벡터 검색은 의미적 유사성 파악에 뛰어납니다 — "구독 취소"가 "멤버십 해지"와 단어가 다르더라도 매칭됩니다. 그러나 사용자가 그대로 입력한 **정확한 용어** — 상품명, 오류 코드, 정책 식별자 등 — 을 놓칠 수 있습니다.

BM25 키워드 검색은 이런 경우를 완벽히 처리하지만, 의미적 이해에는 약합니다. **하이브리드 검색은 두 가지를 결합**하여 의미적 이해와 정확한 키워드 매칭을 동시에 제공합니다.

## 설정

하나의 메서드 호출로 밀집 벡터 검색과 BM25 키워드 검색을 혼합합니다:

```csharp
.WithRag(rag => rag
    .UseHybridRetrieval(vectorWeight: 0.6f)  // 60% 벡터, 40% BM25
    .AddDocument("knowledge-base.txt")
)
```

`vectorWeight`의 범위는 0.0 (순수 BM25)에서 1.0 (순수 벡터)입니다. 대부분의 경우 **0.5~0.7** 정도가 적합합니다.

## 상황별 권장 가중치

| 시나리오 | 권장 가중치 |
| --- | --- |
| 자연어 기반 일반 Q&A | 0.7–0.8 (벡터 중심) |
| 특정 용어가 많은 기술 문서 | 0.4–0.5 (균형) |
| 코드/오류 코드 조회 | 0.2–0.3 (BM25 중심) |

## 예제

```csharp
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseHybridRetrieval(vectorWeight: 0.5f)
        .AddDocument("product-catalog.txt")
        .AddDocument("error-codes.txt")
    );

// "ERR-4012"는 BM25로, 의미적 컨텍스트는 벡터로 매칭
var answer = await service.GetCompletionAsync("ERR-4012를 어떻게 해결하나요?");
```
