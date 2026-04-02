# RAG (검색 증강 생성)

RAG는 쿼리 시점에 관련 청크를 검색하여 모델이 내 문서를 기반으로 질문에 답할 수 있게 합니다.

## 설치

```bash
dotnet add package Mythosia.AI.Rag
```

## 빠른 시작

임의의 `IAIService`에서 `.WithRag()`를 사용해 플루언트 API로 RAG를 활성화합니다:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("환불 정책이 어떻게 되나요?");
```

문서는 자동으로 분할, 임베딩, 저장됩니다. 쿼리 시점에 가장 관련성 높은 청크를 검색해 프롬프트에 주입합니다.

## 문서 추가

여러 소스 타입을 지원합니다:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // 로컬 파일
    .AddDocument("https://example.com/doc.txt")   // URL
    .AddText("인라인 콘텐츠도 여기에 추가할 수 있습니다.")   // 원시 문자열
)
```

## 커스텀 임베딩 프로바이더

기본적으로 RAG는 서비스 자체 프로바이더를 임베딩에 사용합니다. 전용 임베딩 모델을 사용하려면:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbeddingProvider(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## 커스텀 벡터 스토어

기본적으로 인메모리 스토어를 사용합니다. 프로덕션 환경에서는 영구 벡터 스토어를 연결합니다:

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(connectionString, embedDimension: 1536);

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseVectorStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## 쿼리 옵션

쿼리별 검색 동작을 세부 조정합니다:

```csharp
var options = new RagQueryOptions
{
    TopK = 5,               // 검색할 청크 수
    ScoreThreshold = 0.7f   // 최소 유사도 점수
};

var response = await service.GetCompletionAsync("질문", ragOptions: options);
```

## 다음 단계

- [벡터 스토어](../vectordb-overview.md) — 개요 및 백엔드 설정
- [텍스트 분할기](text-splitters.md) — 문서 청크 방식 커스터마이즈
- [고급 RAG](rag-advanced.md) — 하이브리드 검색, 재순위, 쿼리 재작성
