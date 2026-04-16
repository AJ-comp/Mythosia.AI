# 에이전틱 RAG

에이전틱 RAG는 `RagStore`를 에이전트 루프 안에서 호출할 수 있는 검색 도구로 등록합니다. 일반 RAG처럼 사용자 메시지마다 무조건 한 번 검색하는 방식이 아니라, 에이전트가 언제 검색할지, 어떤 쿼리로 검색할지, 다시 검색할지, 그리고 다른 도구와 어떻게 조합할지를 스스로 결정합니다.

## 왜 에이전틱 RAG인가?

일반 RAG는 검색 후 답변을 생성하는 고정된 흐름입니다. 단순하고 빠르지만 다음 상황에서는 부족할 수 있습니다.

- 질문이 여러 주제를 가로질러 여러 번의 검색을 필요로 하는 경우
- 첫 검색 결과가 부족해서 더 나은 쿼리로 다시 검색해야 하는 경우
- 문서 검색이 전혀 필요하지 않은 질문인 경우
- 최종 답변이 문서와 실시간 API 데이터 모두에 의존하는 경우
- 각 검색 단계마다 권한 필터나 진단 정보를 적용해야 하는 경우

에이전틱 RAG는 `RunAgentAsync(...)` 또는 `RunAgentStreamAsync(...)`가 RAG를 에이전트의 등록된 함수 중 하나로 사용하도록 만들어 이런 상황을 처리합니다.

## 빠른 시작

`RagStore`를 한 번 빌드한 뒤 `WithAgenticRag(...)`로 등록하고 에이전트를 실행합니다.

```csharp
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("환불 정책을 요약해 줘.");
```

기본적으로 `WithAgenticRag(...)`는 `search_documents`라는 도구를 등록합니다. 에이전트는 문서 컨텍스트가 필요하다고 판단하면 이 도구를 자동으로 호출하고, 검색된 발췌문을 바탕으로 최종 답변을 생성합니다.

## 스트리밍 에이전틱 RAG

UI에서 답변 텍스트를 스트리밍하면서 도구 호출과 도구 결과도 함께 관찰해야 한다면 `RunAgentStreamAsync(...)`를 사용합니다.

```csharp
service.WithAgenticRag(ragStore);

await foreach (var content in service.RunAgentStreamAsync(
    "환불 정책을 요약하고 주요 자격 조건도 알려 줘.",
    maxSteps: 10))
{
    if (content.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"문서 검색 도구 호출: {content.Metadata["function_name"]}");
    }
    else if (content.Type == StreamingContentType.Text)
    {
        Console.Write(content.Content);
    }
}
```

`RunAgentStreamAsync(...)`는 채팅 UI에 적합합니다. 에이전트가 문서를 검색하고 다른 도구를 호출하고 최종 답변을 작성하는 동안 사용자에게 진행 중인 텍스트를 보여줄 수 있습니다.

## 다른 도구와 조합하기

에이전틱 RAG는 문서 검색을 실시간 API, 계산기, 업무 액션, 도메인 전용 함수와 함께 등록할 때 특히 유용합니다.

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "주문 ID로 주문 상태를 조회합니다.",
           ("order_id", "조회할 주문 ID.", required: true),
           async id => await orderApi.GetStatusAsync(id));

var answer = await service.RunAgentAsync(
    "주문 #12345는 현재 정책 기준으로 환불 대상인가요?");
```

이 예제에서 에이전트는 환불 규칙을 문서에서 검색하고, 주문 API로 실시간 주문 상태를 조회한 뒤, 두 정보를 합쳐 최종 답변을 생성할 수 있습니다.

## 도구 설명 커스터마이징

도구 설명은 모델이 RAG 도구를 언제 호출할지에 큰 영향을 줍니다. 기본 설명은 범용적이며 에이전트에게 독립적인 검색 쿼리를 사용하라고 안내하지만, 실제 앱에서는 도메인에 맞는 설명을 제공하는 것이 좋습니다.

```csharp
service.WithAgenticRag(
    ragStore,
    toolDescription:
        "사내 HR 정책, 제품 매뉴얼, 컴플라이언스 문서를 검색합니다. " +
        "회사 정책이나 제품 관련 정보가 필요할 때 이 도구를 호출하세요.");
