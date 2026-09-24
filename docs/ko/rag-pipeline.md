# RAG 파이프라인 커스터마이징

<a id="indexing-validation"></a>

## 색인 실패 시 기존 문서 보호하기

사용자 정의 분할기나 임베딩 응답에 문제가 있어도 검색 중인 문서가 불완전하거나 잘못 연결된 내용으로 조용히 교체되면 안 됩니다. 파이프라인은 `onDocumentEmbedded`를 사용할 때도 문서별로 저장을 시작하기 전에 입력과 결과를 검증합니다.

임베딩·저장·저장 콜백을 호출하기 전에 `RagDocument.Id`가 null, 빈 문자열 또는 공백뿐이면 `ArgumentException`이 발생합니다. 분할 결과 목록이나 청크가 null이거나, `Content`·`Metadata`가 null이거나, 청크 ID가 비어 있거나 공백뿐이거나, 같은 문서 안에서 청크 ID가 중복되면 `InvalidOperationException`이 발생합니다. 중복은 `StringComparer.Ordinal`로 대소문자를 구분합니다. 청크 값과 메타데이터는 첫 임베딩 호출 전에 복사합니다.

유효한 사용자 지정 ID는 그대로 유지합니다. ID를 자동 생성하거나 공백을 제거해 보정하지 않으며, 서로 다른 문서의 사용자 지정 청크 ID 충돌을 전체 저장소에서 검사하지는 않습니다. [사용자 정의 분할기 예제](text-splitters.md)처럼 대상 컬렉션에서 고유한 ID를 만드세요. 예약 키 `document_id`는 저장용 복사본에서만 실제 문서 ID로 정규화하며 원본 메타데이터는 변경하지 않습니다.

잘못된 ID, 분할 실패, 유효하지 않은 임베딩 배치는 해당 문서의 기존 레코드를 유지하며 저장 콜백도 호출하지 않습니다. 해당 문서의 모든 배치가 [임베딩 검증](rag-embedding.md#embedding-validation)을 통과해야 저장을 시작합니다. 같은 작업에서 앞서 저장을 마친 다른 문서까지 되돌리지는 않으며, 저장이 시작된 뒤의 롤백은 저장소나 콜백 구현에 달려 있습니다.

이번 검증과 응답 순서 교정은 이미 덮어써진 본문이나 잘못된 청크에 연결되어 저장된 기존 벡터를 자동 복구하지 않으므로, 영향받은 문서는 원본에서 다시 색인해야 합니다.

<a id="custom-persistence"></a>

## 저장 콜백에서도 문서 전체를 교체하기

문서가 짧아졌을 때 새 청크만 upsert하면 옛 뒷부분이 검색에 남습니다. `onDocumentEmbedded`는 기본 저장을 완전히 대체하므로 전달받은 레코드의 정규화된 `document_id`로 문서 전체를 교체하세요. 콜백은 검증을 마친 비어 있지 않은 문서 한 개씩 받습니다:

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

분할이 성공해 청크가 0개인 경우에는 콜백을 호출하지 않고 기본 저장소에도 접근하지 않습니다. 알고 있는 문서 ID로 자체 저장소에서 명시적으로 삭제하세요. `DeleteDocumentAsync`는 파이프라인의 저장소를 대상으로 할 때 사용합니다. 교체의 원자성과 롤백은 선택한 저장소나 콜백 구현에 달려 있습니다.

<a id="url-documents"></a>

## URL 문서를 안전하게 읽기

서버는 텍스트 문서를 전송할 때 압축할 수 있습니다. `AddUrl`은 `gzip`, `deflate`, Brotli(`br`)를 풀고 텍스트를 읽으며, 압축 스트림이 끝까지 완성됐는지도 검사합니다. HTTP 전송에 성공했더라도 압축 데이터가 잘렸거나 압축 해제가 실패하거나 해당 형식에서 제공하는 체크섬이 맞지 않으면 임베딩·저장 전에 로딩을 중단하여 해당 문서의 기존 레코드를 유지합니다. 지원하지 않는 `Content-Encoding`이나 여러 겹의 압축 인코딩도 임베딩·저장 전에 거부합니다.

느린 URL 문서를 더 기다리지 않으려면 `RagStore.BuildAsync`에 `cancellationToken`을 전달하세요. 이 토큰은 HTTP 요청, 응답 본문 읽기, 압축 해제까지 전달됩니다. 취소는 협력적으로 처리하며, 앞서 저장을 마친 다른 문서까지 되돌리지는 않습니다.

<a id="custom-retriever"></a>

## 임베딩을 강제하지 않는 검색기 연결

상품 코드는 키워드 검색이 유리하고, 문서와 표현이 다른 질문은 의미 검색이 필요합니다. 선택한 검색기는 각 방식에 필요한 처리만 실행합니다. 키워드 검색을 위해 질문 임베딩부터 만들 필요가 없습니다.

- 이전: 모든 검색 전략에 질문 임베딩을 먼저 전달했습니다.
- 이후: 선택한 검색기가 필요한 질문 표현만 만듭니다.

외부 색인이나 다른 질문 표현을 사용하려면 `IRagRetriever`를 구현합니다. `RagRetrievalRequest`는 `Query`(전체 의미 검색 질문), nullable `TextQuery`(키워드 검색어 재정의), `TopK`, `Filter`, `ProgressAsync`를 전달합니다. 내장 검색기는 `TextQuery`가 null이면 `Query`를 사용하고 빈 문자열이면 텍스트 검색을 생략합니다. 커스텀 검색기는 필요한 변환을 수행하고 필터, 결과 개수, 취소 요청을 적용해야 합니다.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class CatalogRetriever : IRagRetriever
{
    private readonly ITextSearchStore _catalog;
    public CatalogRetriever(ITextSearchStore catalog) => _catalog = catalog;

    public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.TextSearchAsync(
            request.TextQuery ?? request.Query,
            request.TopK,
            request.Filter,
            cancellationToken);
}
```

```csharp
// catalog: an existing ITextSearchStore
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag.UseRetriever(new CatalogRetriever(catalog)));
```

`UseRetriever(...)` 또는 `RagPipeline.SetRetriever(...)`로 등록합니다. 기존 `IRetrievalStrategy`와 `SetRetrievalStrategy(...)`도 유지되며 호환 어댑터에서 계속 질문 임베딩을 만듭니다. 반환 기록에는 재랭킹과 컨텍스트 구성에 필요한 본문과 메타데이터가 있어야 합니다.

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()`는 질문 임베딩을 생략합니다. 문서 등록은 여전히 기존 벡터 저장소를 위해 청크를 나누고 임베딩을 생성합니다. 문서까지 키워드만으로 저장하는 API는 아닙니다. 지연 초기화로 첫 질문에서 문서를 등록하면 문서 임베딩 호출이 발생할 수 있습니다.

