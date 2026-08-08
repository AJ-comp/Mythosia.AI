# RAG 파이프라인 커스터마이징

## RAG 파이프라인이란?

RAG 파이프라인은 사용자의 질문이 들어온 순간부터 AI가 답변을 생성하기까지 거치는 **일련의 처리 단계**를 말합니다. 공장의 조립 라인처럼, 각 단계가 순서대로 실행되면서 질문을 점점 더 정확한 답변으로 만들어갑니다.

## 파이프라인의 전체 흐름

질문이 들어오면 다음과 같은 단계를 순서대로 거칩니다:

```
사용자 질문
    ↓
① 쿼리 재작성 (QueryRewrite)   — 대화 맥락을 반영해 검색 쿼리를 다듬습니다
    ↓
② 임베딩 (Embedding)           — 쿼리를 숫자 벡터로 변환합니다
    ↓
③ 필터링 (Filtering)           — 네임스페이스나 메타데이터로 검색 범위를 좁힙니다
    ↓
④ 검색 (Retrieval)             — 벡터 스토어에서 유사한 청크를 가져옵니다
    ↓
⑤ 재순위 (Reranking)           — 검색 결과의 관련성을 더 정밀하게 재평가합니다
    ↓
⑥ 컨텍스트 구성 (ContextBuild) — 최종 청크들을 프롬프트로 조립합니다
    ↓
AI 응답 생성
```

각 단계는 독립적으로 동작하기 때문에 필요에 따라 특정 단계만 교체하거나 건너뛸 수 있습니다. 예를 들어 쿼리 재작성은 멀티턴 대화가 아니라면 생략되고, 재순위기를 설정하지 않으면 재순위 단계도 자동으로 건너뜁니다.

## 파이프라인을 커스터마이징하는 이유

기본 RAG 파이프라인은 별도 설정 없이도 잘 동작하지만, 실제 프로젝트에서는 다음과 같은 이유로 세밀한 제어가 필요해집니다:

- **디버깅** — 어느 단계에서 시간이 오래 걸리는지, 쿼리 재작성이 의도치 않게 질문을 바꾸진 않았는지 확인하고 싶을 때
- **프롬프트 엔지니어링** — 기본 프롬프트 템플릿이 우리 서비스의 톤이나 요구사항에 맞지 않을 때
- **아키텍처** — 여러 AI 서비스가 하나의 인덱스를 공유해서 비용과 일관성을 관리하고 싶을 때
- **검수** — AI에게 보내기 전에 실제로 어떤 문서가 검색되었는지 미리 확인하고 싶을 때

아래에서 이런 제어를 가능하게 하는 도구들을 하나씩 살펴보겠습니다.

## 진행 상황 추적

각 단계가 실행될 때마다 콜백을 받아 파이프라인이 어느 단계를 지나고 있는지 실시간으로 확인할 수 있습니다:

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

각 단계 사이의 소요 시간을 측정하면 어디가 병목인지 쉽게 파악할 수 있습니다. 예를 들어 Retrieval 단계가 유독 느리다면 벡터 스토어의 인덱스 설정을 점검해볼 수 있겠죠.

## 커스텀 프롬프트 템플릿

검색된 문서 내용이 AI에게 전달되는 방식을 직접 제어할 수 있습니다. `{context}`에는 검색된 청크들이, `{question}`에는 사용자의 질문이 들어갑니다:

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

프롬프트 템플릿을 잘 작성하면 AI가 문서 내용 바깥의 이야기를 지어내는 현상(환각)을 크게 줄일 수 있습니다.

## RagStore 공유

문서 인덱스를 한 번만 만들고 여러 AI 서비스에서 함께 사용할 수 있습니다. 같은 문서를 기반으로 여러 모델의 답변 품질을 비교하거나 A/B 테스트를 할 때 유용합니다:

