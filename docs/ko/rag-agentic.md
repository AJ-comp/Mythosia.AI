# 에이전틱 RAG

## 왜 에이전틱 RAG인가?

일반 RAG에서는 모든 사용자 메시지에 대해 정확히 **한 번**의 검색이 실행됩니다. 시스템이 검색하고, 컨텍스트를 구성하고, 응답을 생성합니다 — 무조건. 단순한 질문에는 잘 동작하지만, 다음과 같은 경우에는 한계가 있습니다:

- 질문이 서로 다른 주제에 대해 **여러 번의 검색**이 필요한 경우 (예: "하드웨어와 소프트웨어 제품의 환불 정책을 비교해 주세요")
- 첫 번째 검색 결과가 **부족**해서 보완 검색이 필요한 경우
- **검색이 전혀 필요 없는** 질문인 경우 (예: "지금까지 대화를 요약해 주세요")
- 답변이 **문서 검색과 실시간 데이터**의 조합에 의존하는 경우

에이전틱 RAG는 이 모든 것을 해결합니다. 고정된 검색→답변 파이프라인 대신, **에이전트가 자율적으로 판단**합니다 — 언제 검색할지, 무엇을 검색할지, 재검색이 필요한지, 다른 Tool을 호출할지를 ReAct 루프 안에서 결정합니다.

## 빠른 시작

`WithAgenticRag`으로 `RagStore`를 도구로 등록하고 `RunAgentAsync`에 위임합니다:

```csharp
// 인덱스를 한 번만 빌드
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

// RAG를 Tool로 등록하고 에이전트 실행
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("환불 정책을 요약해 줘.");
```

에이전트는 문서 컨텍스트가 필요할 때마다 자동으로 `search_documents`를 호출하고, 검색된 내용을 바탕으로 최종 답변을 생성합니다.

## 다른 Tool과 조합

에이전틱 RAG는 추가 Tool과 함께 쓸 때 진가를 발휘합니다. 에이전트가 각 하위 작업에 맞는 Tool을 스스로 선택합니다:

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "주문 ID로 주문 상태를 조회합니다.",
           ("order_id", "조회할 주문 ID.", required: true),
           async id => await orderApi.GetStatusAsync(id));

// 에이전트가 정책은 문서에서 검색하고, 주문 현황은 API에서 조회
var answer = await service.RunAgentAsync(
    "주문 #12345 — 현재 정책상 환불 대상인가요?");
```

이 예제에서 에이전트는 자율적으로:

1. 문서에서 환불 정책을 검색
2. 주문 API를 호출하여 주문 #12345의 상태를 조회
3. 두 정보를 결합하여 최종 답변을 생성

## Tool 설명 커스터마이징

Tool 설명은 에이전트가 RAG를 호출하는 기준이 됩니다. 도메인에 맞게 작성하면 Tool 선택 정확도가 높아집니다:

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "사내 HR 정책, 제품 매뉴얼, 컴플라이언스 문서를 검색합니다. " +
        "회사 정책이나 제품 관련 정보가 필요할 때 호출하세요.");
```

"문서 검색"과 같은 모호한 설명은 에이전트가 RAG를 너무 자주 또는 너무 드물게 호출하게 만들 수 있습니다. 문서에 **어떤 종류의 정보**가 포함되어 있는지 구체적으로 작성하세요.

## 일반 RAG와의 차이

| | 일반 RAG | 에이전틱 RAG |
| --- | --- | --- |
| 검색 시점 | 매 메시지마다 | 에이전트가 결정 |
| 쿼리 생성 | QueryRewriter | 에이전트 자체 |
| 검색 횟수 | 턴당 1회 | 필요에 따라 1회 이상 |
| Tool 조합 | 해당 없음 | 등록된 모든 Tool |
| 설정 방법 | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **참고:** 에이전틱 RAG에서는 `QueryRewriter`가 의도적으로 우회됩니다. 에이전트가 자체적으로 독립적인 검색 쿼리를 생성하므로 별도의 재작성 단계는 불필요하며, 에이전트의 의도를 왜곡할 수 있습니다.

## 어떤 것을 선택할지

- **일반 RAG** — 모든 질문이 문서 기반이고, 단일 주제이며, 최소 지연 시간을 원할 때
- **에이전틱 RAG** — 질문이 여러 주제에 걸치거나, 문서 + 실시간 데이터 조합이 필요하거나, 반복 검색이 필요할 때