질문의 `Embedding` 단계는 선택한 검색기에 따라 실행됩니다. 키워드 검색은 이 단계를 보고하지 않습니다. 커스텀 검색기는 `request.ProgressAsync`로 실제 단계의 진행 상황을 알릴 수 있습니다. 문서 등록 임베딩은 그대로입니다.

## RAG 파이프라인이란?

RAG 파이프라인은 사용자의 질문이 들어온 순간부터 AI가 답변을 생성하기까지 거치는 **일련의 처리 단계**를 말합니다. 공장의 조립 라인처럼, 각 단계가 순서대로 실행되면서 질문을 점점 더 정확한 답변으로 만들어갑니다.

## 파이프라인의 전체 흐름

질문이 들어오면 다음과 같은 단계를 순서대로 거칩니다:

```
사용자 질문
    ↓
① 쿼리 재작성 (QueryRewrite)   — 대화 맥락을 반영해 검색 쿼리를 다듬습니다
    ↓
② 필터링 (Filtering)           — 네임스페이스나 메타데이터로 검색 범위를 좁힙니다
    ↓
③ 필요한 경우 임베딩 (Embedding)           — 쿼리를 숫자 벡터로 변환합니다
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

검색 단계와 답변 생성 단계의 진행 상황은 따로 관찰할 수 있습니다. 아래의 `ProgressAsync`는 검색 파이프라인을 표시하고, 이후 생성되는 텍스트와 실행 제어는 `StartRunAsync`가 반환한 Run으로 처리합니다. [Run 사용 안내](execution-api-transition.md)에서 두 단계의 경계를 확인하세요.

## 진행 상황 추적

각 단계가 실행될 때마다 콜백을 받아 파이프라인이 어느 단계를 지나고 있는지 실시간으로 확인할 수 있습니다:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // 단계: QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
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
   쿼리 재작성 → 필터링 → 임베딩(필요 시) → 검색 → 컨텍스트 조립
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
⑤ AIService.GetLatestMessages()가 현재 요청의 최초 입력을 교체
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
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
var original = new Message(ActorRole.User, query);
return await _innerService.GetCompletionAsync(
    original,
    context: BuildRequestContext(processed, original),
    cancellationToken: cancellationToken);

private static AIRequestContext BuildRequestContext(RagProcessedQuery processed, Message original)
{
    var requestMessage = original.Clone();
    requestMessage.Content = processed.RequestMessageContent;
    if (original.HasMultimodalContent)
    {
        requestMessage.Contents = new List<MessageContent>
        {
            new TextContent(processed.RequestMessageContent)
        };
        requestMessage.Contents.AddRange(
            original.Contents.Where(content => !(content is TextContent)));
    }
    return new AIRequestContext { RequestMessageOverride = requestMessage };
}
```

`AIService`는 context를 `AsyncLocal`에 저장합니다. `GetLatestMessages()`는 현재 논리적 요청의 최초 입력에만 `RequestMessageOverride`를 적용하고, 이후 assistant의 도구 호출과 도구 실행 결과는 유지합니다. 따라서 후속 모델 요청에도 검색 문서와 도구 결과를 함께 전달합니다. 요청이 끝나면 이전 context로 복원합니다.
