# RAG (검색 증강 생성)

완성된 답변과 중지 버튼만 필요하면 `GetCompletionAsync`에 `cancellationToken`을 전달하세요. 진행 이벤트나 지원 모델의 추가 지시에는 Run을 사용합니다. [일반 응답 취소](completions.md#completion-cancellation)를 참고하세요.

검색한 문맥으로 답변할 때도 `RagEnabledService.GetCompletionAsync`에 `cancellationToken`을 전달하세요. 같은 토큰이 검색, `LlmQueryRewriter`, `LlmReranker`, 내부 완료 호출까지 이어지며, 검색 중 취소하면 다음 모델 호출을 진행하지 않습니다. `RagPipeline.QueryAndGenerateAsync`도 토큰을 전달합니다. 각 구성요소는 취소에 협조해야 하며, 이미 완료한 검색이나 도구 행동을 되돌리지는 않습니다.


`IAIService`로 참조한다면 `Mythosia.AI.Extensions`의 `GetLastProcessing()`을 사용하세요. 선택적 `IAIProcessingInfoService`를 읽으며 진단 기능이 없으면 빈 목록을 반환합니다. `IAIService`에 필수 멤버를 추가하지 않습니다. RAG에서는 `RagEnabledService.WithSpeed(...)`가 검색 후 작성하는 다음 답변에 적용되고 `LastProcessing`은 해당 답변의 처리 정보를 보여줍니다. 내부 질의 재작성은 분리하며 Run 결과에서도 같은 `Processing`을 읽습니다. [WithSpeed](request-building.md#inference-speed)

## RAG란?

RAG(Retrieval-Augmented Generation)는 AI 모델이 답변을 생성할 때, **내가 가진 문서에서 관련 정보를 먼저 찾아온 뒤** 그 정보를 바탕으로 답변하도록 하는 기술입니다.

도서관에서 리포트를 쓰는 상황을 떠올려 보세요. 모든 내용을 머릿속에서 꺼내는 것보다, 관련 책을 먼저 찾아 읽고 그 내용을 참고해서 쓰는 것이 훨씬 정확하겠죠? RAG가 바로 이 방식입니다.

## RAG가 필요한 이유

LLM(대규모 언어 모델)은 학습 데이터를 기반으로 답변하기 때문에 다음과 같은 한계가 있습니다:

- **최신 정보를 모릅니다** — 학습 시점 이후의 정보는 알 수 없습니다
- **내부 문서를 모릅니다** — 회사 정책, 제품 매뉴얼 같은 비공개 데이터에는 접근할 수 없습니다
- **환각(Hallucination)** — 모르는 내용도 그럴듯하게 지어내는 경우가 있습니다

RAG는 이런 한계를 해결합니다. 질문이 들어오면 먼저 내 문서에서 관련 정보를 검색하고, 그 결과를 프롬프트에 포함시켜 AI가 **근거 있는 답변**을 생성하도록 합니다.

## RAG의 동작 흐름

RAG는 크게 두 단계로 나뉩니다.

### 1단계: 문서 준비 (최초 한 번만 실행)

```
문서 파일 → 텍스트 분할(청킹) → 임베딩(벡터 변환) → 벡터 스토어에 저장
```

1. **[텍스트 분할](text-splitters.md)** — 긴 문서를 검색에 적합한 작은 조각(청크)으로 나눕니다
2. **임베딩** — 각 청크를 숫자 벡터로 변환합니다. 의미가 비슷한 텍스트는 비슷한 벡터가 됩니다
3. **저장** — 변환된 벡터를 [벡터 스토어](vectordb-overview.md)에 저장합니다

### 2단계: 질문 응답 (매 질문마다 실행)

아래는 기본 벡터 검색 모드의 흐름입니다. 키워드 모드는 질문 임베딩을 생략하고, 커스텀 검색기는 필요한 변환을 직접 선택합니다.

```
사용자 질문 → 질문을 임베딩 → 벡터 스토어에서 유사한 청크 검색 → 프롬프트에 주입 → AI 응답 생성
```

1. **질문 임베딩** — 사용자의 질문도 같은 방식으로 벡터로 변환합니다
2. **유사도 검색** — 벡터 스토어에서 질문과 가장 비슷한 청크들을 찾습니다
3. **프롬프트 구성** — 찾아온 청크들을 프롬프트에 넣어 AI에게 전달합니다
4. **답변 생성** — AI가 전달받은 문서 내용을 참고하여 답변을 생성합니다

검색한 자료로 답변을 작성하는 동안 텍스트를 표시하거나 실행을 중지하려면 `RagEnabledService.StartRunAsync`를 사용합니다. 검색은 실행 전에 한 번 수행하며, 추가 지시가 자동으로 재검색을 하지는 않습니다. 사용법은 [Run 사용 안내](execution-api-transition.md)를 참고하세요.

## 설치

```bash
dotnet add package Mythosia.AI.Rag
```

## 빠른 시작

Mythosia.AI에서는 이 모든 과정을 `.WithRag()` 한 줄로 설정할 수 있습니다:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("환불 정책이 어떻게 되나요?");
```

위 코드만으로 문서 분할 → 임베딩 → 저장 → 검색 → 프롬프트 주입이 자동으로 처리됩니다.

로컬 신경망 희소 검색과 기존 검색을 비교하려면 선택 패키지 `Mythosia.AI.Rag.Search.Pixie` 프리뷰를 사용하세요. 기존 의미 임베딩 공급자를 유지하며 PIXIE 색인은 메모리에 보관합니다. 영구 저장소의 변환이나 기본 검색의 자동 교체는 수행하지 않습니다. [PIXIE 연결과 비교 안내](rag-hybrid-search.md#pixie-search).

<a id="rag-message-attachments"></a>

## 첨부 이미지와 내 문서를 함께 참고해 답변하기

제품 사진을 매뉴얼과 함께 설명하려면 질문과 이미지가 담긴 `Message`를 `RagEnabledService.GetCompletionAsync(Message)` 또는 `StartRunAsync(Message)`에 전달하세요. 두 방식 모두 텍스트 외 첨부 내용을 내부 AI 서비스 요청에 보존합니다. 검색은 메시지의 텍스트를 기준으로 하며, 첨부 파일 자체를 자동으로 색인하거나 임베딩하지는 않습니다. 선택한 공급자와 모델이 해당 첨부 형식을 지원해야 합니다. 검색 문맥은 전송할 요청에만 추가하며, 원본 `Message`나 대화 기록의 사용자 텍스트를 검색 문맥으로 덮어쓰지 않습니다.

매뉴얼과 실시간 재고를 함께 참고해야 한다면 RAG와 등록한 도구를 함께 사용하세요. `GetCompletionAsync`로 도구를 호출하는 동안 검색 문맥은 최초 입력에 유지하고, 이후 도구 실행 결과도 그대로 모델에 전달합니다. 대화 기록에는 원래 사용자 입력을 보존합니다.

<a id="retrieval-modes"></a>

## 문서를 찾는 방식 선택하기

상품 코드는 키워드 검색이 유리하고, 문서와 표현이 다른 질문은 의미 검색이 필요합니다. 선택한 검색기는 각 방식에 필요한 처리만 실행합니다. 키워드 검색을 위해 질문 임베딩부터 만들 필요가 없습니다.

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()`는 질문 임베딩을 생략합니다. 문서 등록은 여전히 기존 벡터 저장소를 위해 청크를 나누고 임베딩을 생성합니다. 문서까지 키워드만으로 저장하는 API는 아닙니다. 지연 초기화로 첫 질문에서 문서를 등록하면 문서 임베딩 호출이 발생할 수 있습니다.

[검색 방식과 저장소 지원 범위](rag-hybrid-search.md), [커스텀 검색기](rag-pipeline.md#custom-retriever)를 확인하세요.

## 문서 추가

로컬 파일, URL, 직접 입력한 텍스트 등 다양한 방식으로 문서를 추가할 수 있습니다:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // 로컬 파일
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("인라인 콘텐츠도 여기에 추가할 수 있습니다.")   // 원시 문자열
)
```

`AddUrl`은 지원하는 HTTP 압축을 검증하고 풀어 텍스트를 읽으며, 불완전하거나 지원하지 않는 압축과 다중 압축은 거부합니다. [URL 압축 해제와 취소](rag-pipeline.md#url-documents)를 참고하세요.

<a id="document-identity"></a>

### 같은 이름의 파일을 서로 구분하기

두 회사가 각각 `docs/faq.txt`를 제공할 수 있습니다. 두 문서는 모두 색인에 남아야 하고, 같은 파일을 다시 등록할 때는 같은 문서로 식별되어야 합니다:

```csharp
var store = await RagStore.BuildAsync(rag => rag
    .AddDocuments("company-a/docs")
    .AddDocuments("company-b/docs"));
```

기본 RAG 저장 흐름은 DB에 전달하기 전에 문서 ID를 만들고, `document_id`가 같은 기존 데이터를 교체합니다. 이 라이브러리의 PostgreSQL(pgvector) 저장소는 전달받은 ID 조건으로 처리하며, 원본 파일 경로를 직접 확인해 구분하지는 않습니다. 이전에는 디렉터리 등록 시 `company-a/docs/faq.txt`와 `company-b/docs/faq.txt` 모두 `faq.txt`라는 ID가 되어 두 번째 문서가 첫 번째 문서를 교체했습니다. 이번 수정은 ID를 만들 때 전체 경로를 반영하는 것이며, PostgreSQL의 테이블 구조는 그대로입니다. 저장소 예제의 `full_path` 필터는 호출자가 직접 넣은 메타데이터를 이용하며, 고유한 문서 ID나 레코드 ID를 자동 생성해 주는 기능은 아닙니다.

기본 `PlainTextDocumentLoader`와 `DirectoryDocumentLoader`는 `Path.GetFullPath`로 정규화한 파일의 절대경로를 `Source`와 자동 문서 ID로 사용합니다. 따라서 서로 다른 폴더의 파일은 ID가 다릅니다. 상대경로·절대경로·`./` 경로도 대소문자까지 같은 절대경로로 해석되면 같은 ID를 사용합니다. 상대경로를 사용할 때는 작업 디렉터리를 일정하게 유지하세요. 파일 이동, 심볼릭 링크·하드 링크 또는 대소문자가 다른 경로까지 같은 ID를 보장하지는 않습니다.

`AddText(..., id: ...)`, 직접 지정한 `RagDocument.Id`, 커스텀 로더의 `Source` 규칙은 그대로입니다. 호출 API를 바꿀 필요는 없습니다. 이 기본 로더들의 `Source`가 절대경로가 되므로 기본 출처 표시에도 절대경로가 나올 수 있습니다. 화면에는 `filename` 또는 기본 디렉터리 로더의 `relative_path` 메타데이터를 활용하세요. 설정 콜백을 받는 디렉터리 등록 오버로드는 `relative_path`를 자동으로 추가하지 않습니다.

**기존 색인 전환:** 이전 상대경로 ID는 자동 삭제하거나 변환하지 않습니다. 새 컬렉션에 전체 문서를 다시 색인하고 검증한 뒤 애플리케이션을 전환하는 방법을 권장합니다. 기존 컬렉션을 재사용한다면 소유 관계를 확인한 이전 문서 ID만 삭제하고 해당 원본 파일을 다시 색인하세요. 파일명만으로 광범위하게 삭제하면 다른 폴더의 같은 이름 문서까지 영향을 받을 수 있습니다.

갱신·삭제 범위가 의도한 문서와 일치하도록 `document_id`는 파이프라인 예약 키로 사용합니다. 입력 메타데이터에 다른 값이 있어도 저장할 각 레코드에는 실제 `RagDocument.Id`를 넣습니다. 입력 문서와 분할기가 제공한 메타데이터 사전 자체는 변경하지 않으며, 커스텀 저장 콜백도 정규화된 레코드를 받습니다. 애플리케이션용 식별자는 다른 키에 저장하세요.

이미 잘못된 `document_id`로 저장된 레코드는 자동으로 복구되지 않습니다. 신뢰할 수 있는 원문으로 새 컬렉션을 만들거나, 영향을 받은 레코드의 소유 문서를 확인해 해당 레코드만 정리한 뒤 재색인하세요. 올바른 ID로 다시 등록하는 것만으로 다른 ID 아래에 저장된 기존 레코드까지 찾을 수는 없습니다.

같은 파일을 상대 경로와 절대 경로로 등록해도 하나의 문서를 갱신해야 하고, 폴더가 다른 동명 파일은 구분해야 합니다. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader`, `PdfDocumentLoader`는 TXT 기본 로더처럼 `DoclingDocument.Source`에 정규화된 절대 파일 경로를 넣습니다. RAG는 이 값에서 자동 문서 ID를 만들며, 명시적으로 지정한 ID는 호출자가 관리합니다. 기본 출처 표시에 절대 경로가 나타날 수 있습니다.

[같은 파일의 등록 경로가 달라도 문서 ID 유지하기](document-loaders.md#file-source-identity).

<a id="empty-document-updates"></a>

### 빈 문서로 갱신할 때 이전 검색 내용도 지우기

폐기한 환불 안내를 비운 뒤 같은 문서를 다시 색인했다면, 이전 안내가 답변에 계속 사용되어서는 안 됩니다. 기본 RAG 저장 흐름에서는 분할이 정상적으로 끝나고 청크가 0개이면 해당 `document_id`의 기존 레코드를 빈 목록으로 교체합니다. 임베딩을 요청하지 않으며 다른 문서 ID의 데이터는 유지합니다. 빈 문자열이나 공백 문서를 분할해 청크가 0개인 경우뿐 아니라, 커스텀 splitter가 정상적으로 0개를 반환한 경우도 같습니다.

설정이 완료된 `RagPipeline` 인스턴스 `pipeline`에서 저장된 문서 ID를 그대로 사용합니다:

```csharp
await pipeline.IndexDocumentAsync(
    new RagDocument { Id = "refund-policy", Content = "" },
    cancellationToken);
```

나중에 같은 ID로 내용이 있는 문서를 다시 색인할 수 있습니다. 로더가 문서를 하나도 반환하지 않거나 다음 파일 목록에서 문서가 빠진 것은 삭제 지시가 아닙니다. 교체할 문서 ID가 전달되지 않았으므로 자동 삭제하지 않습니다.

로딩·파싱·분할 과정에서 예외가 발생하거나 저장 호출 전에 취소가 확인되면 해당 문서의 기존 레코드를 유지합니다. 로더와 파서는 실패를 예외로 알려야 합니다. 정상 반환된 청크 0개만으로는 의도적으로 비운 문서와 실패를 구분할 수 없습니다. 저장이 시작된 이후 실패·취소의 롤백은 저장소 구현에 따릅니다. PostgreSQL 교체는 트랜잭션을 사용합니다. 일괄 색인은 문서별로 처리하므로 앞서 완료된 문서까지 되돌리지는 않습니다.

**커스텀 저장:** `onDocumentEmbedded`를 제공하면 저장은 계속 해당 콜백의 책임입니다. 청크가 0개이면 콜백을 호출하지 않고 기본 저장소에도 접근하지 않습니다. 애플리케이션이 알고 있는 문서 ID로 자체 저장소에서 직접 삭제하거나, 파이프라인의 저장소를 대상으로 `DeleteDocumentAsync`를 호출해야 합니다.

## 커스텀 임베딩 프로바이더

기본적으로는 내장 로컬 임베딩 프로바이더를 사용합니다. 임베딩 전용 모델을 따로 지정하고 싶다면 다음과 같이 설정합니다:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## 커스텀 벡터 스토어

기본적으로는 인메모리 스토어를 사용하므로 앱을 재시작하면 데이터가 사라집니다. 프로덕션 환경에서는 데이터를 영구적으로 보관할 수 있는 벡터 스토어를 연결하세요:

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## 쿼리 옵션

검색할 때 몇 개의 청크를 가져올지, 최소 유사도는 얼마로 할지 등을 조절할 수 있습니다:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,           // 가져올 청크 수 (기본 5개)
        MinScore = 0.7      // 이 점수 이상인 청크만 가져옴
    }
};

var response = await service.GetCompletionAsync("질문", options: options);
```

## 다음 단계

공급자가 이미 문서 색인을 관리한다면 [내장 파일 검색과 RAG](reasoning-and-search.md)를 비교해 검색을 누가 담당할지 정하세요. 같은 가이드에서 RAG 답변에 적용하는 추론 옵션과 공급자의 출처 수집도 설명합니다.

기본 RAG를 익혔다면, 다음 기능들로 검색 품질을 한 단계 높여보세요:

- [하이브리드 검색](rag-hybrid-search.md) — 의미 검색과 키워드 검색을 동시에
- [쿼리 재작성](rag-query-rewriting.md) — 대화 맥락을 반영한 검색 쿼리 최적화
- [재순위](rag-reranking.md) — 검색 결과의 정확도를 한 번 더 높이기
- [파이프라인 커스터마이징](rag-pipeline.md) — RAG 동작 과정을 세밀하게 제어
- [에이전틱 RAG](rag-agentic.md) — AI가 스스로 판단해서 검색하는 지능형 RAG
- [벡터 스토어](../vectordb-overview.md) — 영구 저장소 설정
- [텍스트 분할기](text-splitters.md) — 문서를 나누는 방식 변경

Perplexity: [직접 관리하는 문서 색인에 벡터 사용하기 / 답변을 만들지 않고 검색하기](perplexity.md).
