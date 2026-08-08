# RAG (검색 증강 생성)

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

```
사용자 질문 → 질문을 임베딩 → 벡터 스토어에서 유사한 청크 검색 → 프롬프트에 주입 → AI 응답 생성
```

1. **질문 임베딩** — 사용자의 질문도 같은 방식으로 벡터로 변환합니다
2. **유사도 검색** — 벡터 스토어에서 질문과 가장 비슷한 청크들을 찾습니다
3. **프롬프트 구성** — 찾아온 청크들을 프롬프트에 넣어 AI에게 전달합니다
4. **답변 생성** — AI가 전달받은 문서 내용을 참고하여 답변을 생성합니다

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

## 문서 추가

로컬 파일, URL, 직접 입력한 텍스트 등 다양한 방식으로 문서를 추가할 수 있습니다:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // 로컬 파일
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("인라인 콘텐츠도 여기에 추가할 수 있습니다.")   // 원시 문자열
)
```

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

기본 RAG를 익혔다면, 다음 기능들로 검색 품질을 한 단계 높여보세요:

- [하이브리드 검색](rag-hybrid-search.md) — 의미 검색과 키워드 검색을 동시에
- [쿼리 재작성](rag-query-rewriting.md) — 대화 맥락을 반영한 검색 쿼리 최적화
- [재순위](rag-reranking.md) — 검색 결과의 정확도를 한 번 더 높이기
- [파이프라인 커스터마이징](rag-pipeline.md) — RAG 동작 과정을 세밀하게 제어
- [에이전틱 RAG](rag-agentic.md) — AI가 스스로 판단해서 검색하는 지능형 RAG
- [벡터 스토어](../vectordb-overview.md) — 영구 저장소 설정
- [텍스트 분할기](text-splitters.md) — 문서를 나누는 방식 변경