```csharp
// 인덱스를 한 번만 빌드
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

// 서로 다른 AI 서비스에서 같은 인덱스를 재사용
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

두 서비스가 동일한 임베딩과 벡터 인덱스를 공유하므로, 저장 공간이나 임베딩 연산이 중복되지 않습니다.

## RagStore 직접 쿼리

AI 서비스를 거치지 않고 벡터 스토어에 직접 질문을 던져볼 수도 있습니다. AI에게 보내기 전에 "실제로 어떤 문서가 검색되는지" 확인하고 싶을 때 유용합니다:

```csharp
RagProcessedQuery result = await store.QueryAsync("반품 정책이 무엇인가요?");

Console.WriteLine($"재작성된 쿼리: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent`에는 AI에게 전달될 완성된 프롬프트가 그대로 들어 있습니다. LLM 토큰을 소비하지 않으면서 검색 품질을 점검할 수 있어, 개발 중 디버깅에 매우 유용합니다.

## 내부 동작 원리

`.WithRag()`를 호출하면 실제로는 `RagEnabledService`라는 래퍼가 생성됩니다. 이 래퍼는 원래 AIService를 감싸면서 RAG 파이프라인과 LLM 호출을 자동으로 연결합니다. 그 핵심에는 [AIRequestContext](request-contexts.md)가 있습니다.

### 전체 흐름

```
ragService.GetCompletionAsync("환불 정책이 뭔가요?")
    ↓
① RagEnabledService가 RAG 파이프라인 실행
   쿼리 재작성 → 임베딩 → 검색 → 컨텍스트 조립
    ↓
② TemplateContextBuilder가 {context}와 {question}을 치환
   → "다음 정보로 답하세요.\n[1] 환불은 30일 이내...\n질문: 환불 정책이 뭔가요?"
    ↓
③ RagEnabledService가 AIRequestContext 생성
   RequestMessageOverride = 조립된 프롬프트
    ↓
④ _innerService.GetCompletionAsync(원래 메시지, context: context) 호출
   → AIService가 AsyncLocal에 context 저장
   → 원래 질문을 대화 기록에 추가
    ↓
⑤ AIService.GetLatestMessages()가 마지막 메시지를 교체
   대화 기록: "환불 정책이 뭔가요?" (원본 유지)
   모델이 보는 것: 조립된 프롬프트 (RequestMessageOverride)
```

### 왜 이렇게 동작하나요?

이 설계의 핵심은 **대화 기록과 모델 입력의 분리**입니다:

- **대화 기록에는 원래 질문이 남습니다** — 이후 대화에서 "그것"이 무엇인지 맥락을 유지합니다
- **모델에는 조립된 프롬프트가 전달됩니다** — 검색된 문서 + 질문이 포함된 완성된 프롬프트
- **AIService의 상태는 변하지 않습니다** — `AsyncLocal<T>`을 통해 요청 단위로 격리됩니다

이것이 `request-contexts.md`에서 설명하는 `RequestMessageOverride`의 실제 사용 사례입니다. RAG 파이프라인이 이 메커니즘을 자동으로 활용하기 때문에, 사용자는 `.WithRag()`만 호출하면 됩니다.

### 코드로 보기

`RagEnabledService` 내부에서 이 연결이 일어나는 핵심 코드입니다:

```csharp
// RagEnabledService.GetCompletionAsync 내부
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
return await _innerService.GetCompletionAsync(
    new Message(ActorRole.User, query),         // ← 원래 질문 (대화 기록에 저장됨)
    context: BuildRequestContext(processed));    // ← 조립된 프롬프트 (모델만 봄)

// BuildRequestContext — AIRequestContext를 생성하는 부분
private static AIRequestContext BuildRequestContext(RagProcessedQuery processed)
{
    return new AIRequestContext
    {
        RequestMessageOverride = new Message(
            ActorRole.User,
            processed.RequestMessageContent)  // ← TemplateContextBuilder의 결과물
    };
}
```

`AIService`는 이 context를 `AsyncLocal`에 저장한 뒤, `GetLatestMessages()` 에서 마지막 메시지를 `RequestMessageOverride`로 교체합니다. 요청이 끝나면 자동으로 복원되므로 다음 요청에 영향을 주지 않습니다.