```

문서 도메인이 분명한 앱에서 "문서 검색"처럼 모호한 설명만 쓰면 에이전트가 RAG를 너무 자주 호출하거나, 필요한 순간에 호출하지 않을 수 있습니다. 인덱스에 어떤 정보가 들어 있고 언제 사용해야 하는지 구체적으로 적는 것이 좋습니다.

## 도구 이름 커스터마이징

기본 도구 이름은 `search_documents`입니다. 여러 검색 도구를 함께 등록하거나 더 도메인에 맞는 이름이 도구 선택에 도움이 된다면 이름을 바꿀 수 있습니다.

```csharp
service.WithAgenticRag(
    ragStore,
    toolName: "search_private_docs",
    toolDescription: "현재 사용자가 접근할 수 있는 문서만 검색합니다.");
```

도구 이름은 안정적이고 설명적인 `snake_case` 이름을 권장합니다. tracing도 함께 등록한다면 `WithAgenticRagTracing(...)`에도 같은 도구 이름을 전달해야 합니다.

## 검색 단계별 Query Options

각 에이전트 검색 단계마다 새로운 `RagQueryOptions`가 필요하면 `queryOptions` 오버로드를 사용합니다. 보통 tenant 필터, 사용자 권한, storage 범위, 동적 `TopK`, 검색 정책을 여기에서 적용합니다.

```csharp
service.WithAgenticRag(
    ragStore,
    queryOptions: ctx => new RagQueryOptions
    {
        StoreFilter = new VectorFilter()
            .Where("tenant", currentTenantId)
            .Where("storage_id", currentStorageId),
        FinalFilter = new RagFilter
        {
            TopK = ctx.Query.Contains("정확한 정책", StringComparison.OrdinalIgnoreCase)
                ? 8
                : 5
        }
    },
    toolDescription: "현재 사용자가 접근할 수 있는 문서만 검색합니다.");
```

콜백은 `AgenticRagQueryContext`를 받습니다.

- `ToolName`: 현재 실행 중인 등록 도구 이름
- `Query`: 이 검색 단계에서 에이전트가 만든 독립적인 검색 쿼리

요청 전체에 같은 옵션을 적용하면 `_ => ...` 형태를 사용하면 됩니다. 검색 단계별로 필터나 검색 정책을 바꿔야 한다면 `ctx.Query` 또는 `ctx.ToolName`을 확인하세요.

## 구조화된 Tracing

`WithAgenticRagTracing(...)`은 에이전틱 RAG 검색 실행을 관찰하는 trace observer를 등록합니다. 이 API는 의도적으로 `WithAgenticRag(...)`와 분리되어 있습니다.

- `WithAgenticRag(...)`: RAG 검색 도구를 등록하고 검색 단계별 query options를 계산합니다.
- `WithAgenticRagTracing(...)`: 해당 도구가 생성하는 검색 trace를 받을 observer를 등록합니다.

```csharp
var traces = new List<AgenticRagSearchTrace>();

service
    .WithAgenticRag(
        ragStore,
        queryOptions: _ => new RagQueryOptions
        {
            StoreFilter = new VectorFilter()
                .Where("tenant", currentTenantId)
                .Where("storage_id", currentStorageId)
        },
        toolDescription: "현재 사용자가 접근할 수 있는 문서만 검색합니다.")
    .WithAgenticRagTracing(trace =>
    {
        traces.Add(trace);
    });
