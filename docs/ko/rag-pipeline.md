# RAG 파이프라인 커스터마이징

## 왜 파이프라인을 커스터마이징하는가?

기본 RAG 파이프라인은 바로 사용해도 잘 동작하지만, 실제 프로젝트에서는 더 많은 제어가 필요한 경우가 많습니다:

- **디버깅** — 어느 단계가 느린가? 재작성기가 쿼리를 예상치 못한 방식으로 바꾸고 있진 않은가?
- **프롬프트 엔지니어링** — 기본 프롬프트 템플릿이 도메인의 톤이나 제약에 맞지 않을 수 있음
- **아키텍처** — 여러 서비스가 하나의 인덱스를 공유하면 메모리를 절약하고 임베딩 일관성을 유지
- **검수** — 때때로 LLM에 보내기 *전에* 검색 결과를 확인해야 할 때가 있음

이 챕터에서는 이런 제어를 가능하게 하는 도구들을 다룹니다.

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

지연 시간 프로파일링에 매우 유용합니다 — 단계 간 시간을 측정하여 병목 지점을 찾을 수 있습니다.

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

잘 만들어진 템플릿은 모델이 제공된 컨텍스트 안에서만 답변하도록 지시하여 환각(hallucination)을 극적으로 줄일 수 있습니다.

## RagStore 공유

인덱스를 한 번 구성하고 여러 서비스 인스턴스에서 재사용합니다 — 프로바이더 비교나 A/B 테스트에 유용합니다:

```csharp
// 한 번만 구성
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDirectory("docs/", ".txt", ".md", ".pdf")
    .BuildAsync();

// 여러 서비스에서 재사용
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

두 서비스가 동일한 임베딩과 벡터 인덱스를 공유합니다 — 스토리지나 연산의 중복이 없습니다.

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

`result.RequestMessageContent`에는 LLM에 전송될 완전히 조립된 프롬프트가 포함됩니다. LLM 토큰을 사용하지 않고 검색 품질을 디버깅하는 데 매우 유용합니다.
