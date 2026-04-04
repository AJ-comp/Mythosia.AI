# 에이전틱 RAG

## 일반 RAG의 한계

일반 RAG는 단순하고 빠릅니다. 질문이 들어오면 무조건 **한 번** 검색하고, 결과를 프롬프트에 넣고, 답변을 생성합니다. 하지만 이 "무조건 한 번" 방식은 복잡한 상황에서 한계가 드러납니다:

- **"하드웨어와 소프트웨어의 환불 정책을 비교해 주세요"** → 한 번의 검색으로는 두 주제를 모두 커버하기 어렵습니다
- **첫 번째 검색 결과가 부족할 때** → 일반 RAG는 재시도 없이 부족한 결과 그대로 답변합니다
- **"지금까지 대화를 요약해 주세요"** → 문서 검색이 전혀 필요 없는 질문인데도 검색을 실행합니다
- **문서 + 실시간 데이터가 동시에 필요할 때** → 일반 RAG는 문서 검색만 할 수 있고, API 호출 같은 작업은 불가능합니다

## 에이전틱 RAG란?

에이전틱 RAG는 이 한계를 해결합니다. 고정된 "검색 → 답변" 파이프라인 대신, **AI 에이전트가 직접 판단**합니다:

- 지금 문서 검색이 필요한지?
- 무엇을 검색할지?
- 검색 결과가 충분한지, 한 번 더 검색할지?
- 문서 검색 대신 다른 Tool(API 호출 등)을 사용할지?

이 모든 판단이 **ReAct 루프** 안에서 자동으로 이루어집니다. 사람이 도서관에서 자료를 찾을 때 "이 책으로 부족하니 다른 서가도 찾아보자"라고 스스로 판단하는 것과 비슷합니다.

## 빠른 시작

`WithAgenticRag`으로 문서 인덱스를 Tool로 등록하고, `RunAgentAsync`로 에이전트를 실행합니다:

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

에이전트는 문서가 필요하다고 판단하면 자동으로 `search_documents`를 호출하고, 검색된 내용을 바탕으로 최종 답변을 생성합니다.

## 다른 Tool과 조합하기

에이전틱 RAG의 진짜 강점은 **문서 검색과 다른 Tool을 함께 쓸 수 있다**는 점입니다. 에이전트가 질문의 각 부분에 필요한 Tool을 스스로 골라서 사용합니다:

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

위 예제에서 에이전트는 자율적으로 다음 과정을 수행합니다:

1. 문서에서 환불 정책을 검색
2. 주문 API를 호출하여 주문 #12345의 상태를 조회
3. 두 정보를 종합하여 최종 답변을 생성

## Tool 설명 커스터마이징

에이전트는 Tool의 설명(description)을 보고 "이 상황에서 이 Tool을 써야 할까?"를 판단합니다. 그래서 Tool 설명을 도메인에 맞게 구체적으로 작성하는 것이 중요합니다:

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "사내 HR 정책, 제품 매뉴얼, 컴플라이언스 문서를 검색합니다. " +
        "회사 정책이나 제품 관련 정보가 필요할 때 호출하세요.");
```

"문서 검색"처럼 모호하게 쓰면 에이전트가 RAG를 너무 자주 또는 너무 드물게 호출할 수 있습니다. 문서에 **어떤 종류의 정보**가 담겨 있는지 구체적으로 적어주세요.

## 일반 RAG와의 비교

| | 일반 RAG | 에이전틱 RAG |
| --- | --- | --- |
| 검색 시점 | 매 메시지마다 무조건 | 에이전트가 필요할 때만 |
| 쿼리 생성 | QueryRewriter가 처리 | 에이전트가 직접 생성 |
| 검색 횟수 | 턴당 1회 고정 | 필요에 따라 여러 번 |
| Tool 조합 | 문서 검색만 가능 | API 호출 등 다른 Tool과 자유롭게 조합 |
| 설정 방법 | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **참고:** 에이전틱 RAG에서는 `QueryRewriter`가 의도적으로 사용되지 않습니다. 에이전트가 스스로 독립적인 검색 쿼리를 만들기 때문에, 별도의 재작성 단계는 불필요하며 오히려 에이전트의 의도를 왜곡할 수 있습니다.

## 어떤 걸 선택해야 할까?

- **일반 RAG** — 모든 질문이 문서 기반이고, 한 가지 주제에 대한 질문이며, 빠른 응답이 중요할 때
- **에이전틱 RAG** — 질문이 여러 주제에 걸치거나, 문서 + 실시간 데이터를 함께 써야 하거나, 상황에 따라 유연한 검색이 필요할 때
