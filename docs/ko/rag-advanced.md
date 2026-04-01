# 고급 RAG

## 하이브리드 검색

밀집 벡터 검색과 BM25 키워드 검색을 혼합합니다. 특정 용어나 이름이 포함된 쿼리에서 더 높은 재현율을 제공합니다:

```csharp
.WithRag(rag => rag
    .UseHybridRetrieval(vectorWeight: 0.6f)  // 60% 벡터, 40% BM25
    .AddDocument("knowledge-base.txt")
)
```

`vectorWeight`의 범위는 0.0 (순수 BM25)에서 1.0 (순수 벡터)입니다. 대부분의 경우 0.5~0.7 정도가 적합합니다.

## 쿼리 재작성

멀티턴 대명사 참조를 해석하고 더 나은 검색을 위해 쿼리를 확장합니다. `LlmQueryRewriter`는 임베딩 전에 AI 서비스 자체를 사용해 쿼리를 재작성합니다:

```csharp
.WithRag(rag => rag
    .WithQueryRewriter()             // 동일한 AI 서비스 사용
    .WithQueryRewriteMaxTokens(250)  // 재작성을 위한 토큰 예산
    .AddDocument("docs.txt")
)
```

다음과 같은 대화가 주어지면:
> 사용자: "환불 정책에 대해 알려주세요."
> 사용자: "**그것**의 예외는 무엇인가요?"

재작성기는 검색 전에 "그것" → "환불 정책 예외"로 확장합니다.

또한 **검색 게이트**를 구현합니다: "감사합니다!"와 같이 검색이 필요 없는 쿼리면 벡터 검색을 건너뜁니다.

## 재순위

재순위기는 초기 검색 후보들을 점수 매기고 컨텍스트를 구성하기 전에 관련성에 따라 재정렬합니다.

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

`WithRetrievalMultiplier`는 재순위기를 사용할 때 유용합니다 — 더 많은 후보를 검색하면 재순위기가 더 많은 것을 활용할 수 있습니다.

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

`WeightedBlend`는 재순위기의 판단을 반영하면서 원래 검색 신호를 보존합니다.

## 진행 상황 추적

쿼리별 비동기 콜백으로 실행 중인 RAG 단계를 추적합니다:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // 단계: QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("질문", options);
```

## 커스텀 프롬프트 템플릿

`{context}`와 `{question}` 플레이스홀더를 사용해 검색된 컨텍스트가 프롬프트에 주입되는 방식을 제어합니다:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        다음 정보만을 사용해 질문에 답하세요.
        답변이 컨텍스트에 없으면 "모르겠습니다"라고 말하세요.

        컨텍스트:
        {context}

        질문: {question}
        """)
    .AddDocument("faq.txt")
)
```

## RagStore 공유

인덱스를 한 번 구성하고 여러 서비스 인스턴스에서 재사용합니다:

```csharp
// 한 번만 구성
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDirectory("docs/", ".txt", ".md", ".pdf")
    .BuildAsync();

// 여러 서비스에서 재사용
var claudeRag = new ClaudeService(apiKey, http).WithRag(store);
var gptRag    = new ChatGptService(apiKey, http).WithRag(store);
```

## RagStore 직접 쿼리

AI 서비스와 독립적으로 스토어를 쿼리하여 검색 결과를 확인합니다:

```csharp
RagProcessedQuery result = await store.QueryAsync("반품 정책이 무엇인가요?");

Console.WriteLine($"재작성된 쿼리: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent`에는 LLM에 전송될 완전히 조립된 프롬프트가 포함됩니다.

## 멀티턴 RAG

재작성기가 참조를 해석할 수 있도록 대화 기록을 스토어 쿼리에 전달합니다:

```csharp
var history = new List<ConversationTurn>
{
    new ConversationTurn("환불 정책이 무엇인가요?", "30일 이내에 반품할 수 있습니다."),
    new ConversationTurn("디지털 제품은요?", "디지털 제품은 환불이 불가합니다.")
};

var result = await store.QueryAsync(
    query: "거기에 예외가 있나요?",
    conversationHistory: history
);
```
