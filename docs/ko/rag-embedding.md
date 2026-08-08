# 임베딩

> 📍 **질문 응답 파이프라인:** [쿼리 재작성](rag-query-rewriting.md) → **`임베딩`** → [필터링](rag-filtering.md) → [검색](rag-hybrid-search.md) → [재순위](rag-reranking.md) → [컨텍스트 구성](rag-context-build.md)

## 임베딩이란?

임베딩은 텍스트를 **숫자 벡터**(숫자 배열)로 변환하는 과정입니다. 변환된 벡터는 고차원 공간에 배치되며, **의미가 비슷한 텍스트끼리 가까운 위치에 모입니다**.

지도에 도시를 배치하는 걸 떠올려보세요. 서울과 인천은 지리적으로 가까우니 지도에서도 가깝게 표시됩니다. 마찬가지로 "구독 해지 방법"과 "멤버십을 끝내고 싶어요"는 전혀 다른 단어를 쓰고 있지만, 의미가 비슷하기 때문에 가까운 벡터를 생성합니다.

RAG 파이프라인에서 임베딩은 두 곳에서 사용됩니다:

1. **문서 인덱싱 시** — 각 청크를 벡터로 변환해 벡터 스토어에 저장
2. **쿼리 시** — 사용자의 질문을 벡터로 변환해 저장된 청크와 유사도 비교

이 페이지에서는 **쿼리 시점의 임베딩**(2번)을 상세히 설명합니다.

## 내장 임베딩 프로바이더

Mythosia.AI.Rag에는 4가지 프로바이더가 포함되어 있습니다. 용도에 맞게 골라 쓰세요.

### OpenAI Embedding

가장 대중적인 클라우드 기반 옵션입니다. 품질이 높지만 API 키가 필요합니다:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",   // 기본값
    dimensions: 1536                    // 기본값
);
```

빌더 단축 구문도 사용할 수 있습니다:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

### Ollama (로컬 실행)

데이터를 외부로 보내지 않고 로컬에서 임베딩을 실행합니다. 머신에 [Ollama](https://ollama.com/)가 실행 중이어야 합니다:

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",       // 기본값
    dimensions: 1024,                    // 기본값
    baseUrl: "http://localhost:11434"    // 기본값
);
```

### vLLM (셀프호스팅)

[vLLM](https://docs.vllm.ai/)으로 자체 임베딩 서버를 운영하는 팀을 위한 옵션입니다:

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B", // 기본값
    dimensions: 1024,                     // 기본값
    baseUrl: "http://localhost:8002"      // 기본값
);
```

### Local (API 불필요)

특징 해싱 기반의 경량 프로바이더로, API 키나 외부 서비스가 필요 없습니다. 하지만 임베딩 품질이 뉴럴 모델에 비해 크게 떨어지므로 **실제 사용에는 추천하지 않습니다**.

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **팁:** 대신 `OpenAIEmbeddingProvider`의 `text-embedding-3-small` 모델을 사용하세요. 무료에 가까울 정도로 매우 저렴하면서 훨씬 좋은 결과를 얻을 수 있습니다.

## 배치 처리

문서 인덱싱 시, 파이프라인은 수천 개의 텍스트를 한 번에 보내는 대신 배치 단위로 임베딩합니다. 배치 크기는 설정 가능합니다:

```csharp
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100; // 기본값: API 호출 1회당 100개 청크
pipeline.Options = options;
```

배치 크기가 클수록 API 호출 횟수는 줄지만, 호출당 메모리 사용량이 늘어납니다. API 속도 제한이나 메모리 문제가 발생하면 이 값을 줄여보세요.

## 벡터 차원 수

`Dimensions` 속성은 각 임베딩 벡터의 크기를 결정합니다. 이게 중요한 이유는:

- **벡터 스토어와 반드시 일치해야 합니다** — 임베딩이 1536차원이면 벡터 스토어 컬럼도 1536이어야 합니다
- **차원이 높을수록 = 더 정밀** — 대신 저장 공간이 늘고 검색이 느려집니다
- **차원이 낮을수록 = 더 빠름** — 대신 미묘한 의미 차이를 놓칠 수 있습니다

주요 모델별 기본 차원 수:

| 프로바이더 | 모델 | 기본 차원 수 |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 (32–2560) |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Local | (특징 해싱) | 1024 |

## 커스텀 임베딩 프로바이더

다른 임베딩 서비스를 사용하려면 `IEmbeddingProvider`를 구현합니다:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // 임베딩 API 호출
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // 배치 임베딩 호출
    }
}
```

빌더에 등록합니다:

```csharp
.WithRag(rag => rag
    .UseEmbedding(new MyEmbeddingProvider())
    .AddDocument("docs.txt")
)
```

## 내부 동작

`QueryAsync`가 실행되면 임베딩 단계는 딱 한 가지만 수행합니다:

```
사용자 질문 (문자열) → EmbeddingProvider.GetEmbeddingAsync() → 쿼리 벡터 (float[])
```

이 쿼리 벡터는 다음 단계인 [필터링](rag-filtering.md)으로 전달되고, 메타데이터 필터와 함께 [검색](rag-hybrid-search.md)에서 유사도 검색이 수행됩니다.

## 다음 단계

- [필터링](rag-filtering.md) — 검색 대상 청크를 좁히기
- [검색 (하이브리드 검색)](rag-hybrid-search.md) — 벡터 검색과 키워드 검색을 동시에
- [파이프라인 커스터마이징](rag-pipeline.md) — 임베딩 프로바이더를 여러 서비스에서 공유