```

각 `AgenticRagSearchTrace`에는 다음 정보가 들어 있습니다.

- `ToolName`: trace를 만든 도구 이름
- `Query`: 해당 검색 단계에서 실행한 독립적인 검색 쿼리
- `QueryOptions`: 계산된 per-call `RagQueryOptions`
- `Result`: 검색에 성공했을 때의 구조화된 `RagProcessedQuery`
- `Result.References`: 에이전트에게 반환된 최종 선택 참조
- `Result.RetrievalCandidates` / `Result.RerankedCandidates`: 최종 선택 전 후보
- `Result.Diagnostics`: 적용된 검색 설정과 경과 시간
- `Succeeded` / `Exception`: 도구 실행 성공 여부와 실패 원인
- `HasReferences`: 성공한 검색 결과에 참조가 포함되어 있는지 여부

Trace observer는 reference panel, audit log, 검색 품질 분석, retrieval 설정 디버깅, 권한 조회나 vector search 실패 기록에 유용합니다.

Trace callback은 관찰용 보조 기능입니다. callback 내부에서 예외가 발생해도 에이전트 실행이 깨지지 않도록 예외는 무시됩니다.

## Custom Tool Name과 Tracing

Tracing은 service instance와 tool name 기준으로 등록됩니다. 에이전틱 RAG 도구 이름을 바꿨다면 tracing에도 같은 이름을 넘겨야 합니다.

```csharp
service
    .WithAgenticRag(ragStore, toolName: "search_private_docs")
    .WithAgenticRagTracing(
        trace => traces.Add(trace),
        toolName: "search_private_docs");
```

이름이 서로 다르면 검색 자체는 정상 동작하지만, 해당 observer는 그 도구의 trace를 받지 못합니다.

## Query Rewriter 동작

에이전틱 RAG에서는 `QueryRewriter`를 의도적으로 우회합니다. 에이전트가 RAG 도구를 호출하기 전에 스스로 독립적인 검색 쿼리를 만들기 때문에, 별도의 query rewrite 단계는 중복이며 에이전트의 의도를 왜곡할 수 있습니다.

다만 `RagStore`에 설정된 retrieval strategy와 pipeline options는 그대로 적용됩니다. Vector search, hybrid search, reranking, final selection, context building, `StoreFilter`, diagnostics는 모두 `RagStore.QueryAsync(...)` 내부에서 계속 동작합니다.

## 일반 RAG와의 차이

| | 일반 RAG | 에이전틱 RAG |
| --- | --- | --- |
| 검색 시점 | 모든 메시지마다 검색 | 에이전트가 필요할 때 결정 |
| 쿼리 생성 | `QueryRewriter` | 에이전트가 독립적인 쿼리 생성 |
| 검색 횟수 | 턴당 1회 | 필요에 따라 1회 이상 |
| 도구 조합 | 문서 검색 중심 | 등록된 모든 함수/도구와 조합 |
| 단계별 필터 | 요청 옵션 | 도구 호출마다 `queryOptions` callback |
| 관찰성 | RAG 결과/진단 | 검색 단계별 `AgenticRagSearchTrace` |
| 설정 방식 | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync(...)` 또는 `RunAgentStreamAsync(...)` |

## 언제 무엇을 선택할까?

- **일반 RAG**: 모든 질문이 문서 기반이고 단일 주제이며, 낮은 지연 시간이 더 중요한 경우
- **에이전틱 RAG**: 질문이 여러 주제를 다루거나, 문서 검색과 실시간 데이터를 조합해야 하거나, 반복 검색이 필요하거나, 검색 단계별 권한 필터와 진단이 필요한 경우

## 실무 팁

- 에이전트가 검색하고 결과를 검토하고 필요하면 다시 검색할 수 있도록 `maxSteps`를 충분히 잡으세요.
- 도구 설명은 단순한 이름표가 아니라 사용 정책처럼 작성하세요.
- tenant isolation과 permission boundary는 per-call `StoreFilter`로 적용하세요.
- citations, reference panel, audit log, retrieval diagnostics가 필요하면 trace를 수집하세요.
- custom tool name은 도구 선택이 더 명확해질 때만 사용하세요.
